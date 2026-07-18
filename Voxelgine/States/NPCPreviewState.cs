using FishUI.Controls;
using System.Numerics;
using Voxelgine.Engine;
using Voxelgine.Engine.DI;
using Voxelgine.FishGfxClient;
using Voxelgine.FishGfxClient.Entities;
using Voxelgine.FishGfxClient.Rendering;
using Voxelgine.GUI;
using FishGfx.Graphics;

namespace Voxelgine.States
{
	/// <summary>
	/// Preview state for testing NPC models and animations.
	/// Displays the NPC in a 3D viewport with animation controls.
	/// </summary>
	public class NPCPreviewState : GameStateImpl
	{
		private readonly FishUIManager _gui;
		private readonly FishGfxEntityRenderAssets _assets;
		private readonly FishGfxNpcRenderAdapter _previewNpc;
		private float _cameraAngle = 0f;
		private float _cameraDistance = 5f;
		private float _cameraHeight = 2f;
		private Vector2 _lastMousePosition;
		private string _animationName = "idle";

		// UI elements
		private Window _controlsWindow;
		private Label _animationLabel;
		private Label _timeLabel;

		public NPCPreviewState(IGameWindow window, IFishEngineRunner Eng) : base(window, Eng)
		{
			_gui = new FishUIManager(window, Eng.DI.GetRequiredService<IFishLogging>());
			IFishGfxGameWindow fishWindow = window as IFishGfxGameWindow
				?? throw new ArgumentException("NPC preview requires FishGfx.", nameof(window));
			_assets = new FishGfxEntityRenderAssets(fishWindow);
			_previewNpc = _assets.CreateNpcAdapter();

			CreateUI();
		}

		private void CreateUI()
		{
			var windowSize = new Vector2(280, 660);
			var windowPos = new Vector2(20, 20);

			_controlsWindow = new Window
			{
				Title = "NPC Animation Preview",
				Position = windowPos,
				Size = windowSize,
				IsResizable = false,
				ShowCloseButton = false
			};

			var stack = new StackLayout
			{
				Orientation = StackOrientation.Vertical,
				Spacing = 6,
				Position = new Vector2(10, 10),
				Size = new Vector2(windowSize.X - 40, windowSize.Y - 80),
				IsTransparent = true
			};

			// Animation info
			_animationLabel = new Label
			{
				Text = "Animation: idle",
				Size = new Vector2(220, 24)
			};
			stack.AddChild(_animationLabel);

			_timeLabel = new Label
			{
				Text = "Time: 0.00s",
				Size = new Vector2(220, 24)
			};
			stack.AddChild(_timeLabel);

			// Base Layer buttons section
			var baseLabel = new Label
			{
				Text = "Looping Animations:",
				Size = new Vector2(220, 24)
			};
			stack.AddChild(baseLabel);

			var btnBaseIdle = new Button { Text = "Idle", Size = new Vector2(220, 32) };
			btnBaseIdle.Clicked += (s, e) => _animationName = "idle";
			stack.AddChild(btnBaseIdle);

			var btnBaseWalk = new Button { Text = "Walk", Size = new Vector2(220, 32) };
			btnBaseWalk.Clicked += (s, e) => _animationName = "walk";
			stack.AddChild(btnBaseWalk);

			var btnBaseCrouch = new Button { Text = "Crouch", Size = new Vector2(220, 32) };
			btnBaseCrouch.Clicked += (s, e) => _animationName = "crouch";
			stack.AddChild(btnBaseCrouch);

			// Overlay Layer buttons section
			var overlayLabel = new Label
			{
				Text = "Action Animation:",
				Size = new Vector2(220, 24)
			};
			stack.AddChild(overlayLabel);

			var btnOverlayAttack = new Button { Text = "Attack", Size = new Vector2(220, 32) };
			btnOverlayAttack.Clicked += (s, e) => _animationName = "attack";
			stack.AddChild(btnOverlayAttack);

			var btnStopOverlay = new Button { Text = "Return to Idle", Size = new Vector2(220, 32) };
			btnStopOverlay.Clicked += (s, e) => _animationName = "idle";
			stack.AddChild(btnStopOverlay);

			// Control section
			var controlLabel = new Label
			{
				Text = "Controls:",
				Size = new Vector2(220, 24)
			};
			stack.AddChild(controlLabel);

			var btnStopAll = new Button { Text = "Stop All Layers", Size = new Vector2(220, 32) };
			btnStopAll.Clicked += (s, e) => _animationName = "idle";
			stack.AddChild(btnStopAll);

			var btnReset = new Button { Text = "Reset Pose", Size = new Vector2(220, 32) };
			btnReset.Clicked += (s, e) => _animationName = "idle";
			stack.AddChild(btnReset);

			// Back button
			var btnBack = new Button { Text = "Back to Main Menu", Size = new Vector2(220, 40) };
			btnBack.Clicked += (s, e) => Eng.DI.GetRequiredService<IGameWindow>().SetState(Eng.AsClient().MainMenuState);
			stack.AddChild(btnBack);

			_controlsWindow.AddChild(stack);
			_gui.AddControl(_controlsWindow);
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
			if (Window.InMgr.IsInputDown(InputKey.Click_Left) && mouse.X >= 300)
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
			_previewNpc.Update(
				new NpcRenderState(
					Vector3.Zero,
					new Vector3(0.9f, 1.8f, 0.9f),
					Vector3.UnitZ,
					_animationName,
					Vector3.Zero,
					EntityAssetIds.HumanoidTexture,
					Rgba32.White
				),
				timing.DeltaTime
			);
			_gui.Update(timing.DeltaTime, timing.TotalTime);
			_animationLabel.Text = $"Animation: {_animationName}";
			_timeLabel.Text = $"Time: {timing.TotalTime:F2}s";
		}

		public override GameStateRenderSettings GetRenderSettings(Vector2 framebufferSize)
		{
			FishGfx.Graphics.Camera camera = new();
			camera.Position = new Vector3(
				MathF.Sin(_cameraAngle) * _cameraDistance,
				_cameraHeight,
				MathF.Cos(_cameraAngle) * _cameraDistance
			);
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

			pass.DrawLine(
				new FishGfx.Vertex3(Vector3.Zero, FishGfx.Color.Red),
				new FishGfx.Vertex3(new Vector3(2, 0, 0), FishGfx.Color.Red)
			);
			pass.DrawLine(
				new FishGfx.Vertex3(Vector3.Zero, FishGfx.Color.Green),
				new FishGfx.Vertex3(new Vector3(0, 2, 0), FishGfx.Color.Green)
			);
			pass.DrawLine(
				new FishGfx.Vertex3(Vector3.Zero, FishGfx.Color.Blue),
				new FishGfx.Vertex3(new Vector3(0, 0, 2), FishGfx.Color.Blue)
			);
			_previewNpc.Render(pass);
		}

		public override void RenderOverlay(RenderPass pass, in FrameTiming timing)
		{
			_gui.Render(pass, timing.DeltaTime, timing.TotalTime);
		}

		public override void Dispose()
		{
			_gui.Dispose();
			_assets.Dispose();
		}
	}
}
