using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;

namespace Voxelgine.Audio;

internal enum NativeResult
{
    Success = 0,
    InvalidArgument = -1,
    OutOfMemory = -2,
    Device = -3,
    File = -4,
    Format = -5,
    Capacity = -6,
    StaleHandle = -7,
    Unsupported = -8,
    Internal = -9
}

internal enum NativeSpatialMode
{
    TwoDimensional,
    ThreeDimensional
}

internal enum NativeSampleFormat
{
    Float32 = 1,
    Signed16 = 2
}

internal enum NativeEventType
{
    None,
    VoiceFinished,
    VoiceStopped,
    StreamUnderrun,
    Diagnostic,
    Error
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeEngineConfig
{
    public uint StructSize;
    public uint MaxVoices;
    public uint SampleRate;
    public uint Channels;
    public uint PeriodSizeFrames;
    public uint NoDevice;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeClipConfig
{
    public uint StructSize;
    public uint Streamed;
    public NativeSpatialMode SpatialMode;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativePlayParams
{
    public uint StructSize;
    public AudioBus Bus;
    public NativeSpatialMode SpatialMode;
    public int SourceProcessor;
    public float Gain;
    public float Pitch;
    public float PositionX;
    public float PositionY;
    public float PositionZ;
    public float VelocityX;
    public float VelocityY;
    public float VelocityZ;
    public float MinDistance;
    public float MaxDistance;
    public float DopplerFactor;
    public uint Looping;
    public uint FadeInMilliseconds;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeListener
{
    public uint StructSize;
    public float PositionX;
    public float PositionY;
    public float PositionZ;
    public float ForwardX;
    public float ForwardY;
    public float ForwardZ;
    public float UpX;
    public float UpY;
    public float UpZ;
    public float VelocityX;
    public float VelocityY;
    public float VelocityZ;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativePcmStreamConfig
{
    public uint StructSize;
    public NativeSampleFormat Format;
    public uint Channels;
    public uint SampleRate;
    public uint CapacityFrames;
    public AudioBus Bus;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeEvent
{
    public uint StructSize;
    public NativeEventType Type;
    public NativeResult Result;
    public ulong Voice;
    public uint Code;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeStats
{
    public uint StructSize;
    public uint ActiveVoices;
    public uint ActivePcmStreams;
    public ulong CompletedVoices;
    public ulong StoppedVoices;
    public ulong StreamUnderruns;
    public ulong DroppedEvents;
}

internal sealed class SafeAudioEngineHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    internal SafeAudioEngineHandle(nint handle)
        : base(true)
    {
        SetHandle(handle);
    }

    protected override bool ReleaseHandle()
    {
        NativeMethods.EngineDestroy(handle);
        return true;
    }
}

internal sealed class SafeAudioClipHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    private SafeAudioEngineHandle? _engine;

    internal SafeAudioClipHandle(
        nint handle,
        SafeAudioEngineHandle engine)
        : base(true)
    {
        bool engineReferenceAdded = false;
        engine.DangerousAddRef(ref engineReferenceAdded);
        if (!engineReferenceAdded)
        {
            throw new ObjectDisposedException(nameof(engine));
        }

        _engine = engine;
        SetHandle(handle);
    }

    protected override bool ReleaseHandle()
    {
        try
        {
            NativeMethods.ClipDestroy(handle);
        }
        finally
        {
            _engine!.DangerousRelease();
            _engine = null;
        }
        return true;
    }
}

internal sealed class SafePcmStreamHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    private SafeAudioEngineHandle? _engine;

    internal SafePcmStreamHandle(
        nint handle,
        SafeAudioEngineHandle engine)
        : base(true)
    {
        bool engineReferenceAdded = false;
        engine.DangerousAddRef(ref engineReferenceAdded);
        if (!engineReferenceAdded)
        {
            throw new ObjectDisposedException(nameof(engine));
        }

        _engine = engine;
        SetHandle(handle);
    }

    protected override bool ReleaseHandle()
    {
        try
        {
            NativeMethods.PcmStreamDestroy(handle);
        }
        finally
        {
            _engine!.DangerousRelease();
            _engine = null;
        }
        return true;
    }
}

internal static partial class NativeMethods
{
    internal const string LibraryName = "VoxelAudioNative";

    [LibraryImport(LibraryName, EntryPoint = "va_get_abi_version")]
    internal static partial uint GetAbiVersion();

    [LibraryImport(LibraryName, EntryPoint = "va_engine_create")]
    internal static partial NativeResult EngineCreate(
        in NativeEngineConfig config,
        out nint engine);

    [LibraryImport(LibraryName, EntryPoint = "va_engine_destroy")]
    internal static partial void EngineDestroy(nint engine);

    [LibraryImport(LibraryName, EntryPoint = "va_engine_update")]
    internal static partial NativeResult EngineUpdate(
        SafeAudioEngineHandle engine,
        float deltaSeconds);

    [LibraryImport(LibraryName, EntryPoint = "va_engine_set_listener")]
    internal static partial NativeResult EngineSetListener(
        SafeAudioEngineHandle engine,
        in NativeListener listener);

    [LibraryImport(LibraryName, EntryPoint = "va_engine_set_bus_gain")]
    internal static partial NativeResult EngineSetBusGain(
        SafeAudioEngineHandle engine,
        AudioBus bus,
        float gain);

    [LibraryImport(LibraryName, EntryPoint = "va_engine_stop_all")]
    internal static partial NativeResult EngineStopAll(
        SafeAudioEngineHandle engine,
        int busOrAll,
        uint fadeMilliseconds);

    [LibraryImport(LibraryName, EntryPoint = "va_engine_poll_event")]
    internal static partial int EnginePollEvent(
        SafeAudioEngineHandle engine,
        out NativeEvent audioEvent);

    [LibraryImport(LibraryName, EntryPoint = "va_engine_get_stats")]
    internal static partial NativeResult EngineGetStats(
        SafeAudioEngineHandle engine,
        ref NativeStats stats);

    [LibraryImport(
        LibraryName,
        EntryPoint = "va_clip_create",
        StringMarshalling = StringMarshalling.Utf8)]
    internal static partial NativeResult ClipCreate(
        SafeAudioEngineHandle engine,
        string path,
        in NativeClipConfig config,
        out nint clip);

    [LibraryImport(LibraryName, EntryPoint = "va_clip_destroy")]
    internal static partial void ClipDestroy(nint clip);

    [LibraryImport(LibraryName, EntryPoint = "va_clip_source_channels")]
    internal static partial uint ClipSourceChannels(SafeAudioClipHandle clip);

    [LibraryImport(LibraryName, EntryPoint = "va_clip_was_downmixed")]
    internal static partial uint ClipWasDownmixed(SafeAudioClipHandle clip);

    [LibraryImport(LibraryName, EntryPoint = "va_voice_play")]
    internal static partial NativeResult VoicePlay(
        SafeAudioEngineHandle engine,
        SafeAudioClipHandle clip,
        in NativePlayParams parameters,
        out ulong voice);

    [LibraryImport(LibraryName, EntryPoint = "va_voice_stop")]
    internal static partial NativeResult VoiceStop(
        SafeAudioEngineHandle engine,
        ulong voice,
        uint fadeMilliseconds);

    [LibraryImport(LibraryName, EntryPoint = "va_voice_set_paused")]
    internal static partial NativeResult VoiceSetPaused(
        SafeAudioEngineHandle engine,
        ulong voice,
        uint paused);

    [LibraryImport(LibraryName, EntryPoint = "va_voice_set_gain")]
    internal static partial NativeResult VoiceSetGain(
        SafeAudioEngineHandle engine,
        ulong voice,
        float gain,
        uint fadeMilliseconds);

    [LibraryImport(LibraryName, EntryPoint = "va_voice_seek_seconds")]
    internal static partial NativeResult VoiceSeekSeconds(
        SafeAudioEngineHandle engine,
        ulong voice,
        float seconds);

    [LibraryImport(LibraryName, EntryPoint = "va_voice_is_active")]
    internal static partial int VoiceIsActive(
        SafeAudioEngineHandle engine,
        ulong voice);

    [LibraryImport(LibraryName, EntryPoint = "va_pcm_stream_create")]
    internal static partial NativeResult PcmStreamCreate(
        SafeAudioEngineHandle engine,
        in NativePcmStreamConfig config,
        out nint stream);

    [LibraryImport(LibraryName, EntryPoint = "va_pcm_stream_destroy")]
    internal static partial void PcmStreamDestroy(nint stream);

    [LibraryImport(LibraryName, EntryPoint = "va_pcm_stream_write")]
    internal static unsafe partial NativeResult PcmStreamWrite(
        SafePcmStreamHandle stream,
        void* interleavedFrames,
        uint frameCount,
        out uint framesWritten);

    [LibraryImport(LibraryName, EntryPoint = "va_pcm_stream_available_write")]
    internal static partial uint PcmStreamAvailableWrite(SafePcmStreamHandle stream);

    [LibraryImport(LibraryName, EntryPoint = "va_pcm_stream_set_paused")]
    internal static partial NativeResult PcmStreamSetPaused(
        SafePcmStreamHandle stream,
        uint paused);
}
