namespace Voxelgine.Audio;

public sealed class AudioSystem : IAudioSystem
{
    private readonly Dictionary<string, CueRuntime> _cues =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<ulong, VoiceRuntime> _voices = [];
    private readonly HashSet<OwnedPcmStream> _pcmStreams = [];
    private readonly HashSet<string> _loggedMessages = new(StringComparer.Ordinal);
    private readonly IAudioRandom _random;
    private readonly Action<string> _logger;
    private IAudioBackend _backend;
    private ulong _observedDroppedEvents;
    private bool _disposed;

    public AudioSystem(AudioSystemOptions? options = null)
    {
        options ??= new AudioSystemOptions();
        ValidateOptions(options);
        _logger = options.Log ?? (_ => { });
        _random = new SystemAudioRandom(options.RandomSeed);

        try
        {
            _backend = new NativeAudioBackend(options, LogOnce);
            AudioListener listener = AudioListener.Default;
            _backend.SetListener(listener);
        }
        catch (Exception exception) when (
            !options.ThrowOnInitializationFailure && IsInitializationFailure(exception))
        {
            _backend = new NullAudioBackend();
            LogOnce(
                $"Audio is unavailable and will run silently: {exception.Message}");
        }
    }

    internal AudioSystem(
        IAudioBackend backend,
        IAudioRandom random,
        Action<string>? logger = null)
    {
        _backend = backend;
        _random = random;
        _logger = logger ?? (_ => { });
    }

    public bool IsAvailable => !_disposed && _backend.IsAvailable;

    public AudioSystemStats Stats => _disposed ? default : _backend.GetStats();

    public void RegisterCue(AudioCueDefinition cue)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(cue);
        ValidateCue(cue);

        if (_cues.Remove(cue.CueId, out CueRuntime? existing))
        {
            DisposeCue(existing);
        }

        List<VariantRuntime> variants = new(cue.Variants.Count);
        foreach (AudioCueVariant variant in cue.Variants)
        {
            string path = Path.GetFullPath(variant.Path);
            BackendClip? clip = _backend.LoadClip(
                path,
                cue.Streamed,
                cue.SpatialMode);
            if (clip is null)
            {
                LogOnce($"Audio file '{path}' could not be loaded; that variant will be silent.");
            }

            variants.Add(new VariantRuntime(variant, path, clip));
        }

        _cues.Add(cue.CueId, new CueRuntime(cue, variants));
    }

    public void SetListener(in AudioListener listener)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidateFinite(listener.Position, nameof(listener));
        ValidateFinite(listener.Forward, nameof(listener));
        ValidateFinite(listener.Up, nameof(listener));
        ValidateFinite(listener.Velocity, nameof(listener));
        _backend.SetListener(listener);
    }

    public AudioVoiceHandle PlayCue(string cueId, in AudioEmitter emitter)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(cueId);
        ValidateEmitter(emitter);

        if (!_cues.TryGetValue(cueId, out CueRuntime? cue))
        {
            LogOnce($"Unknown audio cue '{cueId}'; playback will be silent.");
            return default;
        }

        List<VariantRuntime> available = cue.Variants
            .Where(static variant => variant.Clip is not null)
            .ToList();
        if (available.Count == 0)
        {
            LogOnce($"Audio cue '{cueId}' has no playable variants.");
            return default;
        }

        VariantRuntime selected = available[_random.Next(available.Count)];
        PruneFinished(selected);
        while (selected.ActiveVoices.Count >= cue.Definition.MaxInstancesPerVariant)
        {
            ulong oldest = selected.ActiveVoices.First!.Value;
            selected.ActiveVoices.RemoveFirst();
            _backend.Stop(oldest, 0);
            ReleaseVoice(oldest);
        }

        float pitch = SelectPitch(selected.Definition) * emitter.Pitch;
        BackendPlayback playback = CreatePlayback(
            cue.Definition.Bus,
            cue.Definition.SpatialMode,
            emitter,
            cue.Definition.Gain * selected.Definition.Gain,
            pitch,
            cue.Definition.MinDistance,
            cue.Definition.MaxDistance,
            cue.Definition.DopplerFactor,
            cue.Definition.Looping,
            0);
        ulong voice = _backend.Play(selected.Clip!, playback);
        if (voice == 0)
        {
            LogOnce($"Audio cue '{cueId}' could not start playback.");
            return default;
        }

        selected.ActiveVoices.AddLast(voice);
        _voices[voice] = new VoiceRuntime(
            selected,
            null,
            cue.Definition.Bus,
            false,
            cue.Definition.Gain * selected.Definition.Gain);
        return new AudioVoiceHandle(voice);
    }

    public AudioStreamHandle PlayStream(AudioStreamDefinition stream)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(stream);
        ValidateStream(stream);

        string path = Path.GetFullPath(stream.Path);
        BackendClip? clip = _backend.LoadClip(path, true, stream.SpatialMode);
        if (clip is null)
        {
            LogOnce($"Audio stream '{path}' could not be loaded; playback will be silent.");
            return default;
        }

        BackendPlayback playback = CreatePlayback(
            stream.Bus,
            stream.SpatialMode,
            stream.Emitter,
            1.0f,
            stream.Emitter.Pitch,
            stream.MinDistance,
            stream.MaxDistance,
            stream.DopplerFactor,
            stream.Looping,
            ToMilliseconds(stream.FadeInSeconds));
        ulong voice = _backend.Play(clip, playback);
        if (voice == 0)
        {
            clip.Dispose();
            LogOnce($"Audio stream '{path}' could not start playback.");
            return default;
        }

        _voices[voice] = new VoiceRuntime(null, clip, stream.Bus, true, 1.0f);
        return new AudioStreamHandle(voice);
    }

    public IAudioPcmStream CreatePcmStream(
        AudioFormat format,
        AudioStreamOptions options)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(options);
        if (format.Channels is < 1 or > 8)
        {
            throw new ArgumentOutOfRangeException(nameof(format), "Channel count must be between 1 and 8.");
        }
        if (format.SampleRate is < 8_000 or > 384_000)
        {
            throw new ArgumentOutOfRangeException(nameof(format), "Sample rate is outside the supported range.");
        }
        if (options.CapacityFrames <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options.CapacityFrames));
        }

        OwnedPcmStream stream = new(
            _backend.CreatePcmStream(format, options),
            RemovePcmStream);
        _pcmStreams.Add(stream);
        return stream;
    }

    public void SetBusGain(AudioBus bus, float gain)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidateGain(gain);
        _backend.SetBusGain(bus, gain);
    }

    public void Update(float deltaSeconds)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!float.IsFinite(deltaSeconds) || deltaSeconds < 0.0f)
        {
            throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
        }

        try
        {
            _backend.Update(deltaSeconds);
        }
        catch (AudioBackendException exception)
        {
            LogOnce($"Audio update failed and playback may be silent: {exception.Message}");
        }

        while (_backend.TryPollEvent(out BackendEvent audioEvent))
        {
            switch (audioEvent.Type)
            {
                case NativeEventType.VoiceFinished:
                case NativeEventType.VoiceStopped:
                    ReleaseVoice(audioEvent.Voice);
                    break;
                case NativeEventType.StreamUnderrun:
                    LogOnce("A real-time PCM stream underrun was detected.");
                    break;
                case NativeEventType.Diagnostic:
                    LogOnce($"Native audio diagnostic {audioEvent.Code}.");
                    break;
                case NativeEventType.Error:
                    ReleaseVoice(audioEvent.Voice);
                    LogOnce(
                        $"Native audio operation failed with {audioEvent.Result}; playback will continue where possible.");
                    break;
            }
        }

        AudioSystemStats stats = _backend.GetStats();
        if (stats.DroppedEvents != _observedDroppedEvents)
        {
            _observedDroppedEvents = stats.DroppedEvents;
            ReconcileInactiveVoices();
        }
    }

    public void StopAll(AudioBus? bus = null, float fadeSeconds = 0)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        uint fadeMilliseconds = ToMilliseconds(fadeSeconds);

        foreach (KeyValuePair<ulong, VoiceRuntime> pair in _voices)
        {
            if (bus is null || pair.Value.Bus == bus.Value)
            {
                pair.Value.Variant?.ActiveVoices.Remove(pair.Key);
            }
        }

        _backend.StopAll(bus, fadeMilliseconds);

        if (fadeMilliseconds == 0)
        {
            ulong[] affected = _voices
                .Where(pair => bus is null || pair.Value.Bus == bus.Value)
                .Select(static pair => pair.Key)
                .ToArray();
            foreach (ulong voice in affected)
            {
                ReleaseVoice(voice);
            }
        }
    }

    public void Stop(AudioVoiceHandle voice, float fadeSeconds = 0) =>
        StopVoice(voice.Value, fadeSeconds);

    public void Stop(AudioStreamHandle stream, float fadeSeconds = 0) =>
        StopVoice(stream.Value, fadeSeconds);

    public void SetGain(AudioVoiceHandle voice, float gain, float fadeSeconds = 0) =>
        SetVoiceGain(voice.Value, gain, fadeSeconds);

    public void SetGain(AudioStreamHandle stream, float gain, float fadeSeconds = 0) =>
        SetVoiceGain(stream.Value, gain, fadeSeconds);

    public void SetPaused(AudioStreamHandle stream, bool paused)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (stream.IsValid)
        {
            _backend.SetPaused(stream.Value, paused);
        }
    }

    public void Seek(AudioStreamHandle stream, float seconds)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!float.IsFinite(seconds) || seconds < 0.0f)
        {
            throw new ArgumentOutOfRangeException(nameof(seconds));
        }
        if (stream.IsValid)
        {
            _backend.Seek(stream.Value, seconds);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _backend.StopAll(null, 0);
        foreach (OwnedPcmStream stream in _pcmStreams.ToArray())
        {
            stream.Dispose();
        }
        _pcmStreams.Clear();
        foreach (VoiceRuntime voice in _voices.Values)
        {
            voice.OwnedClip?.Dispose();
        }
        _voices.Clear();

        foreach (CueRuntime cue in _cues.Values)
        {
            foreach (VariantRuntime variant in cue.Variants)
            {
                variant.Clip?.Dispose();
            }
        }
        _cues.Clear();
        _backend.Dispose();
        _disposed = true;
    }

    private void StopVoice(ulong voice, float fadeSeconds)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (voice == 0)
        {
            return;
        }

        uint fadeMilliseconds = ToMilliseconds(fadeSeconds);
        if (_voices.TryGetValue(voice, out VoiceRuntime? runtime))
        {
            runtime.Variant?.ActiveVoices.Remove(voice);
        }
        _backend.Stop(voice, fadeMilliseconds);
        if (fadeMilliseconds == 0)
        {
            ReleaseVoice(voice);
        }
    }

    private void SetVoiceGain(ulong voice, float gain, float fadeSeconds)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidateGain(gain);
        if (voice != 0 && _voices.TryGetValue(voice, out VoiceRuntime? runtime))
        {
            _backend.SetGain(
                voice,
                runtime.DefinitionGain * gain,
                ToMilliseconds(fadeSeconds));
        }
    }

    private void DisposeCue(CueRuntime cue)
    {
        foreach (VariantRuntime variant in cue.Variants)
        {
            foreach (ulong voice in variant.ActiveVoices.ToArray())
            {
                _backend.Stop(voice, 0);
                ReleaseVoice(voice);
            }
            variant.Clip?.Dispose();
        }
    }

    private void PruneFinished(VariantRuntime variant)
    {
        LinkedListNode<ulong>? node = variant.ActiveVoices.First;
        while (node is not null)
        {
            LinkedListNode<ulong>? next = node.Next;
            if (!_backend.IsVoiceActive(node.Value))
            {
                variant.ActiveVoices.Remove(node);
                ReleaseVoice(node.Value);
            }
            node = next;
        }
    }

    private void ReconcileInactiveVoices()
    {
        foreach (ulong voice in _voices.Keys.ToArray())
        {
            if (!_backend.IsVoiceActive(voice))
            {
                ReleaseVoice(voice);
            }
        }
    }

    private void ReleaseVoice(ulong voice)
    {
        if (!_voices.Remove(voice, out VoiceRuntime? runtime))
        {
            return;
        }

        runtime.Variant?.ActiveVoices.Remove(voice);
        runtime.OwnedClip?.Dispose();
    }

    private void RemovePcmStream(OwnedPcmStream stream) => _pcmStreams.Remove(stream);

    private float SelectPitch(AudioCueVariant variant)
    {
        if (variant.PitchMin == variant.PitchMax)
        {
            return variant.PitchMin;
        }
        return variant.PitchMin +
            ((variant.PitchMax - variant.PitchMin) * (float)_random.NextDouble());
    }

    private static BackendPlayback CreatePlayback(
        AudioBus bus,
        AudioSpatialMode spatialMode,
        in AudioEmitter emitter,
        float definitionGain,
        float pitch,
        float minDistance,
        float maxDistance,
        float dopplerFactor,
        bool looping,
        uint fadeInMilliseconds) => new(
            bus,
            spatialMode,
            emitter.Position,
            emitter.Velocity,
            definitionGain * emitter.Gain,
            pitch,
            minDistance,
            maxDistance,
            dopplerFactor,
            looping,
            fadeInMilliseconds);

    private void LogOnce(string message)
    {
        if (_loggedMessages.Add(message))
        {
            _logger(message);
        }
    }

    private static uint ToMilliseconds(float seconds)
    {
        if (!float.IsFinite(seconds) || seconds < 0.0f)
        {
            throw new ArgumentOutOfRangeException(nameof(seconds));
        }
        return checked((uint)Math.Round(seconds * 1_000.0f));
    }

    private static void ValidateOptions(AudioSystemOptions options)
    {
        if (options.MaxVoices <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options.MaxVoices));
        }
        if (options.SampleRate < 0 || options.Channels < 0 ||
            options.PeriodSizeFrames <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }
    }

    private static void ValidateCue(AudioCueDefinition cue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cue.CueId);
        if (cue.Variants is null || cue.Variants.Count == 0)
        {
            throw new ArgumentException("A cue must contain at least one variant.", nameof(cue));
        }
        if (!float.IsFinite(cue.Gain) || cue.Gain < 0.0f ||
            !float.IsFinite(cue.MinDistance) || cue.MinDistance <= 0.0f ||
            !float.IsFinite(cue.MaxDistance) || cue.MaxDistance < cue.MinDistance ||
            !float.IsFinite(cue.DopplerFactor) || cue.DopplerFactor < 0.0f ||
            cue.MaxInstancesPerVariant <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cue));
        }

        foreach (AudioCueVariant variant in cue.Variants)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(variant.Path);
            if (!float.IsFinite(variant.Gain) || variant.Gain < 0.0f ||
                !float.IsFinite(variant.PitchMin) || variant.PitchMin <= 0.0f ||
                !float.IsFinite(variant.PitchMax) || variant.PitchMax < variant.PitchMin)
            {
                throw new ArgumentOutOfRangeException(nameof(cue));
            }
        }
    }

    private static void ValidateStream(AudioStreamDefinition stream)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stream.Path);
        ValidateEmitter(stream.Emitter);
        if (!float.IsFinite(stream.MinDistance) || stream.MinDistance <= 0.0f ||
            !float.IsFinite(stream.MaxDistance) || stream.MaxDistance < stream.MinDistance ||
            !float.IsFinite(stream.DopplerFactor) || stream.DopplerFactor < 0.0f ||
            !float.IsFinite(stream.FadeInSeconds) || stream.FadeInSeconds < 0.0f)
        {
            throw new ArgumentOutOfRangeException(nameof(stream));
        }
    }

    private static void ValidateEmitter(in AudioEmitter emitter)
    {
        ValidateFinite(emitter.Position, nameof(emitter));
        ValidateFinite(emitter.Velocity, nameof(emitter));
        ValidateGain(emitter.Gain);
        if (!float.IsFinite(emitter.Pitch) || emitter.Pitch <= 0.0f)
        {
            throw new ArgumentOutOfRangeException(nameof(emitter));
        }
    }

    private static void ValidateGain(float gain)
    {
        if (!float.IsFinite(gain) || gain < 0.0f)
        {
            throw new ArgumentOutOfRangeException(nameof(gain));
        }
    }

    private static void ValidateFinite(System.Numerics.Vector3 value, string name)
    {
        if (!float.IsFinite(value.X) ||
            !float.IsFinite(value.Y) ||
            !float.IsFinite(value.Z))
        {
            throw new ArgumentOutOfRangeException(name);
        }
    }

    private static bool IsInitializationFailure(Exception exception) => exception is
        AudioBackendException or
        DllNotFoundException or
        EntryPointNotFoundException or
        BadImageFormatException or
        TypeInitializationException;

    private sealed class CueRuntime
    {
        public CueRuntime(
            AudioCueDefinition definition,
            IReadOnlyList<VariantRuntime> variants)
        {
            Definition = definition;
            Variants = variants;
        }

        public AudioCueDefinition Definition { get; }

        public IReadOnlyList<VariantRuntime> Variants { get; }
    }

    private sealed class VariantRuntime
    {
        public VariantRuntime(
            AudioCueVariant definition,
            string path,
            BackendClip? clip)
        {
            Definition = definition;
            Path = path;
            Clip = clip;
        }

        public AudioCueVariant Definition { get; }

        public string Path { get; }

        public BackendClip? Clip { get; }

        public LinkedList<ulong> ActiveVoices { get; } = [];
    }

    private sealed record VoiceRuntime(
        VariantRuntime? Variant,
        BackendClip? OwnedClip,
        AudioBus Bus,
        bool IsStream,
        float DefinitionGain);

    private sealed class OwnedPcmStream : IAudioPcmStream
    {
        private readonly BackendPcmStream _inner;
        private readonly Action<OwnedPcmStream> _onDispose;
        private bool _disposed;

        public OwnedPcmStream(
            BackendPcmStream inner,
            Action<OwnedPcmStream> onDispose)
        {
            _inner = inner;
            _onDispose = onDispose;
        }

        public AudioFormat Format => _inner.Format;

        public int AvailableWriteFrames => _disposed ? 0 : _inner.AvailableWriteFrames;

        public int Write(ReadOnlySpan<byte> interleavedFrames, int frameCount)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _inner.Write(interleavedFrames, frameCount);
        }

        public void Start()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _inner.Start();
        }

        public void SetPaused(bool paused)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _inner.SetPaused(paused);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            _inner.Dispose();
            _onDispose(this);
        }
    }
}

internal interface IAudioRandom
{
    int Next(int exclusiveMaximum);

    double NextDouble();
}

internal sealed class SystemAudioRandom : IAudioRandom
{
    private readonly Random _random;

    public SystemAudioRandom(int? seed)
    {
        _random = seed is int value ? new Random(value) : Random.Shared;
    }

    public int Next(int exclusiveMaximum) => _random.Next(exclusiveMaximum);

    public double NextDouble() => _random.NextDouble();
}
