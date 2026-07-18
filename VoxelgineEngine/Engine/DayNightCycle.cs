using System.Numerics;

namespace Voxelgine.Engine;

public readonly record struct DayNightLightingState(
	float SkyLightMultiplier,
	byte AmbientLight);

/// <summary>
/// Renderer-free authoritative day/night clock and presentation parameters.
/// Consumers decide how to apply the emitted light state to their world backend.
/// </summary>
public sealed class DayNightCycle
{
	private const float TimeLerpSpeed = 2f;
	private const float SunriseStart = 5f;
	private const float SunriseEnd = 7f;
	private const float SunsetStart = 17f;
	private const float SunsetEnd = 19f;
	private const float Noon = 12f;
	private const float DayLightLevel = 1f;
	private const float NightLightLevel = 0.15f;
	private const float MinAmbientNight = 1f;
	private const float MinAmbientDay = 2f;

	private static readonly Rgba32 SkyColorNight = new(10, 15, 30);
	private static readonly Rgba32 SkyColorDawn = new(255, 180, 120);
	private static readonly Rgba32 SkyColorDay = new(135, 206, 235);
	private static readonly Rgba32 SkyColorDusk = new(255, 140, 100);
	private static readonly Rgba32 SunColorNoon = new(255, 255, 240);
	private static readonly Rgba32 SunColorDawn = new(255, 180, 100);
	private static readonly Rgba32 SunColorDusk = new(255, 120, 80);

	private float targetTime = -1f;

	public event Action<DayNightLightingState> LightingChanged;

	public float TimeOfDay { get; private set; } = 8f;

	public float DayLengthSeconds { get; set; } = 600f;

	public bool IsPaused { get; set; }

	public float TimeScale { get; set; } = 1f;

	public bool IsAuthority { get; set; } = true;

	public Rgba32 SkyColor { get; private set; } = SkyColorDay;

	public float SkyLightMultiplier { get; private set; } = 1f;

	public byte AmbientLight { get; private set; } = 2;

	public Rgba32 SunColor { get; private set; } = Rgba32.White;

	public float SunElevation { get; private set; }

	public float SunAzimuth { get; private set; }

	public DayNightLightingState Lighting => new(SkyLightMultiplier, AmbientLight);

	public void Update(float deltaTime)
	{
		if (!float.IsFinite(deltaTime) || deltaTime < 0f)
			throw new ArgumentOutOfRangeException(nameof(deltaTime));

		if (IsPaused)
			return;

		if (IsAuthority)
		{
			if (DayLengthSeconds <= 0f)
				return;

			TimeOfDay += deltaTime * (24f / DayLengthSeconds) * TimeScale;
			WrapTime();
		}
		else if (targetTime >= 0f)
		{
			float difference = targetTime - TimeOfDay;
			if (difference > 12f)
				difference -= 24f;
			else if (difference < -12f)
				difference += 24f;

			if (MathF.Abs(difference) < 0.01f)
			{
				TimeOfDay = targetTime;
				targetTime = -1f;
			}
			else
			{
				TimeOfDay += difference * Math.Clamp(TimeLerpSpeed * deltaTime, 0f, 1f);
			}

			WrapTime();
		}

		UpdateLighting();
	}

	public void SetTime(float hours)
	{
		if (!float.IsFinite(hours))
			throw new ArgumentOutOfRangeException(nameof(hours));

		hours %= 24f;
		if (hours < 0f)
			hours += 24f;

		if (IsAuthority)
		{
			TimeOfDay = hours;
			UpdateLighting();
			return;
		}

		if (targetTime < 0f && MathF.Abs(TimeOfDay - hours) > 1f)
		{
			TimeOfDay = hours;
			UpdateLighting();
		}

		targetTime = hours;
	}

	public Vector3 GetSunDirection()
	{
		float cosineElevation = MathF.Cos(SunElevation);
		return new Vector3(
			cosineElevation * MathF.Cos(SunAzimuth),
			MathF.Sin(SunElevation),
			cosineElevation * MathF.Sin(SunAzimuth));
	}

	public string GetTimeString()
	{
		int hours = (int)TimeOfDay;
		int minutes = (int)((TimeOfDay - hours) * 60f);
		return $"{hours:D2}:{minutes:D2}";
	}

	public string GetPeriodString()
	{
		if (TimeOfDay < SunriseStart)
			return "Night";
		if (TimeOfDay < SunriseEnd)
			return "Dawn";
		if (TimeOfDay < Noon)
			return "Morning";
		if (TimeOfDay < SunsetStart)
			return "Afternoon";
		return TimeOfDay < SunsetEnd ? "Dusk" : "Night";
	}

	private void WrapTime()
	{
		while (TimeOfDay >= 24f)
			TimeOfDay -= 24f;
		while (TimeOfDay < 0f)
			TimeOfDay += 24f;
	}

	private void UpdateLighting()
	{
		SkyLightMultiplier = CalculateSkyLightMultiplier();
		float ambient = (SkyLightMultiplier - NightLightLevel) /
			(DayLightLevel - NightLightLevel);
		ambient = Math.Clamp(ambient, 0f, 1f);
		AmbientLight = (byte)Math.Round(Lerp(MinAmbientNight, MinAmbientDay, ambient));
		SkyColor = CalculateSkyColor();
		CalculateSunPosition();
		LightingChanged?.Invoke(Lighting);
	}

	private void CalculateSunPosition()
	{
		float dayProgress = (TimeOfDay - 6f) / 12f;
		SunAzimuth = dayProgress * MathF.PI;

		if (TimeOfDay >= SunriseStart && TimeOfDay <= SunsetEnd)
		{
			float sunProgress = (TimeOfDay - SunriseStart) / (SunsetEnd - SunriseStart);
			SunElevation = MathF.Sin(sunProgress * MathF.PI) * (MathF.PI / 2f) * 0.8f;
		}
		else
		{
			SunElevation = -0.3f;
		}

		if (SunElevation <= 0f)
		{
			SunColor = Rgba32.Transparent;
		}
		else if (SunElevation < 0.3f)
		{
			float blend = SunElevation / 0.3f;
			SunColor = TimeOfDay < Noon
				? LerpColor(SunColorDawn, SunColorNoon, blend)
				: LerpColor(SunColorDusk, SunColorNoon, 1f - blend);
		}
		else
		{
			SunColor = SunColorNoon;
		}
	}

	private float CalculateSkyLightMultiplier()
	{
		if (TimeOfDay < SunriseStart)
			return NightLightLevel;
		if (TimeOfDay < SunriseEnd)
			return Lerp(
				NightLightLevel,
				DayLightLevel,
				SmoothStep((TimeOfDay - SunriseStart) / (SunriseEnd - SunriseStart)));
		if (TimeOfDay < SunsetStart)
			return DayLightLevel;
		if (TimeOfDay < SunsetEnd)
			return Lerp(
				DayLightLevel,
				NightLightLevel,
				SmoothStep((TimeOfDay - SunsetStart) / (SunsetEnd - SunsetStart)));
		return NightLightLevel;
	}

	private Rgba32 CalculateSkyColor()
	{
		if (TimeOfDay < SunriseStart)
			return SkyColorNight;
		if (TimeOfDay < SunriseEnd)
		{
			float progress = (TimeOfDay - SunriseStart) / (SunriseEnd - SunriseStart);
			return progress < 0.5f
				? LerpColor(SkyColorNight, SkyColorDawn, SmoothStep(progress * 2f))
				: LerpColor(SkyColorDawn, SkyColorDay, SmoothStep((progress - 0.5f) * 2f));
		}
		if (TimeOfDay < SunsetStart)
			return SkyColorDay;
		if (TimeOfDay < SunsetEnd)
		{
			float progress = (TimeOfDay - SunsetStart) / (SunsetEnd - SunsetStart);
			return progress < 0.5f
				? LerpColor(SkyColorDay, SkyColorDusk, SmoothStep(progress * 2f))
				: LerpColor(SkyColorDusk, SkyColorNight, SmoothStep((progress - 0.5f) * 2f));
		}
		return SkyColorNight;
	}

	private static float Lerp(float start, float end, float amount) =>
		start + (end - start) * amount;

	private static float SmoothStep(float amount)
	{
		amount = Math.Clamp(amount, 0f, 1f);
		return amount * amount * (3f - 2f * amount);
	}

	private static Rgba32 LerpColor(Rgba32 start, Rgba32 end, float amount) => new(
		(byte)(start.R + (end.R - start.R) * amount),
		(byte)(start.G + (end.G - start.G) * amount),
		(byte)(start.B + (end.B - start.B) * amount),
		(byte)(start.A + (end.A - start.A) * amount));
}
