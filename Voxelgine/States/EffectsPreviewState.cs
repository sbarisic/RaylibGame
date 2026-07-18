using FishUI;
using FishUI.Controls;
using System;
using System.Numerics;
using Voxelgine.Engine;
using Voxelgine.Engine.DI;
using Voxelgine.FishGfxClient;
using Voxelgine.FishGfxClient.Effects;
using Voxelgine.FishGfxClient.Rendering;
using Voxelgine.GUI;

using FishGfx.Graphics;

namespace Voxelgine.States
{
	/// <summary>
	/// Preview state for testing particle effects (smoke, fire, blood, sparks)
	/// with adjustable parameters. Similar to NPCPreviewState.
	/// </summary>
	public class EffectsPreviewState : GameStateImpl
	{
		private static readonly string[] TextureCollections = ["smoke", "fire", "blood", "spark"];
		private readonly FishUIManager _gui;
		private readonly FishGfxParticlePreview _fishParticles;
		private float _cameraAngle = 0f;
		private float _cameraDistance = 6f;
		private float _cameraHeight = 2f;

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
		private string _selectedCollection;
		private Button _btnTexBrowse;
		private Window _browserWindow;
		private int _texIndex = 0;
		private ParticleEffectKind _customType = ParticleEffectKind.Smoke;
		private BillboardBlendMode _blendMode = BillboardBlendMode.Alpha;
		private bool _emissive = false;
		private bool _physics = true;
		private bool _noCollisions = false;
		private float _spread = 0.3f;
		private Slider _sliderTexIdx;
		private float _clipboardTimer;
		private Vector2 _lastMousePosition;

		public EffectsPreviewState(IGameWindow window, IFishEngineRunner Eng) : base(window, Eng)
		{
			_gui = new FishUIManager(window, Eng.DI.GetRequiredService<IFishLogging>());
			IFishGfxGameWindow fishWindow = window as IFishGfxGameWindow
				?? throw new ArgumentException("Effects preview requires FishGfx.", nameof(window));
			_fishParticles = new FishGfxParticlePreview(fishWindow);
			_selectedCollection = TextureCollections[0];

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
			btnSmoke.Clicked += (s, e) => SpawnEffect(ParticleEffectKind.Smoke);
			stack.AddChild(btnSmoke);

			var btnFire = new Button { Text = "Fire", Size = new Vector2(cw, 32) };
			btnFire.Clicked += (s, e) => SpawnEffect(ParticleEffectKind.Fire);
			stack.AddChild(btnFire);

			var btnBlood = new Button { Text = "Blood", Size = new Vector2(cw, 32) };
			btnBlood.Clicked += (s, e) => SpawnEffect(ParticleEffectKind.Blood);
			stack.AddChild(btnBlood);

			var btnSpark = new Button { Text = "Sparks", Size = new Vector2(cw, 32) };
			btnSpark.Clicked += (s, e) => SpawnEffect(ParticleEffectKind.Spark);
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
				_customType = (ParticleEffectKind)(((int)_customType + 1) % typeNames.Length);
				btnCustomType.Text = $"Type: {typeNames[(int)_customType]}";
			};
			stack.AddChild(btnCustomType);

			// Texture collection selector (opens browser window)
			_btnTexBrowse = new Button { Text = $"Texture: {_selectedCollection}", Size = new Vector2(cw, 32) };
			_btnTexBrowse.Clicked += (s, e) => ToggleTextureBrowser();
			stack.AddChild(_btnTexBrowse);

			// Texture index slider
			stack.AddChild(new Label { Text = "Texture Index:", Size = new Vector2(cw, 18) });
			int initialMaxIdx = GetTextureCount(_selectedCollection) - 1;
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
			string[] blendNames = Enum.GetNames<BillboardBlendMode>();
			var btnBlend = new Button { Text = $"Blend: {blendNames[(int)_blendMode]}", Size = new Vector2(cw, 32) };
			btnBlend.Clicked += (s, e) =>
			{
				_blendMode = (BillboardBlendMode)(((int)_blendMode + 1) % blendNames.Length);
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
				((IFishGfxGameWindow)Window).RenderWindow.ClipboardText = code;
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
			btnBack.Clicked += (s, e) => Eng.DI.GetRequiredService<IGameWindow>().SetState(Eng.AsClient().MainMenuState);
			stack.AddChild(btnBack);

			scroll.AddChild(stack);
			controlsWindow.AddChild(scroll);
			_gui.AddControl(controlsWindow);
		}

		private void SpawnEffect(ParticleEffectKind type)
		{
			_fishParticles.Spawn(
				type,
				_count,
				_speed,
				_scale,
				_lifetime,
				new FishGfx.Color(_colorR, _colorG, _colorB)
			);
		}

		private void SpawnCustomEffect()
		{
			_fishParticles.Spawn(
				_customType,
				_count,
				_speed,
				_scale,
				_lifetime,
				new FishGfx.Color(_colorR, _colorG, _colorB),
				_spread
			);
		}

		private string GenerateCustomParticleCode()
		{
			var culture = System.Globalization.CultureInfo.InvariantCulture;
			string Number(float value) => value.ToString("F2", culture) + "f";
			return $$"""
				// {{_selectedCollection}}/{{_texIndex + 1}}.png, blend {{_blendMode}},
				// emissive={{_emissive}}, physics={{_physics}}, noCollisions={{_noCollisions}}
				preview.Spawn(
					ParticleEffectKind.{{_customType}},
					{{_count}},
					{{Number(_speed)}},
					{{Number(_scale)}},
					{{Number(_lifetime)}},
					new FishGfx.Color({{_colorR}}, {{_colorG}}, {{_colorB}}),
					{{Number(_spread)}}
				);
				""";
		}

		private void ToggleTextureBrowser()
		{
			if (_browserWindow != null)
			{
				bool wasVisible = _browserWindow.Visible;
				_browserWindow.Visible = !wasVisible;
				return;
			}

			_browserWindow = CreateTextureBrowser();
			_gui.AddControl(_browserWindow);
		}

		private Window CreateTextureBrowser()
		{
			var win = new Window
			{
				Title = "Texture Browser",
				Position = new Vector2(390, 20),
				Size = new Vector2(280, 500),
				IsResizable = false,
				ShowCloseButton = true
			};

			win.OnClosed += (w) => { win.Visible = false; };

			var scroll = new ScrollablePane
			{
				Position = new Vector2(0, 0),
				Size = win.GetContentSize(),
				Anchor = FishUIAnchor.All,
				AutoContentSize = true
			};

			var stack = new StackLayout
			{
				Orientation = StackOrientation.Vertical,
				Spacing = 4,
				Position = new Vector2(6, 6),
				Size = new Vector2(250, 2000),
				IsTransparent = true
			};

			float bw = 240;

			stack.AddChild(new Label { Text = "Particle Atlases:", Size = new Vector2(bw, 20) });

			foreach (string collection in TextureCollections)
			{
				int fileCount = GetTextureCount(collection);
				if (fileCount == 0)
				{
					continue;
				}

				var btn = new Button { Text = $"{collection} ({fileCount})", Size = new Vector2(bw, 26) };
				btn.Clicked += (s, e) => SelectTextureSource(collection);
				stack.AddChild(btn);
			}

			scroll.AddChild(stack);
			win.AddChild(scroll);
			return win;
		}

		private void SelectTextureSource(string collectionName)
		{
			_selectedCollection = collectionName;
			_btnTexBrowse.Text = $"Texture: {collectionName}";

			int maxIdx = GetTextureCount(collectionName) - 1;
			_sliderTexIdx.MaxValue = Math.Max(maxIdx, 1);
			_texIndex = 0;
			_sliderTexIdx.Value = 0;

			if (_browserWindow != null)
				_browserWindow.Visible = false;
		}

		private static int GetTextureCount(string collectionName)
		{
			string directory = Path.Combine(
				AppContext.BaseDirectory,
				"data",
				"textures",
				collectionName
			);
			return Directory.Exists(directory)
				? Directory.EnumerateFiles(directory, "*.png").Count()
				: 0;
		}

		private void ClearParticles()
		{
			_fishParticles.Clear();
		}

		public override void SwapTo()
		{
			_gui.InputEnabled = true;
			IFishGfxGameWindow fishWindow = (IFishGfxGameWindow)Window;
			fishWindow.RenderWindow.CaptureCursor = false;
			fishWindow.RenderWindow.ShowCursor = true;
		}

		public override void SwapFrom()
		{
			_gui.InputEnabled = false;
		}

		public override void Tick(float GameTime)
		{
			if (Window.InMgr.IsInputPressed(InputKey.Esc))
			{
				Eng.DI.GetRequiredService<IGameWindow>().SetState(Eng.AsClient().MainMenuState);
			}
		}

		public override void BeginInputFrame()
		{
			_gui.BeginInputFrame();
		}

		public override void BeginFrame(in FrameTiming timing)
		{
			Vector2 mouse = Window.InMgr.GetMousePos();
			bool overUi = mouse.X < 390 && mouse.Y < 730
				|| _browserWindow is { Visible: true }
					&& mouse.X >= 390 && mouse.X < 670 && mouse.Y < 530;
			if (Window.InMgr.IsInputDown(InputKey.Click_Left) && !overUi)
			{
				Vector2 delta = mouse - _lastMousePosition;
				_cameraAngle += delta.X * 0.01f;
				_cameraHeight = Math.Clamp(_cameraHeight + delta.Y * 0.03f, 0.5f, 5f);
			}
			_lastMousePosition = mouse;
			_cameraDistance = Math.Clamp(
				_cameraDistance - Window.InMgr.GetMouseWheel() * 0.5f,
				2,
				15
			);

			if (_autoSpawn)
			{
				_autoSpawnTimer += timing.DeltaTime;
				while (_autoSpawnTimer >= AutoSpawnInterval)
				{
					_autoSpawnTimer -= AutoSpawnInterval;
					SpawnCustomEffect();
				}
			}
			_fishParticles.Update(timing.DeltaTime);
			_gui.Update(timing.DeltaTime, timing.TotalTime);
			_statsLabel.Text = $"Particles: {_fishParticles.ActiveCount}/256";
			_paramLabel.Text = $"Color: ({_colorR},{_colorG},{_colorB})";
			_clipboardTimer = MathF.Max(0, _clipboardTimer - timing.DeltaTime);
		}

		public override GameStateRenderSettings GetRenderSettings(Vector2 framebufferSize)
		{
			Vector3 cameraPosition = new(
				MathF.Sin(_cameraAngle) * _cameraDistance,
				_cameraHeight,
				MathF.Cos(_cameraAngle) * _cameraDistance
			);
			FishGfx.Graphics.Camera camera = new() { Position = cameraPosition };
			camera.LookAt(new Vector3(0, 1, 0));
			camera.SetPerspective(framebufferSize, 45 * MathF.PI / 180, 0.05f, 256);
			GameStateRenderSettings overlay = GameStateRenderSettings.CreateOverlay(
				new Vector2(Window.Width, Window.Height)
			);
			return new GameStateRenderSettings
			{
				WorldView = new RenderView(camera),
				ViewmodelView = new RenderView(camera),
				OverlayView = overlay.OverlayView,
				ClearColor = new FishGfx.Color(40, 44, 52),
			};
		}

		public override void RenderWorld(RenderPass pass, in FrameTiming timing)
		{
			for (int coordinate = -5; coordinate <= 5; coordinate++)
			{
				FishGfx.Color color = coordinate == 0
					? new FishGfx.Color(95, 105, 120)
					: new FishGfx.Color(62, 68, 78);
				pass.DrawLine(
					new FishGfx.Vertex3(new Vector3(coordinate, 0, -5), color),
					new FishGfx.Vertex3(new Vector3(coordinate, 0, 5), color)
				);
				pass.DrawLine(
					new FishGfx.Vertex3(new Vector3(-5, 0, coordinate), color),
					new FishGfx.Vertex3(new Vector3(5, 0, coordinate), color)
				);
			}

			Vector3 cameraPosition = new(
				MathF.Sin(_cameraAngle) * _cameraDistance,
				_cameraHeight,
				MathF.Cos(_cameraAngle) * _cameraDistance
			);
			_fishParticles.Render(pass, cameraPosition, new Vector3(0, 1, 0));
		}

		public override void RenderOverlay(RenderPass pass, in FrameTiming timing)
		{
			_gui.Render(pass, timing.DeltaTime, timing.TotalTime);
		}

		public override void Dispose()
		{
			_fishParticles.Dispose();
			_gui.Dispose();
		}
	}
}
