using Voxelgine.Engine;
using Raylib_cs;
using System;
using System.Numerics;

namespace Voxelgine.States
{
	public unsafe partial class MPClientGameState
	{
		// ====================================== Rendering Helpers ===============================================

		private void DrawTransparent()
		{
			if (_simulation?.LocalPlayer != null)
				_simulation.Map.DrawTransparent(ref _viewFrustum, _simulation.LocalPlayer.Position);
		}

		private void DrawBlockPlacementPreview()
		{
			if (_simulation?.LocalPlayer == null)
				return;

			InventoryItem activeItem = _simulation.LocalPlayer.GetActiveItem();
			if (activeItem == null || !activeItem.IsPlaceableBlock())
				return;

			Vector3 start = _simulation.LocalPlayer.Position;
			Vector3 dir = _simulation.LocalPlayer.GetForward();
			const float maxLen = 20f;

			Vector3? placementPos = activeItem.GetBlockPlacementPosition(_simulation.Map, start, dir, maxLen);
			if (placementPos == null)
				return;

			Vector3 center = placementPos.Value + new Vector3(0.5f, 0.5f, 0.5f);
			Raylib.DrawCubeWiresV(center, Vector3.One, Color.White);
		}

		private void DrawUnderwaterOverlay()
		{
			if (_simulation?.LocalPlayer == null)
				return;

			BlockType blockAtCamera = _simulation.Map.GetBlock(_simulation.LocalPlayer.Position);
			if (blockAtCamera != BlockType.Water)
				return;

			if (!_waterOverlayLoaded)
			{
				_waterOverlayLoaded = true;
				try
				{
					_waterOverlayTexture = ResMgr.GetTexture("overlay_water.png", TextureFilter.Bilinear);
				}
				catch
				{
					_waterOverlayTexture = null;
				}
			}

			int screenWidth = _gameWindow.Width;
			int screenHeight = _gameWindow.Height;

			if (_waterOverlayTexture.HasValue && _waterOverlayTexture.Value.Id != 0)
			{
				Texture2D tex = _waterOverlayTexture.Value;
				Rectangle srcRect = new Rectangle(0, 0, tex.Width, tex.Height);
				Rectangle destRect = new Rectangle(0, 0, screenWidth, screenHeight);
				Raylib.DrawTexturePro(tex, srcRect, destRect, Vector2.Zero, 0f, Color.White);
			}
			else
			{
				Color waterColor = new Color(30, 80, 150, 120);
				Raylib.DrawRectangle(0, 0, screenWidth, screenHeight, waterColor);
			}
		}

		private void LoadCelestialTextures()
		{
			if (_celestialTexturesLoaded)
				return;

			_celestialTexturesLoaded = true;
			try { _sunTexture = ResMgr.GetTexture("sun.png", TextureFilter.Bilinear); }
			catch { _sunTexture = null; }

			try { _moonTexture = ResMgr.GetTexture("moon.png", TextureFilter.Bilinear); }
			catch { _moonTexture = null; }
		}

		private void CalculateCelestialPositions()
		{
			if (_simulation?.LocalPlayer == null)
				return;

			LoadCelestialTextures();

			const float CelestialDistance = 100f;
			const float BaseSunScreenSize = 128f;
			const float BaseMoonScreenSize = 96f;

			_sunVisible = false;
			_moonVisible = false;

			var dayNight = _simulation.DayNight;
			var renderCam = _simulation.LocalPlayer.RenderCam;

			// Calculate sun screen position if above horizon
			if (dayNight.SunElevation > 0 && _sunTexture.HasValue)
			{
				Vector3 sunDir = dayNight.GetSunDirection();
				Vector3 sunWorldPos = renderCam.Position + sunDir * CelestialDistance;

				Vector3 toSun = Vector3.Normalize(sunWorldPos - renderCam.Position);
				Vector3 camForward = Vector3.Normalize(renderCam.Target - renderCam.Position);
				float dot = Vector3.Dot(toSun, camForward);

				if (dot > 0)
				{
					_sunScreenPos = Raylib.GetWorldToScreen(sunWorldPos, renderCam);
					float horizonScale = 1f + (1f - Math.Min(1f, dayNight.SunElevation / 0.5f)) * 0.3f;
					_sunScreenSize = BaseSunScreenSize * horizonScale;
					_sunVisible = true;
				}
			}

			// Calculate moon screen position if sun is below horizon
			if (dayNight.SunElevation <= 0 && _moonTexture.HasValue)
			{
				float moonElevation = MathF.Abs(dayNight.SunElevation) + 0.3f;
				moonElevation = MathF.Min(moonElevation, MathF.PI / 3f);

				float moonAzimuth = dayNight.SunAzimuth + MathF.PI;

				float cosElev = MathF.Cos(moonElevation);
				Vector3 moonDir = new Vector3(
					cosElev * MathF.Cos(moonAzimuth),
					MathF.Sin(moonElevation),
					cosElev * MathF.Sin(moonAzimuth)
				);

				Vector3 moonWorldPos = renderCam.Position + moonDir * CelestialDistance;

				Vector3 toMoon = Vector3.Normalize(moonWorldPos - renderCam.Position);
				Vector3 camForward = Vector3.Normalize(renderCam.Target - renderCam.Position);
				float dot = Vector3.Dot(toMoon, camForward);

				if (dot > 0)
				{
					_moonScreenPos = Raylib.GetWorldToScreen(moonWorldPos, renderCam);
					_moonScreenSize = BaseMoonScreenSize;
					_moonVisible = true;
				}
			}
		}

		private void DrawCelestialBodies()
		{
			if (_sunVisible && _sunTexture.HasValue)
			{
				Texture2D tex = _sunTexture.Value;
				float halfSize = _sunScreenSize / 2f;
				Rectangle srcRect = new Rectangle(0, 0, tex.Width, tex.Height);
				Rectangle destRect = new Rectangle(_sunScreenPos.X - halfSize, _sunScreenPos.Y - halfSize, _sunScreenSize, _sunScreenSize);
				Raylib.DrawTexturePro(tex, srcRect, destRect, Vector2.Zero, 0f, _simulation.DayNight.SunColor);
			}

			if (_moonVisible && _moonTexture.HasValue)
			{
				Texture2D tex = _moonTexture.Value;
				float halfSize = _moonScreenSize / 2f;
				Color moonColor = new Color(220, 230, 255, 255);
				Rectangle srcRect = new Rectangle(0, 0, tex.Width, tex.Height);
				Rectangle destRect = new Rectangle(_moonScreenPos.X - halfSize, _moonScreenPos.Y - halfSize, _moonScreenSize, _moonScreenSize);
				Raylib.DrawTexturePro(tex, srcRect, destRect, Vector2.Zero, 0f, moonColor);
			}
		}
	}
}
