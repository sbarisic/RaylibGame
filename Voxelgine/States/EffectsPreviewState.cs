using FishUI;
using FishUI.Controls;
using Raylib_cs;
using System;
using System.Numerics;
using Voxelgine.Engine;
using Voxelgine.Engine.DI;
using Voxelgine.Graphics;
using Voxelgine.GUI;

namespace Voxelgine.States
{
	/// <summary>
	/// Preview state for testing particle effects (smoke, fire, blood, sparks)
	/// with adjustable parameters. Similar to NPCPreviewState.
	/// </summary>
	public class EffectsPreviewState : GameStateImpl
	{
		private FishUIManager _gui;
		private ParticleSystem _particles;
		private Camera3D _camera;
		private float _cameraAngle = 0f;
		private float _cameraDistance = 6f;
		private float _cameraHeight = 2f;
		private float _totalTime;

		// UI elements
		private Label _statsLabel;
		private Label _paramLabel;

		// Adjustable parameters
		private float _scale = 1.0f;
		private float _speed = 1.0f;
		private float _lifetime = 1.0f;
		private int _count = 5;
		private byte _colorR = 255;
		private byte _colorG = 255;
		private byte _colorB = 255;
		private bool _autoSpawn;
		private float _autoSpawnTimer;
		private const float AutoSpawnInterval = 0.3f;

		public EffectsPreviewState(IGameWindow window, IFishEngineRunner Eng) : base(window, Eng)
		{
			_gui = new FishUIManager(window, Eng.DI.GetRequiredService<IFishLogging>());

			_camera = new Camera3D(
				new Vector3(0, 2, 6),
				new Vector3(0, 1, 0),
				Vector3.UnitY,
				45,
				CameraProjection.Perspective
			);

			_particles = new ParticleSystem();
			_particles.Init(
				point => false,
				point => BlockType.None,
				point => Color.White
			);

			CreateUI();
		}

		private void CreateUI()
		{
			var windowSize = new Vector2(360, 700);
			var windowPos = new Vector2(20, 20);
			float cw = 260; // control width

			var controlsWindow = new Window
			{
				Title = "Effects Preview",
				Position = windowPos,
				Size = windowSize,
				IsResizable = false,
				ShowCloseButton = false
			};

			var scroll = new ScrollablePane
			{
				Position = new Vector2(0, 0),
				Size = controlsWindow.GetContentSize(),
				Anchor = FishUIAnchor.All,
				AutoContentSize = true
			};

			var stack = new StackLayout
			{
				Orientation = StackOrientation.Vertical,
				Spacing = 6,
				Position = new Vector2(10, 10),
				Size = new Vector2(windowSize.X - 40, 1200),
				IsTransparent = true
			};

			// Stats
			_statsLabel = new Label
			{
				Text = "Particles: 0/0/256",
				Size = new Vector2(cw, 24)
			};
			stack.AddChild(_statsLabel);

			_paramLabel = new Label
			{
				Text = "Color: (255,255,255)",
				Size = new Vector2(cw, 24)
			};
			stack.AddChild(_paramLabel);

			// === Spawn Buttons ===
			stack.AddChild(new Label { Text = "Spawn Effects:", Size = new Vector2(cw, 24) });

			var btnSmoke = new Button { Text = "Smoke", Size = new Vector2(cw, 32) };
			btnSmoke.Clicked += (s, e) => SpawnEffect(ParticleType.Smoke);
			stack.AddChild(btnSmoke);

			var btnFire = new Button { Text = "Fire", Size = new Vector2(cw, 32) };
			btnFire.Clicked += (s, e) => SpawnEffect(ParticleType.Fire);
			stack.AddChild(btnFire);

			var btnBlood = new Button { Text = "Blood", Size = new Vector2(cw, 32) };
			btnBlood.Clicked += (s, e) => SpawnEffect(ParticleType.Blood);
			stack.AddChild(btnBlood);

			var btnSpark = new Button { Text = "Sparks", Size = new Vector2(cw, 32) };
			btnSpark.Clicked += (s, e) => SpawnEffect(ParticleType.Spark);
			stack.AddChild(btnSpark);

			// === Parameters ===
			stack.AddChild(new Label { Text = "Parameters:", Size = new Vector2(cw, 24) });

			// Scale
			stack.AddChild(new Label { Text = "Scale:", Size = new Vector2(cw, 18) });
			var sliderScale = new Slider
			{
				Size = new Vector2(cw, 24),
				MinValue = 0.1f,
				MaxValue = 5.0f,
				Value = 1.0f,
				Step = 0.1f,
				ShowValueLabel = true,
				ValueLabelFormat = "0.00"
			};
			sliderScale.OnValueChanged += (s, val) => _scale = val;
			stack.AddChild(sliderScale);

			// Speed
			stack.AddChild(new Label { Text = "Speed:", Size = new Vector2(cw, 18) });
			var sliderSpeed = new Slider
			{
				Size = new Vector2(cw, 24),
				MinValue = 0.1f,
				MaxValue = 5.0f,
				Value = 1.0f,
				Step = 0.1f,
				ShowValueLabel = true,
				ValueLabelFormat = "0.00"
			};
			sliderSpeed.OnValueChanged += (s, val) => _speed = val;
			stack.AddChild(sliderSpeed);

			// Lifetime
			stack.AddChild(new Label { Text = "Lifetime:", Size = new Vector2(cw, 18) });
			var sliderLife = new Slider
			{
				Size = new Vector2(cw, 24),
				MinValue = 0.1f,
				MaxValue = 5.0f,
				Value = 1.0f,
				Step = 0.1f,
				ShowValueLabel = true,
				ValueLabelFormat = "0.00"
			};
			sliderLife.OnValueChanged += (s, val) => _lifetime = val;
			stack.AddChild(sliderLife);

			// Count per click
			stack.AddChild(new Label { Text = "Count (per click):", Size = new Vector2(cw, 18) });
			var sliderCount = new Slider
			{
				Size = new Vector2(cw, 24),
				MinValue = 1,
				MaxValue = 50,
				Value = 5,
				Step = 1,
				ShowValueLabel = true,
				ValueLabelFormat = "0"
			};
			sliderCount.OnValueChanged += (s, val) => _count = (int)val;
			stack.AddChild(sliderCount);

			// === Color ===
			stack.AddChild(new Label { Text = "Color:", Size = new Vector2(cw, 24) });

			stack.AddChild(new Label { Text = "Red:", Size = new Vector2(cw, 18) });
			var sliderR = new Slider
			{
				Size = new Vector2(cw, 24),
				MinValue = 0,
				MaxValue = 255,
				Value = 255,
				Step = 1,
				ShowValueLabel = true,
				ValueLabelFormat = "0"
			};
			sliderR.OnValueChanged += (s, val) => _colorR = (byte)val;
			stack.AddChild(sliderR);

			stack.AddChild(new Label { Text = "Green:", Size = new Vector2(cw, 18) });
			var sliderG = new Slider
			{
				Size = new Vector2(cw, 24),
				MinValue = 0,
				MaxValue = 255,
				Value = 255,
				Step = 1,
				ShowValueLabel = true,
				ValueLabelFormat = "0"
			};
			sliderG.OnValueChanged += (s, val) => _colorG = (byte)val;
			stack.AddChild(sliderG);

			stack.AddChild(new Label { Text = "Blue:", Size = new Vector2(cw, 18) });
			var sliderB = new Slider
			{
				Size = new Vector2(cw, 24),
				MinValue = 0,
				MaxValue = 255,
				Value = 255,
				Step = 1,
				ShowValueLabel = true,
				ValueLabelFormat = "0"
			};
			sliderB.OnValueChanged += (s, val) => _colorB = (byte)val;
			stack.AddChild(sliderB);

			// === Controls ===
			stack.AddChild(new Label { Text = "Controls:", Size = new Vector2(cw, 24) });

			var btnAutoSpawn = new Button { Text = "[ ] Auto Spawn", Size = new Vector2(cw, 32) };
			btnAutoSpawn.Clicked += (s, e) =>
			{
				_autoSpawn = !_autoSpawn;
				btnAutoSpawn.Text = _autoSpawn ? "[X] Auto Spawn" : "[ ] Auto Spawn";
			};
			stack.AddChild(btnAutoSpawn);

			var btnClear = new Button { Text = "Clear All", Size = new Vector2(cw, 32) };
			btnClear.Clicked += (s, e) => ClearParticles();
			stack.AddChild(btnClear);

			var btnBack = new Button { Text = "Back to Main Menu", Size = new Vector2(cw, 40) };
			btnBack.Clicked += (s, e) => Eng.DI.GetRequiredService<IGameWindow>().SetState(Eng.MainMenuState);
			stack.AddChild(btnBack);

			scroll.AddChild(stack);
			controlsWindow.AddChild(scroll);
			_gui.AddControl(controlsWindow);
		}

		private void SpawnEffect(ParticleType type)
		{
			Color clr = new Color(_colorR, _colorG, _colorB, (byte)255);
			Vector3 spawnPos = new Vector3(0, 1, 0);

			for (int i = 0; i < _count; i++)
			{
				// Slight random offset per particle so bursts spread out
				Vector3 offset = new Vector3(
					(Random.Shared.NextSingle() - 0.5f) * 0.3f,
					(Random.Shared.NextSingle() - 0.5f) * 0.3f,
					(Random.Shared.NextSingle() - 0.5f) * 0.3f
				);

				switch (type)
				{
					case ParticleType.Smoke:
					{
						Vector3 vel = new Vector3(
							(Random.Shared.NextSingle() - 0.5f) * _speed,
							0.5f * _speed + Random.Shared.NextSingle() * _speed,
							(Random.Shared.NextSingle() - 0.5f) * _speed
						);
						_particles.SpawnSmoke(spawnPos + offset, vel, clr);
						break;
					}
					case ParticleType.Fire:
					{
						Vector3 force = new Vector3(
							(Random.Shared.NextSingle() - 0.5f) * _speed,
							_speed,
							(Random.Shared.NextSingle() - 0.5f) * _speed
						);
						_particles.SpawnFire(spawnPos + offset, force, clr, _scale, true, _lifetime);
						break;
					}
					case ParticleType.Blood:
					{
						Vector3 normal = Vector3.Normalize(new Vector3(
							(Random.Shared.NextSingle() - 0.5f),
							0.5f + Random.Shared.NextSingle() * 0.5f,
							(Random.Shared.NextSingle() - 0.5f)
						));
						_particles.SpawnBlood(spawnPos + offset, normal * _speed, _scale);
						break;
					}
					case ParticleType.Spark:
					{
						Vector3 dir = Vector3.Normalize(new Vector3(
							(Random.Shared.NextSingle() - 0.5f),
							0.5f + Random.Shared.NextSingle() * 0.5f,
							(Random.Shared.NextSingle() - 0.5f)
						));
						_particles.SpawnSpark(spawnPos + offset, dir * _speed, clr, _scale);
						break;
					}
				}
			}
		}

		private void ClearParticles()
		{
			// Re-initialize to clear all particles
			_particles.Init(
				point => false,
				point => BlockType.None,
				point => Color.White
			);
		}

		public override void SwapTo()
		{
			base.SwapTo();
			Raylib.EnableCursor();
		}

		public override void Tick(float GameTime)
		{
			float dt = Raylib.GetFrameTime();
			_totalTime += dt;

			// Camera orbit control with mouse drag
			if (Raylib.IsMouseButtonDown(MouseButton.Left) && !IsMouseOverUI())
			{
				Vector2 mouseDelta = Raylib.GetMouseDelta();
				_cameraAngle += mouseDelta.X * 0.01f;
				_cameraHeight += mouseDelta.Y * 0.03f;
				_cameraHeight = Math.Clamp(_cameraHeight, 0.5f, 5f);
			}

			// Zoom with scroll wheel
			float scroll = Raylib.GetMouseWheelMove();
			_cameraDistance -= scroll * 0.5f;
			_cameraDistance = Math.Clamp(_cameraDistance, 2f, 15f);

			// Update camera position
			_camera.Position = new Vector3(
				MathF.Sin(_cameraAngle) * _cameraDistance,
				_cameraHeight,
				MathF.Cos(_cameraAngle) * _cameraDistance
			);
			_camera.Target = new Vector3(0, 1, 0);

			// Auto spawn
			if (_autoSpawn)
			{
				_autoSpawnTimer += dt;
				if (_autoSpawnTimer >= AutoSpawnInterval)
				{
					_autoSpawnTimer -= AutoSpawnInterval;
					// Cycle through all types
					int typeIdx = (int)(_totalTime / AutoSpawnInterval) % 4;
					SpawnEffect((ParticleType)typeIdx);
				}
			}

			// ESC to go back
			if (Window.InMgr.IsInputPressed(InputKey.Esc))
			{
				Eng.DI.GetRequiredService<IGameWindow>().SetState(Eng.MainMenuState);
			}
		}

		private bool IsMouseOverUI()
		{
			Vector2 mousePos = Raylib.GetMousePosition();
			return mousePos.X < 390 && mousePos.Y < 730;
		}

		public override void UpdateLockstep(float TotalTime, float Dt, InputMgr InMgr)
		{
			_particles.Tick(TotalTime);
		}

		public override void Draw(float TimeAlpha, ref GameFrameInfo LastFrame, ref GameFrameInfo FInfo)
		{
			Raylib.ClearBackground(new Color(40, 44, 52));
			Raylib.BeginMode3D(_camera);

			// Draw ground grid
			Raylib.DrawGrid(10, 1.0f);

			// Draw coordinate axes
			Raylib.DrawLine3D(Vector3.Zero, new Vector3(2, 0, 0), Color.Red);
			Raylib.DrawLine3D(Vector3.Zero, new Vector3(0, 2, 0), Color.Green);
			Raylib.DrawLine3D(Vector3.Zero, new Vector3(0, 0, 2), Color.Blue);

			// Draw scale reference block (1×1×1, top face at Y=1 where particles spawn)
			Vector3 blockCenter = new Vector3(0, 0.5f, 0);
			Vector3 blockSize = new Vector3(1, 1, 1);
			Raylib.DrawCubeV(blockCenter, blockSize, new Color(120, 120, 120, 180));
			Raylib.DrawCubeWiresV(blockCenter, blockSize, new Color(200, 200, 200, 255));

			// Draw spawn point marker
			Raylib.DrawSphere(new Vector3(0, 1, 0), 0.05f, Color.Yellow);

			// Draw particles
			_particles.DrawPreview(_camera, _camera.Position);

			Raylib.EndMode3D();
		}

		public override void Draw2D()
		{
			float dt = Raylib.GetFrameTime();
			_gui.Tick(dt, _totalTime);

			// Update stats
			_particles.GetStats(out int onScreen, out int drawn, out int max);
			_statsLabel.Text = $"Particles: {onScreen}/{drawn}/{max}";
			_paramLabel.Text = $"Color: ({_colorR},{_colorG},{_colorB})";

			// Draw instructions
			Raylib.DrawText("Drag mouse to rotate | Scroll to zoom", Window.Width - 320, Window.Height - 30, 16, Color.LightGray);
			Raylib.DrawFPS(Window.Width - 100, 10);
		}
	}
}
