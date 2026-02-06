using RaylibGame.Engine;
using Raylib_cs;
using System;
using System.Diagnostics;
using System.Numerics;
using Voxelgine.Graphics;
using Voxelgine.GUI;
using Voxelgine.Engine.DI;

namespace Voxelgine.Engine
{
	/// <summary>
	/// Represents the player entity with first-person camera, physics, inventory, and input handling.
	/// Implements Quake-style movement physics including strafe-jumping, bunny-hopping, and swimming.
	/// </summary>
	/// <remarks>
	/// The player uses a separate physics position and interpolated render camera for smooth visuals.
	/// Physics are updated at a fixed timestep while rendering interpolates between states.
	/// 
	/// This class is split into multiple partial class files:
	/// - Player.cs: Core fields, constructor, position/camera management
	/// - Player.Physics.cs: Movement, collision, swimming physics
	/// - Player.Input.cs: Input handling, key bindings, Tick
	/// - Player.GUI.cs: GUI/Inventory setup and updates
	/// - Player.Rendering.cs: Draw, viewmodel rendering
	/// - Player.Serialization.cs: Save/load functionality
	/// </remarks>
	public unsafe partial class Player
	{
		/// <summary>The physics camera used for game logic and collision.</summary>
		public Camera3D Cam = new Camera3D(Vector3.Zero, Vector3.UnitX, Vector3.UnitY, 90, CameraProjection.Perspective);

		/// <summary>The interpolated camera used for smooth rendering (updated each frame).</summary>
		public Camera3D RenderCam = new Camera3D(Vector3.Zero, Vector3.UnitX, Vector3.UnitY, 90, CameraProjection.Perspective);

		FishUIManager GUI;

		/// <summary>The first-person view model (weapon/tool) renderer.</summary>
		public ViewModel ViewMdl;

		/// <summary>When true, player can fly through blocks without collision.</summary>
		public bool NoClip;
		/// <summary>When true, freezes the view frustum for debugging culling.</summary>
		public bool FreezeFrustum = false;

		Stopwatch LegTimer = Stopwatch.StartNew();
		long LastWalkSound = 0;
		long LastJumpSound = 0;
		long LastCrashSound = 0;
		long LastSwimSound = 0;

		Vector3 PreviousPosition;
		bool LocalPlayer;
		SoundMgr Snd;

		/// <summary>Player collision cylinder height in world units.</summary>
		public const float PlayerHeight = 1.7f;
		/// <summary>Eye position offset from feet (camera height).</summary>
		public const float PlayerEyeOffset = 1.6f;
		/// <summary>Player collision cylinder radius.</summary>
		public const float PlayerRadius = 0.4f;

		/// <summary>Axis-aligned bounding box for the player, updated when position changes.</summary>
		public BoundingBox BBox { get; private set; }

		public Vector3 Position;
		public bool CursorDisabled = false;

		/// <summary>
		/// Called when F1 toggles the menu/cursor state. Parameter is true when cursor is now visible (menu open).
		/// </summary>
		public Action<bool> OnMenuToggled;

		public Vector3 FeetPosition => Position - new Vector3(0, PlayerEyeOffset, 0);

		// --- Camera direction vectors ---
		Vector3 Fwd;
		Vector3 Left;
		Vector3 Up;

		IFishEngineRunner Eng;
		IFishLogging Logging;

		public Player(IFishEngineRunner Eng, FishUIManager GUI, string ModelName, bool LocalPlayer, SoundMgr Snd)
		{
			this.Eng = Eng;
			this.Logging = Eng.DI.GetRequiredService<IFishLogging>();
			this.GUI = GUI;
			this.Snd = Snd;
			this.LocalPlayer = LocalPlayer;

			ViewMdl = new ViewModel(Eng);

			Position = Vector3.Zero;
			ToggleMouse(true);


		}

		public void SetPosition(int X, int Y, int Z)
		{
			Position = FPSCamera.Position = new Vector3(X, Y, Z);
			UpdateBoundingBox();
		}

		public void SetPosition(Vector3 Pos)
		{
			if (float.IsNaN(Pos.X) || float.IsNaN(Pos.Y) || float.IsNaN(Pos.Z))
				return;

			PreviousPosition = Position;
			Position = FPSCamera.Position = Pos;
			UpdateBoundingBox();
		}

		/// <summary>
		/// Recalculates the player's axis-aligned bounding box based on current position.
		/// </summary>
		private void UpdateBoundingBox()
		{
			Vector3 feet = FeetPosition;
			BBox = new BoundingBox(
				new Vector3(feet.X - PlayerRadius, feet.Y, feet.Z - PlayerRadius),
				new Vector3(feet.X + PlayerRadius, feet.Y + PlayerHeight, feet.Z + PlayerRadius)
			);
		}

		public Vector3 GetPreviousPosition()
		{
			return PreviousPosition;
		}

		public Vector3 GetForward() => Fwd;
		public Vector3 GetLeft() => Left;
		public Vector3 GetUp() => Up;

		public void SetCamAngle(Vector3 CamAngle)
		{
			FPSCamera.CamAngle = CamAngle;
		}

		public Vector3 GetCamAngle()
		{
			return FPSCamera.CamAngle;
		}

		public void UpdateFPSCamera(ref GameFrameInfo FInfo)
		{
			Fwd = FPSCamera.GetForward();
			Left = FPSCamera.GetLeft();
			Up = FPSCamera.GetUp();
		}

		/// <summary>
		/// Plays a sound combo at the specified position.
		/// </summary>
		public void PlaySound(string comboName, Vector3 soundPos)
		{
			Snd.PlayCombo(comboName, Position, GetForward(), soundPos);
		}
	}
}
