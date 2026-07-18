using System.Numerics;

namespace Voxelgine.Audio;

public readonly record struct AmbienceSample(
    float OutdoorExposure,
    float DirectSkylight,
    float Daylight,
    bool IsUnderwater,
    Vector3 ListenerPosition,
    IReadOnlyList<Vector3> CampfirePositions);

public sealed record AmbienceControllerOptions
{
    public string WindCueId { get; init; } = "ambience.wind";

    public string BirdsCueId { get; init; } = "ambience.birds";

    public string UnderwaterCueId { get; init; } = "ambience.underwater";

    public string CampfireCueId { get; init; } = "ambience.campfire";

    public float UpdateRateHz { get; init; } = 5.0f;

    public float CrossfadeSeconds { get; init; } = 1.0f;

    public float ExposureSmoothingSeconds { get; init; } = 0.75f;

    public float OutdoorOnThreshold { get; init; } = 0.55f;

    public float OutdoorOffThreshold { get; init; } = 0.35f;

    public float BirdsDaylightThreshold { get; init; } = 0.2f;

    public int MaximumCampfires { get; init; } = 4;

    public float CampfireRange { get; init; } = 48.0f;
}

public sealed class AmbienceController : IDisposable
{
    private readonly IAudioSystem _audio;
    private readonly AmbienceControllerOptions _options;
    private readonly Dictionary<Vector3, AudioVoiceHandle> _campfires = [];
    private AudioVoiceHandle _wind;
    private AudioVoiceHandle _birds;
    private AudioVoiceHandle _underwater;
    private float _tickAccumulator;
    private float _smoothedExposure;
    private bool _outdoorGate;
    private bool _disposed;

    public AmbienceController(
        IAudioSystem audio,
        AmbienceControllerOptions? options = null)
    {
        _audio = audio ?? throw new ArgumentNullException(nameof(audio));
        _options = options ?? new AmbienceControllerOptions();
        ValidateOptions(_options);
    }

    public void Update(float deltaSeconds, in AmbienceSample sample)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!float.IsFinite(deltaSeconds) || deltaSeconds < 0.0f)
        {
            throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
        }

        float exposure = Math.Clamp(
            Math.Min(sample.OutdoorExposure, sample.DirectSkylight),
            0.0f,
            1.0f);
        float smoothing = _options.ExposureSmoothingSeconds <= 0.0f
            ? 1.0f
            : 1.0f - MathF.Exp(-deltaSeconds / _options.ExposureSmoothingSeconds);
        _smoothedExposure += (exposure - _smoothedExposure) * smoothing;
        _tickAccumulator += deltaSeconds;

        float tickInterval = 1.0f / _options.UpdateRateHz;
        if (_tickAccumulator < tickInterval)
        {
            return;
        }
        _tickAccumulator %= tickInterval;

        _outdoorGate = _outdoorGate
            ? _smoothedExposure > _options.OutdoorOffThreshold
            : _smoothedExposure >= _options.OutdoorOnThreshold;

        float windGain = _outdoorGate ? _smoothedExposure : 0.0f;
        float daylight = Math.Clamp(sample.Daylight, 0.0f, 1.0f);
        float birdsGain = _outdoorGate && daylight >= _options.BirdsDaylightThreshold
            ? _smoothedExposure * daylight
            : 0.0f;

        UpdateLayer(ref _wind, _options.WindCueId, windGain);
        UpdateLayer(ref _birds, _options.BirdsCueId, birdsGain);
        UpdateLayer(
            ref _underwater,
            _options.UnderwaterCueId,
            sample.IsUnderwater ? 1.0f : 0.0f);
        UpdateCampfires(sample.ListenerPosition, sample.CampfirePositions);
    }

    public void ResetWorld()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        StopLayer(ref _wind);
        StopLayer(ref _birds);
        StopLayer(ref _underwater);
        foreach (AudioVoiceHandle campfire in _campfires.Values)
        {
            _audio.Stop(campfire, _options.CrossfadeSeconds);
        }
        _campfires.Clear();
        _tickAccumulator = 0.0f;
        _smoothedExposure = 0.0f;
        _outdoorGate = false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        ResetWorld();
        _disposed = true;
    }

    private void UpdateLayer(
        ref AudioVoiceHandle handle,
        string cueId,
        float targetGain)
    {
        if (targetGain <= 0.001f)
        {
            StopLayer(ref handle);
            return;
        }

        if (!handle.IsValid)
        {
            AudioEmitter emitter = AudioEmitter.NonSpatial(0.0f);
            handle = _audio.PlayCue(cueId, emitter);
        }

        if (handle.IsValid)
        {
            _audio.SetGain(handle, targetGain, _options.CrossfadeSeconds);
        }
    }

    private void StopLayer(ref AudioVoiceHandle handle)
    {
        if (!handle.IsValid)
        {
            return;
        }
        _audio.Stop(handle, _options.CrossfadeSeconds);
        handle = default;
    }

    private void UpdateCampfires(
        Vector3 listenerPosition,
        IReadOnlyList<Vector3>? positions)
    {
        positions ??= Array.Empty<Vector3>();
        float rangeSquared = _options.CampfireRange * _options.CampfireRange;
        Vector3[] desiredPositions = positions
            .Where(position => Vector3.DistanceSquared(position, listenerPosition) <= rangeSquared)
            .Distinct()
            .OrderBy(position => Vector3.DistanceSquared(position, listenerPosition))
            .Take(_options.MaximumCampfires)
            .ToArray();
        HashSet<Vector3> desired = desiredPositions.ToHashSet();

        foreach (Vector3 removed in _campfires.Keys.Where(position => !desired.Contains(position)).ToArray())
        {
            _audio.Stop(_campfires[removed], _options.CrossfadeSeconds);
            _campfires.Remove(removed);
        }

        foreach (Vector3 position in desiredPositions)
        {
            if (_campfires.ContainsKey(position))
            {
                continue;
            }

            AudioEmitter emitter = new(position, Vector3.Zero, 0.0f);
            AudioVoiceHandle handle = _audio.PlayCue(_options.CampfireCueId, emitter);
            if (handle.IsValid)
            {
                _audio.SetGain(handle, 1.0f, _options.CrossfadeSeconds);
                _campfires.Add(position, handle);
            }
        }
    }

    private static void ValidateOptions(AmbienceControllerOptions options)
    {
        if (!float.IsFinite(options.UpdateRateHz) || options.UpdateRateHz <= 0.0f ||
            !float.IsFinite(options.CrossfadeSeconds) || options.CrossfadeSeconds < 0.0f ||
            !float.IsFinite(options.ExposureSmoothingSeconds) || options.ExposureSmoothingSeconds < 0.0f ||
            !float.IsFinite(options.OutdoorOnThreshold) ||
            !float.IsFinite(options.OutdoorOffThreshold) ||
            options.OutdoorOnThreshold < options.OutdoorOffThreshold ||
            !float.IsFinite(options.BirdsDaylightThreshold) ||
            options.MaximumCampfires <= 0 ||
            !float.IsFinite(options.CampfireRange) || options.CampfireRange <= 0.0f)
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }
    }
}
