using System.Numerics;
using System.Runtime.InteropServices;

namespace Voxelgine.Audio;

internal readonly record struct BackendPlayback(
    AudioBus Bus,
    AudioSpatialMode SpatialMode,
    Vector3 Position,
    Vector3 Velocity,
    float Gain,
    float Pitch,
    float MinDistance,
    float MaxDistance,
    float DopplerFactor,
    bool Looping,
    uint FadeInMilliseconds);

internal readonly record struct BackendEvent(
    NativeEventType Type,
    ulong Voice,
    NativeResult Result,
    int Code);

internal abstract class BackendClip : IDisposable
{
    public abstract bool WasDownmixed { get; }

    public abstract uint SourceChannels { get; }

    public abstract void Dispose();
}

internal abstract class BackendPcmStream : IAudioPcmStream
{
    public abstract AudioFormat Format { get; }

    public abstract int AvailableWriteFrames { get; }

    public abstract int Write(ReadOnlySpan<byte> interleavedFrames, int frameCount);

    public abstract void Start();

    public abstract void SetPaused(bool paused);

    public abstract void Dispose();
}

internal interface IAudioBackend : IDisposable
{
    bool IsAvailable { get; }

    BackendClip? LoadClip(
        string path,
        bool streamed,
        AudioSpatialMode spatialMode);

    ulong Play(BackendClip clip, in BackendPlayback playback);

    bool IsVoiceActive(ulong voice);

    void Stop(ulong voice, uint fadeMilliseconds);

    void SetPaused(ulong voice, bool paused);

    void SetGain(ulong voice, float gain, uint fadeMilliseconds);

    void Seek(ulong voice, float seconds);

    void SetListener(in AudioListener listener);

    void SetBusGain(AudioBus bus, float gain);

    void StopAll(AudioBus? bus, uint fadeMilliseconds);

    void Update(float deltaSeconds);

    bool TryPollEvent(out BackendEvent audioEvent);

    BackendPcmStream CreatePcmStream(
        AudioFormat format,
        AudioStreamOptions options);

    AudioSystemStats GetStats();
}

internal sealed class NativeAudioBackend : IAudioBackend
{
    private const uint ExpectedAbiVersion = 1;
    private readonly SafeAudioEngineHandle _engine;
    private readonly Action<string> _diagnostic;
    private bool _disposed;

    public NativeAudioBackend(AudioSystemOptions options, Action<string> diagnostic)
    {
        _diagnostic = diagnostic;
        uint abiVersion = NativeMethods.GetAbiVersion();
        if (abiVersion != ExpectedAbiVersion)
        {
            throw new AudioBackendException(
                $"VoxelAudioNative ABI {abiVersion} is incompatible with expected ABI {ExpectedAbiVersion}.");
        }

        NativeEngineConfig config = new()
        {
            StructSize = (uint)Marshal.SizeOf<NativeEngineConfig>(),
            MaxVoices = checked((uint)options.MaxVoices),
            SampleRate = checked((uint)options.SampleRate),
            Channels = checked((uint)options.Channels),
            PeriodSizeFrames = checked((uint)options.PeriodSizeFrames),
            NoDevice = options.NoDevice ? 1u : 0u
        };

        NativeResult result = NativeMethods.EngineCreate(config, out nint engine);
        ThrowIfFailed(result, "initialize the audio engine");
        _engine = new SafeAudioEngineHandle(engine);
    }

    public bool IsAvailable => !_disposed;

    public BackendClip? LoadClip(
        string path,
        bool streamed,
        AudioSpatialMode spatialMode)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        NativeClipConfig config = new()
        {
            StructSize = (uint)Marshal.SizeOf<NativeClipConfig>(),
            Streamed = streamed ? 1u : 0u,
            SpatialMode = ToNative(spatialMode)
        };

        NativeResult result = NativeMethods.ClipCreate(
            _engine,
            path,
            config,
            out nint clipPointer);
        if (result != NativeResult.Success)
        {
            return null;
        }

        SafeAudioClipHandle handle = new(clipPointer, _engine);
        uint sourceChannels = NativeMethods.ClipSourceChannels(handle);
        bool downmixed = NativeMethods.ClipWasDownmixed(handle) != 0;
        if (downmixed)
        {
            _diagnostic(
                $"Spatial clip '{path}' has {sourceChannels} channels and was downmixed to mono.");
        }

        return new NativeBackendClip(handle, sourceChannels, downmixed);
    }

    public ulong Play(BackendClip clip, in BackendPlayback playback)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        NativeBackendClip nativeClip = clip as NativeBackendClip
            ?? throw new ArgumentException("Clip belongs to another audio backend.", nameof(clip));
        Vector3 position = AudioCoordinates.ToNative(playback.Position);
        Vector3 velocity = AudioCoordinates.ToNative(playback.Velocity);
        NativePlayParams parameters = new()
        {
            StructSize = (uint)Marshal.SizeOf<NativePlayParams>(),
            Bus = playback.Bus,
            SpatialMode = ToNative(playback.SpatialMode),
            SourceProcessor = 0,
            Gain = playback.Gain,
            Pitch = playback.Pitch,
            PositionX = position.X,
            PositionY = position.Y,
            PositionZ = position.Z,
            VelocityX = velocity.X,
            VelocityY = velocity.Y,
            VelocityZ = velocity.Z,
            MinDistance = playback.MinDistance,
            MaxDistance = playback.MaxDistance,
            DopplerFactor = playback.DopplerFactor,
            Looping = playback.Looping ? 1u : 0u,
            FadeInMilliseconds = playback.FadeInMilliseconds
        };

        NativeResult result = NativeMethods.VoicePlay(
            _engine,
            nativeClip.Handle,
            parameters,
            out ulong voice);
        return result == NativeResult.Success ? voice : 0;
    }

    public bool IsVoiceActive(ulong voice) =>
        !_disposed && voice != 0 && NativeMethods.VoiceIsActive(_engine, voice) != 0;

    public void Stop(ulong voice, uint fadeMilliseconds)
    {
        if (!_disposed && voice != 0)
        {
            NativeMethods.VoiceStop(_engine, voice, fadeMilliseconds);
        }
    }

    public void SetPaused(ulong voice, bool paused)
    {
        if (!_disposed && voice != 0)
        {
            NativeMethods.VoiceSetPaused(_engine, voice, paused ? 1u : 0u);
        }
    }

    public void SetGain(ulong voice, float gain, uint fadeMilliseconds)
    {
        if (!_disposed && voice != 0)
        {
            NativeMethods.VoiceSetGain(_engine, voice, gain, fadeMilliseconds);
        }
    }

    public void Seek(ulong voice, float seconds)
    {
        if (!_disposed && voice != 0)
        {
            NativeMethods.VoiceSeekSeconds(_engine, voice, seconds);
        }
    }

    public void SetListener(in AudioListener listener)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Vector3 position = AudioCoordinates.ToNative(listener.Position);
        Vector3 forward = AudioCoordinates.ToNative(listener.Forward);
        Vector3 up = AudioCoordinates.ToNative(listener.Up);
        Vector3 velocity = AudioCoordinates.ToNative(listener.Velocity);
        NativeListener native = new()
        {
            StructSize = (uint)Marshal.SizeOf<NativeListener>(),
            PositionX = position.X,
            PositionY = position.Y,
            PositionZ = position.Z,
            ForwardX = forward.X,
            ForwardY = forward.Y,
            ForwardZ = forward.Z,
            UpX = up.X,
            UpY = up.Y,
            UpZ = up.Z,
            VelocityX = velocity.X,
            VelocityY = velocity.Y,
            VelocityZ = velocity.Z
        };
        ThrowIfFailed(
            NativeMethods.EngineSetListener(_engine, native),
            "set the listener");
    }

    public void SetBusGain(AudioBus bus, float gain)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ThrowIfFailed(
            NativeMethods.EngineSetBusGain(_engine, bus, gain),
            "set a bus gain");
    }

    public void StopAll(AudioBus? bus, uint fadeMilliseconds)
    {
        if (!_disposed)
        {
            NativeMethods.EngineStopAll(
                _engine,
                bus is null ? -1 : (int)bus.Value,
                fadeMilliseconds);
        }
    }

    public void Update(float deltaSeconds)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ThrowIfFailed(
            NativeMethods.EngineUpdate(_engine, deltaSeconds),
            "update the audio engine");
    }

    public bool TryPollEvent(out BackendEvent audioEvent)
    {
        if (_disposed || NativeMethods.EnginePollEvent(_engine, out NativeEvent native) == 0)
        {
            audioEvent = default;
            return false;
        }

        audioEvent = new BackendEvent(
            native.Type,
            native.Voice,
            native.Result,
            (int)native.Code);
        return true;
    }

    public BackendPcmStream CreatePcmStream(
        AudioFormat format,
        AudioStreamOptions options)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        NativePcmStreamConfig config = new()
        {
            StructSize = (uint)Marshal.SizeOf<NativePcmStreamConfig>(),
            Format = format.SampleFormat switch
            {
                AudioPcmSampleFormat.Float32 => NativeSampleFormat.Float32,
                AudioPcmSampleFormat.Signed16 => NativeSampleFormat.Signed16,
                _ => throw new ArgumentOutOfRangeException(nameof(format))
            },
            Channels = checked((uint)format.Channels),
            SampleRate = checked((uint)format.SampleRate),
            CapacityFrames = checked((uint)options.CapacityFrames),
            Bus = options.Bus
        };

        NativeResult result = NativeMethods.PcmStreamCreate(
            _engine,
            config,
            out nint stream);
        ThrowIfFailed(result, "create a PCM stream");
        NativeBackendPcmStream managed = new(
            new SafePcmStreamHandle(stream, _engine),
            format);
        if (options.StartImmediately)
        {
            managed.Start();
        }
        return managed;
    }

    public AudioSystemStats GetStats()
    {
        if (_disposed)
        {
            return default;
        }

        NativeStats stats = new()
        {
            StructSize = (uint)Marshal.SizeOf<NativeStats>()
        };
        NativeResult result = NativeMethods.EngineGetStats(_engine, ref stats);
        return result == NativeResult.Success
            ? new AudioSystemStats(
                stats.ActiveVoices,
                stats.ActivePcmStreams,
                stats.CompletedVoices,
                stats.StoppedVoices,
                stats.StreamUnderruns,
                stats.DroppedEvents)
            : default;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _engine.Dispose();
    }

    private static NativeSpatialMode ToNative(AudioSpatialMode mode) => mode switch
    {
        AudioSpatialMode.TwoDimensional => NativeSpatialMode.TwoDimensional,
        AudioSpatialMode.ThreeDimensional => NativeSpatialMode.ThreeDimensional,
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };

    private static void ThrowIfFailed(NativeResult result, string operation)
    {
        if (result != NativeResult.Success)
        {
            throw new AudioBackendException(
                $"Failed to {operation}: native result {result} ({(int)result}).");
        }
    }

    private sealed class NativeBackendClip : BackendClip
    {
        public NativeBackendClip(
            SafeAudioClipHandle handle,
            uint sourceChannels,
            bool wasDownmixed)
        {
            Handle = handle;
            SourceChannels = sourceChannels;
            WasDownmixed = wasDownmixed;
        }

        public SafeAudioClipHandle Handle { get; }

        public override bool WasDownmixed { get; }

        public override uint SourceChannels { get; }

        public override void Dispose() => Handle.Dispose();
    }

    private sealed class NativeBackendPcmStream : BackendPcmStream
    {
        private readonly SafePcmStreamHandle _handle;
        private bool _disposed;

        public NativeBackendPcmStream(
            SafePcmStreamHandle handle,
            AudioFormat format)
        {
            _handle = handle;
            Format = format;
        }

        public override AudioFormat Format { get; }

        public override int AvailableWriteFrames => _disposed
            ? 0
            : checked((int)NativeMethods.PcmStreamAvailableWrite(_handle));

        public override unsafe int Write(
            ReadOnlySpan<byte> interleavedFrames,
            int frameCount)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (frameCount < 0 ||
                interleavedFrames.Length < checked(frameCount * Format.BytesPerFrame))
            {
                throw new ArgumentOutOfRangeException(nameof(frameCount));
            }

            fixed (byte* frames = interleavedFrames)
            {
                NativeResult result = NativeMethods.PcmStreamWrite(
                    _handle,
                    frames,
                    checked((uint)frameCount),
                    out uint written);
                ThrowIfFailed(result, "write PCM frames");
                return checked((int)written);
            }
        }

        public override void Start() => SetPaused(false);

        public override void SetPaused(bool paused)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ThrowIfFailed(
                NativeMethods.PcmStreamSetPaused(_handle, paused ? 1u : 0u),
                paused ? "pause a PCM stream" : "start a PCM stream");
        }

        public override void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            _handle.Dispose();
        }
    }
}

internal sealed class NullAudioBackend : IAudioBackend
{
    public bool IsAvailable => false;

    public BackendClip? LoadClip(
        string path,
        bool streamed,
        AudioSpatialMode spatialMode) => null;

    public ulong Play(BackendClip clip, in BackendPlayback playback) => 0;

    public bool IsVoiceActive(ulong voice) => false;

    public void Stop(ulong voice, uint fadeMilliseconds) { }

    public void SetPaused(ulong voice, bool paused) { }

    public void SetGain(ulong voice, float gain, uint fadeMilliseconds) { }

    public void Seek(ulong voice, float seconds) { }

    public void SetListener(in AudioListener listener) { }

    public void SetBusGain(AudioBus bus, float gain) { }

    public void StopAll(AudioBus? bus, uint fadeMilliseconds) { }

    public void Update(float deltaSeconds) { }

    public bool TryPollEvent(out BackendEvent audioEvent)
    {
        audioEvent = default;
        return false;
    }

    public BackendPcmStream CreatePcmStream(
        AudioFormat format,
        AudioStreamOptions options) => new SilentPcmStream(format, options.CapacityFrames);

    public AudioSystemStats GetStats() => default;

    public void Dispose() { }

    private sealed class SilentPcmStream : BackendPcmStream
    {
        private readonly int _capacityFrames;

        public SilentPcmStream(AudioFormat format, int capacityFrames)
        {
            Format = format;
            _capacityFrames = capacityFrames;
        }

        public override AudioFormat Format { get; }

        public override int AvailableWriteFrames => _capacityFrames;

        public override int Write(ReadOnlySpan<byte> interleavedFrames, int frameCount)
        {
            if (frameCount < 0 ||
                interleavedFrames.Length < checked(frameCount * Format.BytesPerFrame))
            {
                throw new ArgumentOutOfRangeException(nameof(frameCount));
            }
            return frameCount;
        }

        public override void Start() { }

        public override void SetPaused(bool paused) { }

        public override void Dispose() { }
    }
}

internal sealed class AudioBackendException : Exception
{
    public AudioBackendException(string message)
        : base(message)
    {
    }
}
