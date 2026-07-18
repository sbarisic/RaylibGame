using System.Numerics;

namespace Voxelgine.Audio.Tests;

public sealed class AudioSystemTests
{
    [Fact]
    public void PlayCue_SeventhVoiceStealsOldestForVariant()
    {
        FakeBackend backend = new();
        using AudioSystem system = new(backend, new FakeRandom());
        system.RegisterCue(CreateCue("step", "step.wav"));

        AudioVoiceHandle[] voices = Enumerable.Range(0, 7)
            .Select(_ => system.PlayCue("step", AudioEmitter.NonSpatial()))
            .ToArray();

        Assert.All(voices, static voice => Assert.True(voice.IsValid));
        Assert.Contains(voices[0].Value, backend.StoppedVoices);
        Assert.DoesNotContain(voices[0].Value, backend.ActiveVoices);
        Assert.Equal(6, backend.ActiveVoices.Count);
    }

    [Fact]
    public void PlayCue_UsesInjectedVariantRandomness()
    {
        FakeBackend backend = new();
        FakeRandom random = new(nextValue: 1);
        using AudioSystem system = new(backend, random);
        system.RegisterCue(new AudioCueDefinition
        {
            CueId = "step",
            Variants =
            [
                new AudioCueVariant { Path = "one.wav" },
                new AudioCueVariant { Path = "two.wav" }
            ]
        });

        system.PlayCue("step", AudioEmitter.NonSpatial());

        Assert.EndsWith("two.wav", backend.LastPlayedPath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PlayCue_PreservesCueEmitterAndVariantGains()
    {
        FakeBackend backend = new();
        using AudioSystem system = new(backend, new FakeRandom());
        system.RegisterCue(new AudioCueDefinition
        {
            CueId = "shot",
            Gain = 0.5f,
            Variants = [new AudioCueVariant { Path = "shot.wav", Gain = 0.25f }]
        });

        system.PlayCue(
            "shot",
            new AudioEmitter(Vector3.One, Vector3.Zero, Gain: 0.4f));

        Assert.Equal(0.05f, backend.LastPlayback.Gain, 5);
    }

    [Fact]
    public void SetGain_PreservesCueAndVariantGain()
    {
        FakeBackend backend = new();
        using AudioSystem system = new(backend, new FakeRandom());
        system.RegisterCue(new AudioCueDefinition
        {
            CueId = "ambience",
            Gain = 0.6f,
            Variants =
            [
                new AudioCueVariant
                {
                    Path = "ambience.flac",
                    Gain = 0.5f
                }
            ]
        });

        AudioVoiceHandle voice = system.PlayCue(
            "ambience",
            AudioEmitter.NonSpatial(0.0f));
        system.SetGain(voice, 0.25f, 1.0f);

        Assert.Equal(0.075f, backend.LastSetGain, 5);
        Assert.Equal(1_000u, backend.LastGainFadeMilliseconds);
    }

    [Fact]
    public void FadingVoice_ReleasesItsInstanceSlotBeforeReplacementStarts()
    {
        FakeBackend backend = new();
        using AudioSystem system = new(backend, new FakeRandom());
        system.RegisterCue(new AudioCueDefinition
        {
            CueId = "campfire",
            Variants = [new AudioCueVariant { Path = "campfire.flac" }],
            MaxInstancesPerVariant = 4
        });

        AudioVoiceHandle[] voices = Enumerable.Range(0, 4)
            .Select(_ => system.PlayCue("campfire", AudioEmitter.NonSpatial()))
            .ToArray();

        system.Stop(voices[3], 1.0f);
        AudioVoiceHandle replacement = system.PlayCue(
            "campfire",
            AudioEmitter.NonSpatial());

        Assert.True(replacement.IsValid);
        Assert.Equal([voices[3].Value], backend.StoppedVoices);
        Assert.Contains(voices[0].Value, backend.ActiveVoices);
    }

    [Fact]
    public void UnknownCue_IsLoggedOnlyOnce()
    {
        List<string> log = [];
        using AudioSystem system = new(
            new FakeBackend(),
            new FakeRandom(),
            log.Add);

        system.PlayCue("missing", AudioEmitter.NonSpatial());
        system.PlayCue("missing", AudioEmitter.NonSpatial());

        Assert.Single(log);
    }

    [Fact]
    public void MissingVariant_DegradesToSilence()
    {
        List<string> log = [];
        FakeBackend backend = new() { FailLoads = true };
        using AudioSystem system = new(backend, new FakeRandom(), log.Add);
        system.RegisterCue(CreateCue("missing", "missing.wav"));

        AudioVoiceHandle voice = system.PlayCue("missing", AudioEmitter.NonSpatial());

        Assert.False(voice.IsValid);
        Assert.Equal(2, log.Count);
    }

    [Fact]
    public void DroppedCompletion_ReconcilesVoiceAndDisposesOwnedStreamClip()
    {
        FakeBackend backend = new();
        using AudioSystem system = new(backend, new FakeRandom());
        AudioStreamHandle stream = system.PlayStream(new AudioStreamDefinition
        {
            Path = "stream.wav",
            Looping = false
        });

        backend.CompleteWithoutEvent(stream.Value);
        system.Update(0);

        Assert.Equal(1, backend.DisposedClipCount);
        Assert.Empty(backend.ActiveVoices);
    }

    private static AudioCueDefinition CreateCue(string id, string path) => new()
    {
        CueId = id,
        Variants = [new AudioCueVariant { Path = path }],
        MaxInstancesPerVariant = 6
    };
}

internal sealed class FakeRandom : IAudioRandom
{
    private readonly int _nextValue;
    private readonly double _nextDouble;

    public FakeRandom(int nextValue = 0, double nextDouble = 0.5)
    {
        _nextValue = nextValue;
        _nextDouble = nextDouble;
    }

    public int Next(int exclusiveMaximum) => Math.Min(_nextValue, exclusiveMaximum - 1);

    public double NextDouble() => _nextDouble;
}

internal sealed class FakeBackend : IAudioBackend
{
    private ulong _nextVoice = 1;
    private readonly Queue<BackendEvent> _events = [];

    public bool FailLoads { get; init; }

    public bool IsAvailable => true;

    public HashSet<ulong> ActiveVoices { get; } = [];

    public List<ulong> StoppedVoices { get; } = [];

    public int DisposedClipCount { get; private set; }

    public ulong DroppedEvents { get; private set; }

    public string LastPlayedPath { get; private set; } = string.Empty;

    public BackendPlayback LastPlayback { get; private set; }

    public float LastSetGain { get; private set; }

    public uint LastGainFadeMilliseconds { get; private set; }

    public BackendClip? LoadClip(string path, bool streamed, AudioSpatialMode spatialMode) =>
        FailLoads ? null : new FakeClip(path, () => DisposedClipCount++);

    public ulong Play(BackendClip clip, in BackendPlayback playback)
    {
        LastPlayedPath = ((FakeClip)clip).Path;
        LastPlayback = playback;
        ulong voice = _nextVoice++;
        ActiveVoices.Add(voice);
        return voice;
    }

    public bool IsVoiceActive(ulong voice) => ActiveVoices.Contains(voice);

    public void Stop(ulong voice, uint fadeMilliseconds)
    {
        StoppedVoices.Add(voice);
        ActiveVoices.Remove(voice);
        _events.Enqueue(new BackendEvent(
            NativeEventType.VoiceStopped,
            voice,
            NativeResult.Success,
            0));
    }

    public void CompleteWithoutEvent(ulong voice)
    {
        ActiveVoices.Remove(voice);
        DroppedEvents++;
    }

    public void SetPaused(ulong voice, bool paused) { }

    public void SetGain(ulong voice, float gain, uint fadeMilliseconds)
    {
        LastSetGain = gain;
        LastGainFadeMilliseconds = fadeMilliseconds;
    }

    public void Seek(ulong voice, float seconds) { }

    public void SetListener(in AudioListener listener) { }

    public void SetBusGain(AudioBus bus, float gain) { }

    public void StopAll(AudioBus? bus, uint fadeMilliseconds)
    {
        foreach (ulong voice in ActiveVoices.ToArray())
        {
            Stop(voice, fadeMilliseconds);
        }
    }

    public void Update(float deltaSeconds) { }

    public bool TryPollEvent(out BackendEvent audioEvent) =>
        _events.TryDequeue(out audioEvent);

    public BackendPcmStream CreatePcmStream(
        AudioFormat format,
        AudioStreamOptions options) => throw new NotSupportedException();

    public AudioSystemStats GetStats() => new(
        (uint)ActiveVoices.Count,
        0,
        0,
        (ulong)StoppedVoices.Count,
        0,
        DroppedEvents);

    public void Dispose() { }

    private sealed class FakeClip : BackendClip
    {
        private readonly Action _onDispose;
        private bool _disposed;

        public FakeClip(string path, Action onDispose)
        {
            Path = path;
            _onDispose = onDispose;
        }

        public string Path { get; }

        public override bool WasDownmixed => false;

        public override uint SourceChannels => 1;

        public override void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _onDispose();
        }
    }
}
