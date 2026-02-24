using FishUI;
using FishUI.Controls;
using Raylib_cs;
using System;
using System.Numerics;
using Voxelgine.Engine;
using Voxelgine.Engine.DI;
using Voxelgine.Graphics;
using Voxelgine.GUI;

using ParticleBlendMode = Voxelgine.Engine.ParticleBlendMode;

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

		// Custom particle settings
		private string[] _texCollectionNames;
		private int _texCollectionIdx = 0;
		private int _texIndex = 0;
		private ParticleType _customType = ParticleType.Smoke;
		private ParticleBlendMode _blendMode = ParticleBlendMode.Alpha;
		private bool _emissive = false;
		private bool _physics = true;
		private bool _noCollisions = false;
		private float _spread = 0.3f;
		private Slider _sliderTexIdx;
		private float _clipboardTimer;

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

			_texCollectionNames = ResMgr.GetCollectionNames();

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

			// === Custom Particle ===
			stack.AddChild(new Label { Text = "Custom Particle:", Size = new Vector2(cw, 24) });

			// Particle type selector (cycling button)
			string[] typeNames = ["Smoke", "Fire", "Blood", "Spark"];
			var btnCustomType = new Button { Text = $"Type: {typeNames[(int)_customType]}", Size = new Vector2(cw, 32) };
			btnCustomType.Clicked += (s, e) =>
			{
				_customType = (ParticleType)(((int)_customType + 1) % typeNames.Length);
				btnCustomType.Text = $"Type: {typeNames[(int)_customType]}";
			};
			stack.AddChild(btnCustomType);

			// Texture collection selector (cycling button)
			var btnTexCollection = new Button { Text = $"Texture: {_texCollectionNames[_texCollectionIdx]}", Size = new Vector2(cw, 32) };
			btnTexCollection.Clicked += (s, e) =>
			{
				_texCollectionIdx = (_texCollectionIdx + 1) % _texCollectionNames.Length;
				btnTexCollection.Text = $"Texture: {_texCollectionNames[_texCollectionIdx]}";
				int maxIdx = ResMgr.GetCollectionSize(_texCollectionNames[_texCollectionIdx]) - 1;
				_sliderTexIdx.MaxValue = Math.Max(maxIdx, 1);
				_texIndex = 0;
				_sliderTexIdx.Value = 0;
			};
			stack.AddChild(btnTexCollection);

			// Texture index slider
			stack.AddChild(new Label { Text = "Texture Index:", Size = new Vector2(cw, 18) });
			int initialMaxIdx = _texCollectionNames.Length > 0 ? ResMgr.GetCollectionSize(_texCollectionNames[0]) - 1 : 0;
			_sliderTexIdx = new Slider
			{
				Size = new Vector2(cw, 24),
				MinValue = 0,
				MaxValue = Math.Max(initialMaxIdx, 1),
				Value = 0,
				Step = 1,
				ShowValueLabel = true,
				ValueLabelFormat = "0"
			};
			_sliderTexIdx.OnValueChanged += (s, val) => _texIndex = (int)val;
			stack.AddChild(_sliderTexIdx);

			// Blend mode selector (cycling button)
			string[] blendNames = ["Additive", "FireType", "AlphaPremul", "Multiply", "Alpha"];
			var btnBlend = new Button { Text = $"Blend: {blendNames[(int)_blendMode]}", Size = new Vector2(cw, 32) };
			btnBlend.Clicked += (s, e) =>
			{
				_blendMode = (ParticleBlendMode)(((int)_blendMode + 1) % blendNames.Length);
				btnBlend.Text = $"Blend: {blendNames[(int)_blendMode]}";
			};
			stack.AddChild(btnBlend);

			// Emissive toggle
			var btnEmissive = new Button { Text = "[ ] Emissive", Size = new Vector2(cw, 32) };
			btnEmissive.Clicked += (s, e) =>
			{
				_emissive = !_emissive;
				btnEmissive.Text = _emissive ? "[X] Emissive" : "[ ] Emissive";
			};
			stack.AddChild(btnEmissive);

			// Physics toggle
			var btnPhysics = new Button { Text = "[X] Physics", Size = new Vector2(cw, 32) };
			btnPhysics.Clicked += (s, e) =>
			{
				_physics = !_physics;
				btnPhysics.Text = _physics ? "[X] Physics" : "[ ] Physics";
			};
			stack.AddChild(btnPhysics);

			// No Collisions toggle
			var btnNoCollisions = new Button { Text = "[ ] No Collisions", Size = new Vector2(cw, 32) };
			btnNoCollisions.Clicked += (s, e) =>
			{
				_noCollisions = !_noCollisions;
				btnNoCollisions.Text = _noCollisions ? "[X] No Collisions" : "[ ] No Collisions";
			};
			stack.AddChild(btnNoCollisions);

			// Spread slider
			stack.AddChild(new Label { Text = "Spread:", Size = new Vector2(cw, 18) });
			var sliderSpread = new Slider
			{
				Size = new Vector2(cw, 24),
				MinValue = 0.0f,
				MaxValue = 3.0f,
				Value = 0.3f,
				Step = 0.05f,
				ShowValueLabel = true,
				ValueLabelFormat = "0.00"
			};
			sliderSpread.OnValueChanged += (s, val) => _spread = val;
			stack.AddChild(sliderSpread);

			// Spawn Custom button
			var btnSpawnCustom = new Button { Text = "Spawn Custom", Size = new Vector2(cw, 40) };
			btnSpawnCustom.Clicked += (s, e) => SpawnCustomEffect();
			stack.AddChild(btnSpawnCustom);

			// Export C# Code button
			var btnExportCode = new Button { Text = "Export C# Code", Size = new Vector2(cw, 32) };
			btnExportCode.Clicked += (s, e) =>
			{
				string code = GenerateCustomParticleCode();
				Raylib.SetClipboardText(code);
				_clipboardTimer = 3.0f;
			};
			stack.AddChild(btnExportCode);

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

		private void SpawnCustomEffect()
		{
			string collName = _texCollectionNames[_texCollectionIdx];
			Texture2D tex = ResMgr.GetFromCollectionByIndex(collName, _texIndex);
			Color clr = new Color(_colorR, _colorG, _colorB, (byte)255);
			Vector3 spawnPos = new Vector3(0, 1, 0);

			for (int i = 0; i < _count; i++)
			{
				Vector3 offset = new Vector3(
					(Random.Shared.NextSingle() - 0.5f) * _spread,
					(Random.Shared.NextSingle() - 0.5f) * _spread,
					(Random.Shared.NextSingle() - 0.5f) * _spread
				);

				Vector3 vel = new Vector3(
					(Random.Shared.NextSingle() - 0.5f) * _speed * _spread,
					_speed * (0.5f + Random.Shared.NextSingle()),
					(Random.Shared.NextSingle() - 0.5f) * _speed * _spread
				);

				_particles.SpawnCustom(
					spawnPos + offset,
					vel,
					clr,
					tex,
					_scale,
					_lifetime,
					_customType,
					_blendMode,
					_emissive,
					_physics,
					_noCollisions
				);
			}
		}

		private string GenerateCustomParticleCode()
		{
			var ic = System.Globalization.CultureInfo.InvariantCulture;
			string typeName = _customType.ToString();
			string blendName = _blendMode.ToString();
			string collName = _texCollectionNames[_texCollectionIdx];
			string scaler = _customType == ParticleType.Smoke ? "0.4f" : "0";

			string F(float v) => v.ToString("F2", ic) + "f";
			string B(bool v) => v ? "true" : "false";

			var sb = new System.Text.StringBuilder();
			sb.AppendLine("\t\t/// <summary>");
			sb.AppendLine($"\t\t/// Custom particle \u2014 exported from Effects Preview.");
			sb.AppendLine($"\t\t/// Type: {typeName}, Texture: {collName}[{_texIndex}], Blend: {blendName}");
			sb.AppendLine("\t\t/// </summary>");
			sb.AppendLine("\t\tpublic void SpawnMyEffect(Vector3 Pos, Vector3 Direction, Color Clr, float ScaleFactor = 1.0f)");
			sb.AppendLine("\t\t{");
			sb.AppendLine("\t\t\tfor (int i = 0; i < Particles.Length; i++)");
			sb.AppendLine("\t\t\t{");
			sb.AppendLine("\t\t\t\tref Particle P = ref Particles[i];");
			sb.AppendLine();
			sb.AppendLine("\t\t\t\tif (!P.Draw)");
			sb.AppendLine("\t\t\t\t{");
			sb.AppendLine("\t\t\t\t\tP.Draw = true;");
			sb.AppendLine("\t\t\t\t\tP.Pos = Pos;");
			sb.AppendLine("\t\t\t\t\tP.Color = Clr;");
			sb.AppendLine();
			sb.AppendLine($"\t\t\t\t\tfloat spreadX = (Random.Shared.NextSingle() - 0.5f) * {F(_spread)};");
			sb.AppendLine($"\t\t\t\t\tfloat spreadY = (Random.Shared.NextSingle() - 0.5f) * {F(_spread)};");
			sb.AppendLine($"\t\t\t\t\tfloat spreadZ = (Random.Shared.NextSingle() - 0.5f) * {F(_spread)};");
			sb.AppendLine("\t\t\t\t\tP.Vel = Direction + new Vector3(spreadX, spreadY, spreadZ);");
			sb.AppendLine();
			sb.AppendLine("\t\t\t\t\tP.SpawnedAt = lastGameTime;");
			sb.AppendLine($"\t\t\t\t\tP.LifeTime = {F(_lifetime)} + Random.Shared.NextSingle() * {F(_lifetime * 0.4f)};");
			sb.AppendLine($"\t\t\t\t\tP.MovePhysics = {B(_physics)};");
			sb.AppendLine($"\t\t\t\t\tP.Tex = ResMgr.GetFromCollectionByIndex(\"{collName}\", {_texIndex});");
			sb.AppendLine($"\t\t\t\t\tP.Scaler = {scaler};");
			sb.AppendLine($"\t\t\t\t\tP.InitialScale = ({F(_scale)} + Random.Shared.NextSingle() * {F(_scale * 0.3f)}) * ScaleFactor;");
			sb.AppendLine("\t\t\t\t\tP.Scale = P.InitialScale;");
			sb.AppendLine("\t\t\t\t\tP.Rnd = Random.Shared.NextSingle();");
			sb.AppendLine($"\t\t\t\t\tP.Type = ParticleType.{typeName};");
			sb.AppendLine($"\t\t\t\t\tP.IsEmissive = {B(_emissive)};");
			sb.AppendLine($"\t\t\t\t\tP.BlendMode = ParticleBlendMode.{blendName};");
			sb.AppendLine($"\t\t\t\t\tP.NoCollisions = {B(_noCollisions)};");
			sb.AppendLine();
			sb.AppendLine("\t\t\t\t\treturn;");
			sb.AppendLine("\t\t\t\t}");
			sb.AppendLine("\t\t\t}");
			sb.AppendLine("\t\t}");

			return sb.ToString();
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
					SpawnCustomEffect();
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

			// Clipboard notification
			if (_clipboardTimer > 0)
			{
				_clipboardTimer -= dt;
				Raylib.DrawText("Code copied to clipboard!", Window.Width / 2 - 130, Window.Height - 60, 20, Color.Lime);
			}

			// Draw instructions
			Raylib.DrawText("Drag mouse to rotate | Scroll to zoom", Window.Width - 320, Window.Height - 30, 16, Color.LightGray);
			Raylib.DrawFPS(Window.Width - 100, 10);
		}
	}
}
