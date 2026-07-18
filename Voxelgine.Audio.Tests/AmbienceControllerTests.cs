using System.Numerics;

namespace Voxelgine.Audio.Tests;

public sealed class AmbienceControllerTests
{
    [Fact]
    public void Update_StartsOutdoorLayersAndFourNearestCampfires()
    {
        RecordingAudioSystem audio = new();
        using AmbienceController controller = CreateController(audio);
        Vector3[] campfires = Enumerable.Range(1, 6)
            .Select(index => new Vector3(index, 0, 0))
            .ToArray();
        AmbienceSample sample = new(
            1,
            1,
            1,
            false,
            Vector3.Zero,
            campfires);

        controller.Update(0.2f, sample);

        Assert.Contains(audio.Plays, play => play.CueId == "ambience.wind");
        Assert.Contains(audio.Plays, play => play.CueId == "ambience.birds");
        RecordingAudioSystem.PlayRecord[] firePlays = audio.Plays
            .Where(play => play.CueId == "ambience.campfire")
            .ToArray();
        Assert.Equal(4, firePlays.Length);
        Assert.Equal(
            [1f, 2f, 3f, 4f],
            firePlays.Select(play => play.Emitter.Position.X).ToArray());
        Assert.All(audio.GainChanges, change => Assert.Equal(1.0f, change.FadeSeconds));
    }

    [Fact]
    public void Update_UsesOutdoorHysteresisAndUnderwaterCrossfade()
    {
        RecordingAudioSystem audio = new();
        using AmbienceController controller = CreateController(audio);

        controller.Update(0.2f, Sample(exposure: 1, underwater: false));
        int windStarts = audio.Plays.Count(play => play.CueId == "ambience.wind");
        controller.Update(0.2f, Sample(exposure: 0.4f, underwater: true));

        Assert.Equal(
            windStarts,
            audio.Plays.Count(play => play.CueId == "ambience.wind"));
        Assert.Contains(audio.Plays, play => play.CueId == "ambience.underwater");

        controller.Update(0.2f, Sample(exposure: 0.3f, underwater: false));

        Assert.Contains(audio.Stops, stop => stop.FadeSeconds == 1.0f);
    }

    private static AmbienceController CreateController(RecordingAudioSystem audio) => new(
        audio,
        new AmbienceControllerOptions
        {
            ExposureSmoothingSeconds = 0,
            UpdateRateHz = 5,
            CrossfadeSeconds = 1,
            MaximumCampfires = 4,
            CampfireRange = 48
        });

    private static AmbienceSample Sample(float exposure, bool underwater) => new(
        exposure,
        exposure,
        1,
        underwater,
        Vector3.Zero,
        Array.Empty<Vector3>());
}

internal sealed class RecordingAudioSystem : IAudioSystem
{
    private ulong _nextVoice = 1;

    public bool IsAvailable => true;

    public List<PlayRecord> Plays { get; } = [];

    public List<GainRecord> GainChanges { get; } = [];

    public List<StopRecord> Stops { get; } = [];

    public void RegisterCue(AudioCueDefinition cue) { }

    public void SetListener(in AudioListener listener) { }

    public AudioVoiceHandle PlayCue(string cueId, in AudioEmitter emitter)
    {
        AudioVoiceHandle handle = new(_nextVoice++);
        Plays.Add(new PlayRecord(cueId, emitter, handle));
        return handle;
    }

    public AudioStreamHandle PlayStream(AudioStreamDefinition stream) => default;

    public IAudioPcmStream CreatePcmStream(
        AudioFormat format,
        AudioStreamOptions options) => throw new NotSupportedException();

    public void SetBusGain(AudioBus bus, float gain) { }

    public void Update(float deltaSeconds) { }

    public void StopAll(AudioBus? bus = null, float fadeSeconds = 0) { }

    public void Stop(AudioVoiceHandle voice, float fadeSeconds = 0) =>
        Stops.Add(new StopRecord(voice, fadeSeconds));

    public void Stop(AudioStreamHandle stream, float fadeSeconds = 0) { }

    public void SetGain(AudioVoiceHandle voice, float gain, float fadeSeconds = 0) =>
        GainChanges.Add(new GainRecord(voice, gain, fadeSeconds));

    public void SetGain(AudioStreamHandle stream, float gain, float fadeSeconds = 0) { }

    public void SetPaused(AudioStreamHandle stream, bool paused) { }

    public void Seek(AudioStreamHandle stream, float seconds) { }

    public void Dispose() { }

    public readonly record struct PlayRecord(
        string CueId,
        AudioEmitter Emitter,
        AudioVoiceHandle Handle);

    public readonly record struct GainRecord(
        AudioVoiceHandle Voice,
        float Gain,
        float FadeSeconds);

    public readonly record struct StopRecord(
        AudioVoiceHandle Voice,
        float FadeSeconds);
}
