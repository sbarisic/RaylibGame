using System.Numerics;

namespace Voxelgine.Audio;

public enum AudioBus
{
    Master,
    Sfx,
    Ambience,
    Music,
    Voice,
    Ui
}

public enum AudioSpatialMode
{
    TwoDimensional,
    ThreeDimensional
}

public enum AudioPcmSampleFormat
{
    Float32,
    Signed16
}

public readonly record struct AudioVoiceHandle(ulong Value)
{
    public bool IsValid => Value != 0;
}

public readonly record struct AudioStreamHandle(ulong Value)
{
    public bool IsValid => Value != 0;
}

public readonly record struct AudioListener(
    Vector3 Position,
    Vector3 Forward,
    Vector3 Up,
    Vector3 Velocity)
{
    public static AudioListener Default { get; } = new(
        Vector3.Zero,
        Vector3.UnitZ,
        Vector3.UnitY,
        Vector3.Zero);
}

public readonly record struct AudioEmitter(
    Vector3 Position,
    Vector3 Velocity,
    float Gain = 1.0f,
    float Pitch = 1.0f)
{
    public static AudioEmitter NonSpatial(float gain = 1.0f) => new(
        Vector3.Zero,
        Vector3.Zero,
        gain,
        1.0f);
}

public sealed record AudioCueVariant
{
    public required string Path { get; init; }

    public float Gain { get; init; } = 1.0f;

    public float PitchMin { get; init; } = 1.0f;

    public float PitchMax { get; init; } = 1.0f;
}

public sealed record AudioCueDefinition
{
    public required string CueId { get; init; }

    public required IReadOnlyList<AudioCueVariant> Variants { get; init; }

    public AudioBus Bus { get; init; } = AudioBus.Sfx;

    public float Gain { get; init; } = 1.0f;

    public AudioSpatialMode SpatialMode { get; init; } = AudioSpatialMode.ThreeDimensional;

    public float MinDistance { get; init; } = 1.0f;

    public float MaxDistance { get; init; } = 32.0f;

    public float DopplerFactor { get; init; } = 1.0f;

    public bool Looping { get; init; }

    public bool Streamed { get; init; }

    public int MaxInstancesPerVariant { get; init; } = 6;
}

public sealed record AudioStreamDefinition
{
    public required string Path { get; init; }

    public AudioBus Bus { get; init; } = AudioBus.Music;

    public AudioSpatialMode SpatialMode { get; init; } = AudioSpatialMode.TwoDimensional;

    public AudioEmitter Emitter { get; init; } = AudioEmitter.NonSpatial();

    public float MinDistance { get; init; } = 1.0f;

    public float MaxDistance { get; init; } = 32.0f;

    public float DopplerFactor { get; init; } = 1.0f;

    public bool Looping { get; init; } = true;

    public float FadeInSeconds { get; init; }
}

public readonly record struct AudioFormat(
    AudioPcmSampleFormat SampleFormat,
    int Channels,
    int SampleRate)
{
    public int BytesPerSample => SampleFormat switch
    {
        AudioPcmSampleFormat.Float32 => sizeof(float),
        AudioPcmSampleFormat.Signed16 => sizeof(short),
        _ => throw new ArgumentOutOfRangeException(nameof(SampleFormat))
    };

    public int BytesPerFrame => checked(BytesPerSample * Channels);
}

public sealed record AudioStreamOptions
{
    public AudioBus Bus { get; init; } = AudioBus.Voice;

    public int CapacityFrames { get; init; } = 48_000;

    public bool StartImmediately { get; init; }
}

public sealed record AudioSystemOptions
{
    public int MaxVoices { get; init; } = 256;

    public int SampleRate { get; init; }

    public int Channels { get; init; }

    public int PeriodSizeFrames { get; init; } = 256;

    public bool NoDevice { get; init; }

    public bool ThrowOnInitializationFailure { get; init; }

    public int? RandomSeed { get; init; }

    public Action<string>? Log { get; init; }
}

public readonly record struct AudioSystemStats(
    uint ActiveVoices,
    uint ActivePcmStreams,
    ulong CompletedVoices,
    ulong StoppedVoices,
    ulong StreamUnderruns,
    ulong DroppedEvents);
