#if WINDOWS
using FishGfx;
using FishGfx.Graphics;
using System.Numerics;
using System.Runtime.CompilerServices;
using Voxelgine.Engine;
using Voxelgine.FishGfxClient.Assets;
using Voxelgine.FishGfxClient.Effects;

namespace Voxelgine.FishGfxClient.Rendering;

/// <summary>
/// Camera-relative sun and moon billboards rendered behind world geometry.
/// The sky itself is the scene clear color so it remains resolution independent.
/// </summary>
public sealed class FishGfxCelestialLayer : IDisposable
{
	private const float CelestialDistance = 350;
	private const float CelestialSize = 42;
	private static readonly ConditionalWeakTable<IFishGfxGameWindow, SharedTextures> TexturesByWindow = new();
	private readonly BillboardBatch billboards;
	private readonly SharedTextures textures;
	private bool disposed;

	public FishGfxCelestialLayer(IFishGfxGameWindow window)
	{
		ArgumentNullException.ThrowIfNull(window);
		billboards = new BillboardBatch(window.RenderWindow.Graphics);
		textures = TexturesByWindow.GetValue(window, static value => new SharedTextures(
			value.Assets.LoadColorTexture("celestial.sun", "data/textures/sun.png"),
			value.Assets.LoadColorTexture("celestial.moon", "data/textures/moon.png")
		));
	}

	public void Render(
		RenderPass pass,
		in GameCameraState camera,
		DayNightCycle dayNight
	)
	{
		ObjectDisposedException.ThrowIf(disposed, this);
		ArgumentNullException.ThrowIfNull(pass);
		ArgumentNullException.ThrowIfNull(dayNight);

		Vector3 forward = camera.Forward;
		Vector3 up = camera.Up.LengthSquared() >= 0.000001f
			? Vector3.Normalize(camera.Up)
			: Vector3.UnitY;
		Vector3 right = Vector3.Cross(forward, up);
		if (right.LengthSquared() < 0.000001f)
		{
			right = Vector3.UnitX;
		}
		right = Vector3.Normalize(right);
		up = Vector3.Normalize(Vector3.Cross(right, forward));
		billboards.Begin(camera.Position, right, up);

		Vector3 sunDirection = dayNight.GetSunDirection();
		if (sunDirection.LengthSquared() < 0.000001f)
		{
			sunDirection = Vector3.UnitY;
		}
		sunDirection = Vector3.Normalize(sunDirection);
		Rgba32 sunTint = dayNight.SunColor;
		if (sunTint.A > 0)
		{
			billboards.Add(
				textures.Sun.Value,
				camera.Position + sunDirection * CelestialDistance,
				new Vector2(CelestialSize),
				new Color(sunTint.R, sunTint.G, sunTint.B, sunTint.A),
				BillboardBlendMode.Additive
			);
		}

		float moonVisibility = Math.Clamp(
			(1 - dayNight.SkyLightMultiplier) / 0.85f,
			0,
			1
		);
		if (moonVisibility > 0.01f)
		{
			billboards.Add(
				textures.Moon.Value,
				camera.Position - sunDirection * CelestialDistance,
				new Vector2(CelestialSize * 0.8f),
				new Color(205, 220, 255, (byte)(255 * moonVisibility)),
				BillboardBlendMode.Alpha
			);
		}

		billboards.Flush(pass);
	}

	public void Dispose()
	{
		if (disposed)
		{
			return;
		}
		billboards.Dispose();
		disposed = true;
	}

	private sealed record SharedTextures(
		AssetHandle<Texture> Sun,
		AssetHandle<Texture> Moon
	);
}
#endif
