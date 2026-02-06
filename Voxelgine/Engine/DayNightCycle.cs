using Raylib_cs;
using System;
using System.Numerics;
using Voxelgine.Graphics;

namespace Voxelgine.Engine
{
	/// <summary>
	/// Manages the day/night cycle, controlling skylight levels and sky colors over time.
	/// Time is measured in game hours (0-24), with configurable day length.
	/// </summary>
	public class DayNightCycle
	{
		/// <summary>
		/// Current time of day in hours (0-24). 
		/// 0 = midnight, 6 = sunrise, 12 = noon, 18 = sunset.
		/// </summary>
		public float TimeOfDay { get; private set; } = 8f; // Start at 8 AM

		/// <summary>
		/// Length of a full day/night cycle in real seconds.
		/// Default is 600 seconds (10 minutes).
		/// </summary>
		public float DayLengthSeconds { get; set; } = 600f;

		/// <summary>
		/// When true, time progression is paused.
		/// </summary>
		public bool IsPaused { get; set; } = false;

		/// <summary>
		/// Speed multiplier for time progression. 1.0 = normal, 2.0 = double speed.
		/// </summary>
		public float TimeScale { get; set; } = 1f;

		/// <summary>
		/// When true (default), time advances locally in <see cref="Update"/>.
		/// When false (client in multiplayer), time only updates via <see cref="SetTime"/> from server sync.
		/// </summary>
		public bool IsAuthority { get; set; } = true;

		// Time constants (in hours)
		const float SunriseStart = 5f;    // Dawn begins
		const float SunriseEnd = 7f;      // Full daylight
		const float SunsetStart = 17f;    // Dusk begins
		const float SunsetEnd = 19f;      // Full night
		const float Midnight = 0f;
		const float Noon = 12f;

		// Sky light levels
		const float DayLightLevel = 1.0f;      // Full sunlight
		const float NightLightLevel = 0.15f;   // Moonlight
		const float MinAmbientNight = 1;       // Minimum ambient at night (0-15)
		const float MinAmbientDay = 2;         // Minimum ambient during day (0-15)

		// Sky colors
		static readonly Color SkyColorNight = new Color(10, 15, 30, 255);
		static readonly Color SkyColorDawn = new Color(255, 180, 120, 255);
		static readonly Color SkyColorDay = new Color(135, 206, 235, 255);
		static readonly Color SkyColorDusk = new Color(255, 140, 100, 255);

		/// <summary>
		/// Current sky background color based on time of day.
		/// </summary>
		public Color SkyColor { get; private set; } = SkyColorDay;

		/// <summary>
		/// Current skylight multiplier (0-1) based on time of day.
		/// </summary>
		public float SkyLightMultiplier { get; private set; } = 1f;

		/// <summary>
		/// Current sun color based on time of day.
		/// </summary>
		public Color SunColor { get; private set; } = Color.White;

		/// <summary>
		/// Sun elevation angle in radians (0 = horizon, PI/2 = zenith, negative = below horizon).
		/// </summary>
		public float SunElevation { get; private set; } = 0f;

		/// <summary>
		/// Sun azimuth angle in radians (direction around Y axis).
		/// </summary>
		public float SunAzimuth { get; private set; } = 0f;

		// Sun colors for different times
		static readonly Color SunColorNoon = new Color(255, 255, 240, 255);      // Bright white-yellow
		static readonly Color SunColorDawn = new Color(255, 180, 100, 255);      // Orange
		static readonly Color SunColorDusk = new Color(255, 120, 80, 255);       // Red-orange

		/// <summary>
		/// Updates the day/night cycle. Call this every frame.
		/// </summary>
		/// <param name="deltaTime">Frame delta time in seconds.</param>
		public void Update(float deltaTime)
		{
			if (!IsAuthority || IsPaused || DayLengthSeconds <= 0)
				return;

			// Calculate hours per real second
			float hoursPerSecond = 24f / DayLengthSeconds;
			
			// Advance time
			TimeOfDay += deltaTime * hoursPerSecond * TimeScale;
			
			// Wrap around at 24 hours
			while (TimeOfDay >= 24f)
				TimeOfDay -= 24f;
			while (TimeOfDay < 0f)
				TimeOfDay += 24f;

			// Update lighting based on new time
			UpdateLighting();
		}

		/// <summary>
		/// Sets the time of day directly (0-24 hours).
		/// </summary>
		public void SetTime(float hours)
		{
			TimeOfDay = hours % 24f;
			if (TimeOfDay < 0f)
				TimeOfDay += 24f;
			UpdateLighting();
		}

		/// <summary>
		/// Calculates and applies lighting values based on current time.
		/// </summary>
		void UpdateLighting()
		{
			// Calculate sky light multiplier
			SkyLightMultiplier = CalculateSkyLightMultiplier();

			// Apply to the global BlockLight multiplier
			BlockLight.SkyLightMultiplier = SkyLightMultiplier;

			// Update ambient light level
			float ambientT = (SkyLightMultiplier - NightLightLevel) / (DayLightLevel - NightLightLevel);
			ambientT = Math.Clamp(ambientT, 0f, 1f);
			BlockLight.AmbientLight = (byte)Math.Round(Lerp(MinAmbientNight, MinAmbientDay, ambientT));

			// Calculate sky color
			SkyColor = CalculateSkyColor();

			// Calculate sun position and color
			CalculateSunPosition();
		}

		/// <summary>
		/// Calculates sun position (elevation and azimuth) based on time of day.
		/// Sun rises in east (azimuth 0), peaks at noon (elevation PI/2), sets in west (azimuth PI).
		/// </summary>
		void CalculateSunPosition()
		{
			// Sun travels from east to west over 12 hours (sunrise to sunset)
			// At 6:00 (sunrise): azimuth = 0, elevation = 0
			// At 12:00 (noon): azimuth = PI/2, elevation = PI/2 (zenith)
			// At 18:00 (sunset): azimuth = PI, elevation = 0

			float t = TimeOfDay;

			// Calculate sun azimuth (horizontal angle)
			// Maps 6:00-18:00 to 0-PI (east to west)
			float dayProgress = (t - 6f) / 12f; // 0 at 6:00, 1 at 18:00
			SunAzimuth = dayProgress * MathF.PI;

			// Calculate sun elevation (vertical angle)
			// Peaks at noon (12:00), 0 at sunrise/sunset
			// Use sine curve for smooth arc
			if (t >= SunriseStart && t <= SunsetEnd)
			{
				float sunProgress = (t - SunriseStart) / (SunsetEnd - SunriseStart); // 0-1 over sun visible time
				SunElevation = MathF.Sin(sunProgress * MathF.PI) * (MathF.PI / 2f) * 0.8f; // Max ~72 degrees
			}
			else
			{
				SunElevation = -0.3f; // Below horizon at night
			}

			// Calculate sun color based on elevation
			if (SunElevation <= 0)
			{
				SunColor = new Color(0, 0, 0, 0); // Invisible when below horizon
			}
			else if (SunElevation < 0.3f) // Low sun (dawn/dusk)
			{
				float blend = SunElevation / 0.3f;
				SunColor = t < Noon ? 
					LerpColor(SunColorDawn, SunColorNoon, blend) : 
					LerpColor(SunColorDusk, SunColorNoon, 1f - blend);
			}
			else
			{
				SunColor = SunColorNoon;
			}
		}

		/// <summary>
		/// Gets the sun direction vector (pointing from origin toward sun).
		/// </summary>
		public Vector3 GetSunDirection()
		{
			// Convert spherical to cartesian coordinates
			float cosElev = MathF.Cos(SunElevation);
			return new Vector3(
				cosElev * MathF.Cos(SunAzimuth),
				MathF.Sin(SunElevation),
				cosElev * MathF.Sin(SunAzimuth)
			);
		}

		/// <summary>
		/// Calculates the skylight multiplier based on time of day.
		/// Smoothly transitions between day and night during sunrise/sunset.
		/// </summary>
		float CalculateSkyLightMultiplier()
		{
			float t = TimeOfDay;

			// Night (before sunrise)
			if (t < SunriseStart)
				return NightLightLevel;
			
			// Sunrise transition
			if (t < SunriseEnd)
			{
				float progress = (t - SunriseStart) / (SunriseEnd - SunriseStart);
				return Lerp(NightLightLevel, DayLightLevel, SmoothStep(progress));
			}
			
			// Day
			if (t < SunsetStart)
				return DayLightLevel;
			
			// Sunset transition
			if (t < SunsetEnd)
			{
				float progress = (t - SunsetStart) / (SunsetEnd - SunsetStart);
				return Lerp(DayLightLevel, NightLightLevel, SmoothStep(progress));
			}
			
			// Night (after sunset)
			return NightLightLevel;
		}

		/// <summary>
		/// Calculates the sky background color based on time of day.
		/// </summary>
		Color CalculateSkyColor()
		{
			float t = TimeOfDay;

			// Night (before dawn)
			if (t < SunriseStart)
				return SkyColorNight;
			
			// Dawn transition (night -> dawn -> day)
			if (t < SunriseEnd)
			{
				float progress = (t - SunriseStart) / (SunriseEnd - SunriseStart);
				if (progress < 0.5f)
				{
					// Night to dawn
					return LerpColor(SkyColorNight, SkyColorDawn, SmoothStep(progress * 2f));
				}
				else
				{
					// Dawn to day
					return LerpColor(SkyColorDawn, SkyColorDay, SmoothStep((progress - 0.5f) * 2f));
				}
			}
			
			// Day
			if (t < SunsetStart)
				return SkyColorDay;
			
			// Dusk transition (day -> dusk -> night)
			if (t < SunsetEnd)
			{
				float progress = (t - SunsetStart) / (SunsetEnd - SunsetStart);
				if (progress < 0.5f)
				{
					// Day to dusk
					return LerpColor(SkyColorDay, SkyColorDusk, SmoothStep(progress * 2f));
				}
				else
				{
					// Dusk to night
					return LerpColor(SkyColorDusk, SkyColorNight, SmoothStep((progress - 0.5f) * 2f));
				}
			}
			
			// Night (after dusk)
			return SkyColorNight;
		}

		/// <summary>
		/// Gets a formatted time string (e.g., "14:30").
		/// </summary>
		public string GetTimeString()
		{
			int hours = (int)TimeOfDay;
			int minutes = (int)((TimeOfDay - hours) * 60);
			return $"{hours:D2}:{minutes:D2}";
		}

		/// <summary>
		/// Gets the current period of day as a string.
		/// </summary>
		public string GetPeriodString()
		{
			if (TimeOfDay < SunriseStart) return "Night";
			if (TimeOfDay < SunriseEnd) return "Dawn";
			if (TimeOfDay < Noon) return "Morning";
			if (TimeOfDay < SunsetStart) return "Afternoon";
			if (TimeOfDay < SunsetEnd) return "Dusk";
			return "Night";
		}

		// Utility functions
		static float Lerp(float a, float b, float t) => a + (b - a) * t;
		
		static float SmoothStep(float t)
		{
			t = Math.Clamp(t, 0f, 1f);
			return t * t * (3f - 2f * t);
		}
		
		static Color LerpColor(Color a, Color b, float t)
		{
			return new Color(
				(byte)(a.R + (b.R - a.R) * t),
				(byte)(a.G + (b.G - a.G) * t),
				(byte)(a.B + (b.B - a.B) * t),
				(byte)(a.A + (b.A - a.A) * t)
			);
		}
	}
}
