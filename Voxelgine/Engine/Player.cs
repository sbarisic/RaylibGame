using RaylibGame.Engine;

using Raylib_cs;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Voxelgine.Graphics;
using Voxelgine.GUI;

namespace Voxelgine.Engine {
	// TODO: Implement player as VEntity in class VEntPlayer
	public unsafe class Player {
		const bool DEBUG_PLAYER = true;

		public Camera3D Cam = new Camera3D(Vector3.Zero, Vector3.UnitX, Vector3.UnitY, 90, CameraProjection.Perspective);
		public Camera3D RenderCam = new Camera3D(Vector3.Zero, Vector3.UnitX, Vector3.UnitY, 90, CameraProjection.Perspective); // Interpolated camera for rendering only

		GUIManager GUI;

		public ViewModel ViewMdl;

		bool NoClip;
		public bool FreezeFrustum = false;

		Dictionary<InputKey, Action<OnKeyPressedEventArg>> OnKeyFuncs = new Dictionary<InputKey, Action<OnKeyPressedEventArg>>();

		Stopwatch LegTimer = Stopwatch.StartNew();
		long LastWalkSound = 0;
		long LastJumpSound = 0;
		long LastCrashSound = 0;

		Vector3 PreviousPosition;
		bool LocalPlayer;
		SoundMgr Snd;

		public const float PlayerHeight = 1.7f;
		public const float PlayerEyeOffset = 1.6f;
		public const float PlayerRadius = 0.4f;
		// TODO Implement bounding box calculation
		public BoundingBox BBox;

		public Vector3 Position;
		public bool CursorDisabled = false;

		public Vector3 FeetPosition => Position - new Vector3(0, PlayerEyeOffset, 0);

		// --- Player movement/physics fields ---
		Vector3 PlyVelocity = Vector3.Zero;
		bool WasLastLegsOnFloor = false;
		float GroundGraceTimer = 0f; // Coyote time for ground detection
		Vector3 LastWallNormal = Vector3.Zero;
		Stopwatch JumpCounter = Stopwatch.StartNew();
		float HeadBumpCooldown = 0f; // Cooldown applied when hitting head shortly after jumping

		public Player(GUIManager GUI, string ModelName, bool LocalPlayer, SoundMgr Snd) {
			this.GUI = GUI;
			this.Snd = Snd;
			this.LocalPlayer = LocalPlayer;


			ViewMdl = new ViewModel();

			Position = Vector3.Zero;
			ToggleMouse(false);
		}

		public void Init(ChunkMap Map) {
			Stopwatch SWatch = Stopwatch.StartNew();

			AddOnKeyPressed(InputKey.F2, (E) => {
				Console.WriteLine("Compute light!");
				SWatch.Restart();
				Map.ComputeLighting();
				SWatch.Stop();
				Console.Title = $"> {SWatch.ElapsedMilliseconds / 1000.0f} s";
			});

			AddOnKeyPressed(InputKey.F3, (E) => { Program.DebugMode = !Program.DebugMode; });

			AddOnKeyPressed(InputKey.F4, (E) => { Console.WriteLine("Clearing records"); Utils.ClearRaycastRecord(); });

			AddOnKeyPressed(InputKey.C, (E) => {
				NoClip = !NoClip;
				Console.WriteLine($"No-clip mode: {(NoClip ? "ON" : "OFF")}");
			});

			AddOnKeyPressed(InputKey.Num1, (K) => { Inventory?.SetSelectedIndex(0); });
			AddOnKeyPressed(InputKey.Num2, (K) => { Inventory?.SetSelectedIndex(1); });
			AddOnKeyPressed(InputKey.Num3, (K) => { Inventory?.SetSelectedIndex(2); });
			AddOnKeyPressed(InputKey.Num4, (K) => { Inventory?.SetSelectedIndex(3); });

			AddOnKeyPressed(InputKey.I, (K) => {
				if (Program.DebugMode) {
					FreezeFrustum = !FreezeFrustum;
				}
			});
		}

		public void ToggleMouse(bool? Enable = null) {
			if (Enable != null)
				CursorDisabled = !Enable.Value;

			if (CursorDisabled)
				Raylib.EnableCursor();
			else {
				Raylib.DisableCursor();

				Vector2 MPos = FPSCamera.GetPreviousMousePos();
				Raylib.SetMousePosition((int)MPos.X, (int)MPos.Y);
			}

			CursorDisabled = !CursorDisabled;
		}

		public void SetPosition(int X, int Y, int Z) {
			Position = FPSCamera.Position = new Vector3(X, Y, Z);
		}

		public void SetPosition(Vector3 Pos) {
			if (float.IsNaN(Pos.X) || float.IsNaN(Pos.Y) || float.IsNaN(Pos.Z))
				return;

			PreviousPosition = Position;
			Position = FPSCamera.Position = Pos;
		}

		public Vector3 GetPreviousPosition() {
			return PreviousPosition;
		}

		float ClampToZero(float Num, float ClampHyst) {
			if (Num < 0 && Num > -ClampHyst)
				return 0;
			if (Num > 0 && Num < ClampHyst)
				return 0;
			return Num;
		}

		void ClampToZero(ref Vector3 Vec, float ClampHyst) {
			if (float.IsNaN(Vec.X))
				Vec.X = 0;
			if (float.IsNaN(Vec.Y))
				Vec.Y = 0;
			if (float.IsNaN(Vec.Z))
				Vec.Z = 0;
			Vec.X = ClampToZero(Vec.X, ClampHyst);
			Vec.Y = ClampToZero(Vec.Y, ClampHyst);
			Vec.Z = ClampToZero(Vec.Z, ClampHyst);
		}

		IEnumerable<Vector3> Phys_PlayerCollisionPointsImproved(Vector3 feetPos, float Radius = -1, float Height = -1) {
			if (Radius < 0)
				Radius = Player.PlayerRadius;
			if (Height < 0)
				Height = Player.PlayerHeight;
			int RadialDivs = 12;
			int HeightDivs = 4;
			for (int h = 0; h < HeightDivs; h++) {
				float heightRatio = (float)h / (HeightDivs - 1);
				float currentHeight = heightRatio * Height;
				for (int i = 0; i < RadialDivs; i++) {
					float angle = (float)i / RadialDivs * 2.0f * MathF.PI;
					float x = MathF.Cos(angle) * Radius;
					float z = MathF.Sin(angle) * Radius;
					yield return new Vector3(feetPos.X + x, feetPos.Y + currentHeight, feetPos.Z + z);
				}
			}
			for (int h = 0; h < HeightDivs; h++) {
				float heightRatio = (float)h / (HeightDivs - 1);
				float currentHeight = heightRatio * Height;
				yield return new Vector3(feetPos.X, feetPos.Y + currentHeight, feetPos.Z);
			}
			yield return new Vector3(feetPos.X, feetPos.Y + Height, feetPos.Z);
			yield return new Vector3(feetPos.X, feetPos.Y, feetPos.Z);
		}

		/// <summary>
		/// Quake-style ClipVelocity - clips velocity against a surface normal while preserving speed along the surface.
		/// This is the key to smooth wall sliding without losing momentum.
		/// </summary>
		Vector3 ClipVelocity(Vector3 velocity, Vector3 normal, float overbounce = 1.001f) {
			float backoff = Vector3.Dot(velocity, normal) * overbounce;
			Vector3 clipped = velocity - normal * backoff;

			// Prevent tiny oscillations
			if (MathF.Abs(clipped.X) < 0.001f) clipped.X = 0;
			if (MathF.Abs(clipped.Y) < 0.001f) clipped.Y = 0;
			if (MathF.Abs(clipped.Z) < 0.001f) clipped.Z = 0;

			return clipped;
		}

		/// <summary>
		/// Finds the collision normal for an AABB at the given position.
		/// Returns the primary axis of penetration.
		/// Prioritizes horizontal (wall) collisions over vertical to preserve jump velocity.
		/// </summary>
		Vector3 FindCollisionNormal(ChunkMap Map, Vector3 feetPos, Vector3 move, float playerRadius, float playerHeight) {
			// Test each axis to find which one is blocked
			Vector3 testX = new Vector3(feetPos.X + move.X, feetPos.Y, feetPos.Z);
			Vector3 testY = new Vector3(feetPos.X, feetPos.Y + move.Y, feetPos.Z);
			Vector3 testZ = new Vector3(feetPos.X, feetPos.Y, feetPos.Z + move.Z);

			bool blockedX = Map.HasBlocksInBoundsMinMax(
				testX - new Vector3(playerRadius, 0, playerRadius),
				testX + new Vector3(playerRadius, playerHeight, playerRadius));
			bool blockedY = Map.HasBlocksInBoundsMinMax(
				testY - new Vector3(playerRadius, 0, playerRadius),
				testY + new Vector3(playerRadius, playerHeight, playerRadius));
			bool blockedZ = Map.HasBlocksInBoundsMinMax(
				testZ - new Vector3(playerRadius, 0, playerRadius),
				testZ + new Vector3(playerRadius, playerHeight, playerRadius));

			// Prioritize horizontal collisions (walls) to preserve vertical velocity during jumps
			// Only return one axis at a time to prevent multi-axis clipping that kills jump velocity

			// First, handle horizontal (wall) collisions - these should NOT affect Y velocity
			if (blockedX && MathF.Abs(move.X) > MathF.Abs(move.Z) && MathF.Abs(move.X) > 0.0001f) {
				return new Vector3(-MathF.Sign(move.X), 0, 0);
			}
			if (blockedZ && MathF.Abs(move.Z) > 0.0001f) {
				return new Vector3(0, 0, -MathF.Sign(move.Z));
			}
			if (blockedX && MathF.Abs(move.X) > 0.0001f) {
				return new Vector3(-MathF.Sign(move.X), 0, 0);
			}

			// Only return Y normal if no horizontal collision and Y is actually blocked
			if (blockedY && MathF.Abs(move.Y) > 0.0001f) {
				return new Vector3(0, -MathF.Sign(move.Y), 0);
			}

			return Vector3.Zero;
		}

		private Vector3 QuakeMoveWithCollision(ChunkMap Map, Vector3 pos, Vector3 velocity, float dt, float stepHeight = 0.5f, int maxSlides = 4, bool onGround = false) {
			float playerRadius = Player.PlayerRadius;
			float playerHeight = Player.PlayerHeight;
			Vector3 feetPos = FeetPosition;
			Vector3 originalVelocity = velocity;
			Vector3 primalVelocity = velocity;
			Vector3 move = velocity * dt;
			LastWallNormal = Vector3.Zero;

			Vector3[] planes = new Vector3[5];
			int numPlanes = 0;

			// Only add ground plane if moving downward (not when jumping up)
			if (onGround && velocity.Y <= 0) {
				planes[numPlanes++] = Vector3.UnitY;
			}

			float timeLeft = dt;

			for (int slide = 0; slide < maxSlides && timeLeft > 0; slide++) {
				Vector3 endPos = feetPos + velocity * timeLeft;

				// Check if move is clear
				if (!Map.HasBlocksInBoundsMinMax(
					endPos - new Vector3(playerRadius, 0, playerRadius),
					endPos + new Vector3(playerRadius, playerHeight, playerRadius))) {
					feetPos = endPos;
					break;
				}

				// Try step up if on ground
				if (onGround && slide == 0) {
					Vector3 stepUp = feetPos + new Vector3(0, stepHeight, 0);
					Vector3 stepEnd = stepUp + velocity * timeLeft;
					if (!Map.HasBlocksInBoundsMinMax(
						stepEnd - new Vector3(playerRadius, 0, playerRadius),
						stepEnd + new Vector3(playerRadius, playerHeight, playerRadius))) {
						feetPos = stepEnd;
						break;
					}
				}

				// Find collision normal
				Vector3 normal = FindCollisionNormal(Map, feetPos, velocity * timeLeft, playerRadius, playerHeight);

				if (normal == Vector3.Zero) {
					// Stuck - try to nudge out
					break;
				}

				// Store wall normal for air control
				if (MathF.Abs(normal.Y) < 0.5f) {
					LastWallNormal = normal;
				}

				// Clip velocity against this plane
				velocity = ClipVelocity(velocity, normal);

				// Check if velocity is now moving into a previous plane
				for (int i = 0; i < numPlanes; i++) {
					if (Vector3.Dot(velocity, planes[i]) < 0) {
						// Clip against the previous plane too
						velocity = ClipVelocity(velocity, planes[i]);
					}
				}

				// Add this plane to the list
				if (numPlanes < planes.Length) {
					planes[numPlanes++] = normal;
				}

				// Calculate how much of the move we completed (approximate)
				float moveFraction = 0.1f; // Small step to avoid getting stuck
				feetPos += velocity * timeLeft * moveFraction;
				timeLeft *= (1.0f - moveFraction);

				// Check if we're not moving anymore
				if (velocity.LengthSquared() < 0.0001f) {
					break;
				}
			}

			// Update the player's velocity to the clipped version
			PlyVelocity = velocity;

			return feetPos + new Vector3(0, Player.PlayerEyeOffset, 0);
		}

		// --- Quake-style acceleration (ground) ---
		void Accelerate(ref Vector3 velocity, Vector3 wishdir, float wishspeed, float accel, float dt) {
			float currentspeed = Vector3.Dot(velocity, wishdir);
			float addspeed = wishspeed - currentspeed;
			if (addspeed <= 0)
				return;
			float accelspeed = accel * dt * wishspeed;
			if (accelspeed > addspeed)
				accelspeed = addspeed;
			velocity += accelspeed * wishdir;
		}

		// --- Quake-style air acceleration (key for strafe jumping) ---
		void AirAccelerate(ref Vector3 velocity, Vector3 wishdir, float wishspeed, float accel, float dt) {
			// Cap wishspeed for air acceleration - this is what makes strafe jumping work
			float wishspd = wishspeed;
			if (wishspd > 0.7f) // Quake uses 30 units, but we're scaled differently
				wishspd = 0.7f;

			float currentspeed = Vector3.Dot(velocity, wishdir);
			float addspeed = wishspd - currentspeed;
			if (addspeed <= 0)
				return;
			float accelspeed = accel * dt * wishspeed;
			if (accelspeed > addspeed)
				accelspeed = addspeed;
			velocity += accelspeed * wishdir;
		}

		// --- Quake-style friction ---
		void ApplyFriction(ref Vector3 velocity, float friction, float dt, bool onGround) {
			if (!onGround)
				return;

			float speed = MathF.Sqrt(velocity.X * velocity.X + velocity.Z * velocity.Z);
			if (speed < 0.1f) {
				velocity.X = 0;
				velocity.Z = 0;
				return;
			}

			float drop = speed * friction * dt;
			float newspeed = speed - drop;
			if (newspeed < 0)
				newspeed = 0;
			newspeed /= speed;

			velocity.X *= newspeed;
			velocity.Z *= newspeed;
		}

		void NoclipMove(PhysData PhysicsData, float Dt, InputMgr InMgr) {
			Vector3 move = Vector3.Zero;
			Vector3 fwd = GetForward();
			Vector3 lft = GetLeft();
			Vector3 up = GetUp();

			if (InMgr.IsInputDown(InputKey.W))
				move += fwd;

			if (InMgr.IsInputDown(InputKey.S))
				move -= fwd;

			if (InMgr.IsInputDown(InputKey.A))
				move += lft;

			if (InMgr.IsInputDown(InputKey.D))
				move -= lft;

			if (InMgr.IsInputDown(InputKey.Space))
				move += up;

			if (InMgr.IsInputDown(InputKey.Shift))
				move -= up;

			if (move != Vector3.Zero) {
				move = Vector3.Normalize(move) * PhysicsData.NoClipMoveSpeed * Dt;
				SetPosition(Position + move);
			}
		}

		public void UpdatePhysics(ChunkMap Map, PhysData PhysicsData, float Dt, InputMgr InMgr) {
			const float GroundHitBelowFeet = -0.075f;
			float playerHeight = Player.PlayerHeight;
			float playerRadius = Player.PlayerRadius;

			if (NoClip) {
				NoclipMove(PhysicsData, Dt, InMgr);
				return;
			}

			if (!Utils.HasRecord())
				Utils.BeginRaycastRecord();

			ClampToZero(ref PlyVelocity, PhysicsData.ClampHyst);
			Vector3 feetPos = FeetPosition;

			Vector3[] groundCheckPoints = new Vector3[] {
				new Vector3(feetPos.X - playerRadius, feetPos.Y + PhysicsData.GroundEpsilon, feetPos.Z - playerRadius),
				new Vector3(feetPos.X + playerRadius, feetPos.Y + PhysicsData.GroundEpsilon, feetPos.Z - playerRadius),
				new Vector3(feetPos.X - playerRadius, feetPos.Y + PhysicsData.GroundEpsilon, feetPos.Z + playerRadius),
				new Vector3(feetPos.X + playerRadius, feetPos.Y + PhysicsData.GroundEpsilon, feetPos.Z + playerRadius),
				feetPos + new Vector3(0, PhysicsData.GroundEpsilon, 0)
			};

			bool OnGround = false;
			Vector3 HitFloor = Vector3.Zero;

			foreach (var pt in groundCheckPoints) {
				Vector3 localFace;
				Vector3 hit = Map.RaycastPos(pt, PhysicsData.GroundCheckDist, new Vector3(0, -1f, 0), out localFace);

				if (hit != Vector3.Zero && localFace.Y > 0.99f && Math.Abs(localFace.X) < 0.05f && Math.Abs(localFace.Z) < 0.05f && PlyVelocity.Y <= 0 && hit.Y < feetPos.Y + GroundHitBelowFeet) {
					OnGround = true;
					HitFloor = hit;
					break;
				}
			}
			if (!OnGround) {
				foreach (var pt in groundCheckPoints) {
					Vector3 TestPoint = pt + PlyVelocity * Dt;

					if (Map.Collide(TestPoint, new Vector3(0, -1, 0), out Vector3 PicNorm)) {
						if (PicNorm.Y > 0.99f && Math.Abs(PicNorm.X) < 0.05f && Math.Abs(PicNorm.Z) < 0.05f && PlyVelocity.Y <= 0 && TestPoint.Y < feetPos.Y + GroundHitBelowFeet) {
							OnGround = true;
							HitFloor = TestPoint;
							break;
						}
					}
				}
			}
			if (OnGround) {
				GroundGraceTimer = 0.1f;
			} else {
				GroundGraceTimer -= Dt;
				if (GroundGraceTimer < 0)
					GroundGraceTimer = 0;
			}

			bool OnGroundGrace = GroundGraceTimer > 0f;
			Vector3 wishdir = Vector3.Zero;

			// Flatten forward direction to horizontal plane for movement (prevents climbing walls by looking up)
			Vector3 fwd2 = GetForward();
			fwd2.Y = 0;
			if (fwd2.LengthSquared() > 0.0001f)
				fwd2 = Vector3.Normalize(fwd2);
			else
				fwd2 = Vector3.UnitX; // Fallback when looking straight up/down

			// Left is already horizontal (perpendicular to up vector)
			Vector3 lft2 = GetLeft();
			lft2.Y = 0;
			if (lft2.LengthSquared() > 0.0001f)
				lft2 = Vector3.Normalize(lft2);

			if (InMgr.IsInputDown(InputKey.W))
				wishdir += fwd2;

			if (InMgr.IsInputDown(InputKey.S))
				wishdir -= fwd2;

			if (InMgr.IsInputDown(InputKey.A))
				wishdir += lft2;

			if (InMgr.IsInputDown(InputKey.D))
				wishdir -= lft2;

			if (wishdir != Vector3.Zero)
				wishdir = Vector3.Normalize(wishdir);

			bool ledgeSafety = OnGroundGrace && InMgr.IsInputDown(InputKey.Shift);
			if (ledgeSafety && wishdir != Vector3.Zero) {
				float innerRadius = 0.4f;
				var points = Phys_PlayerCollisionPointsImproved(feetPos, innerRadius, Player.PlayerHeight).ToArray();
				float minY = points.Min(p => p.Y);
				var feetPoints = points.Where(p => Math.Abs(p.Y - minY) < 0.01f).ToArray();

				List<Vector3> supportedPoints = new();
				foreach (var pt in feetPoints) {
					Vector3 groundCheck = pt + new Vector3(0, -0.15f, 0);
					if (Map.GetBlock((int)MathF.Floor(groundCheck.X), (int)MathF.Floor(groundCheck.Y), (int)MathF.Floor(groundCheck.Z)) != BlockType.None) {
						supportedPoints.Add(pt);
					}
				}

				if (supportedPoints.Count == 0) {
					PlyVelocity.X = 0;
					PlyVelocity.Z = 0;
					wishdir = Vector3.Zero;
				} else {
					bool allow = false;

					foreach (var spt in supportedPoints) {
						Vector3 toSupport = Vector3.Normalize(spt - feetPos);
						if (Vector3.Dot(wishdir, toSupport) > 0) {
							allow = true;
							break;
						}
					}

					if (!allow) {
						PlyVelocity.X = 0;
						PlyVelocity.Z = 0;
						wishdir = Vector3.Zero;
					}
				}
			}

			float VelLen = PlyVelocity.Length();

			if (OnGroundGrace) {
				if (!WasLastLegsOnFloor) {
					WasLastLegsOnFloor = true;
					this.PhysicsHit(Position, VelLen, false, true, false, false);
				} else if (VelLen >= (PhysicsData.MaxGroundSpeed / 2)) {
					this.PhysicsHit(HitFloor, VelLen, false, true, true, false);
				}
			} else {
				WasLastLegsOnFloor = false;
			}

			// --- Apply friction BEFORE acceleration (Quake order) ---
			ApplyFriction(ref PlyVelocity, PhysicsData.GroundFriction, Dt, OnGroundGrace);

			// --- Update head bump cooldown ---
			if (HeadBumpCooldown > 0)
				HeadBumpCooldown -= Dt;

			// --- Jumping (before movement for bunny hop) ---
			bool canJump = HeadBumpCooldown <= 0 && JumpCounter.ElapsedMilliseconds > 50;
			if (InMgr.IsInputDown(InputKey.Space) && OnGroundGrace && canJump) {
				JumpCounter.Restart();
				PlyVelocity.Y = PhysicsData.JumpImpulse;
				this.PhysicsHit(HitFloor, VelLen, false, false, false, true);
				GroundGraceTimer = 0;
				OnGroundGrace = false; // Immediately in air after jump
			}

			// --- Apply acceleration ---
			if (wishdir != Vector3.Zero) {
				float wishspeed = InMgr.IsInputDown(InputKey.Shift) ? PhysicsData.MaxWalkSpeed : PhysicsData.MaxGroundSpeed;

				if (OnGroundGrace) {
					// Ground acceleration - standard Quake ground move
					Accelerate(ref PlyVelocity, wishdir, wishspeed, PhysicsData.GroundAccel, Dt);
				} else {
					// Air acceleration - key for strafe jumping
					// Wall sliding adjustment
					Vector3 accelDir = wishdir;
					if (LastWallNormal != Vector3.Zero) {
						accelDir -= Vector3.Dot(accelDir, LastWallNormal) * LastWallNormal;
						if (accelDir.LengthSquared() > 1e-4f)
							accelDir = Vector3.Normalize(accelDir);
						else
							accelDir = Vector3.Zero;
					}

					if (accelDir != Vector3.Zero) {
						AirAccelerate(ref PlyVelocity, accelDir, wishspeed, PhysicsData.AirAccel, Dt);
					}
				}
			}

			// --- Gravity ---
			if (!OnGroundGrace) {
				PlyVelocity.Y -= PhysicsData.Gravity * Dt;
			} else if (PlyVelocity.Y < 0) {
				PlyVelocity.Y = 0;
			}

			// --- Cap ground speed only (NOT air speed - allows bunny hopping) ---
			if (OnGroundGrace) {
				float maxSpeed = InMgr.IsInputDown(InputKey.Shift) ? PhysicsData.MaxWalkSpeed : PhysicsData.MaxGroundSpeed;
				float horizSpeed = MathF.Sqrt(PlyVelocity.X * PlyVelocity.X + PlyVelocity.Z * PlyVelocity.Z);
				if (horizSpeed > maxSpeed) {
					float scale = maxSpeed / horizSpeed;
					PlyVelocity.X *= scale;
					PlyVelocity.Z *= scale;
				}
			}

			// --- Move and collide ---
			float stepHeight = OnGroundGrace ? 0.5f : 0.0f;
			float preMovePlyVelocityY = PlyVelocity.Y; // Save Y velocity before collision may clip it
			Vector3 newPos = QuakeMoveWithCollision(Map, Position, PlyVelocity, Dt, stepHeight, 4, OnGroundGrace);

			if (newPos != Position) {
				SetPosition(newPos);
			}
			// Note: PlyVelocity is now updated inside QuakeMoveWithCollision via ClipVelocity

			// --- Head collision detection ---
			// If player was moving upward and velocity was clipped to 0 or less, they hit a ceiling
			if (preMovePlyVelocityY > 0.1f && PlyVelocity.Y <= 0) {
				// Apply 0.5s jump cooldown if head was hit within 0.6s of jumping
				if (JumpCounter.ElapsedMilliseconds < 600) {
					HeadBumpCooldown = 0.5f;
				}
			}

			Utils.EndRaycastRecord();
		}

		public void Tick(InputMgr InMgr) {
			// ViewMdl.SetRotationMode(InMgr.IsInputDown(InputKey.Click_Right) ? ViewModelRotationMode.GunIronsight : ViewModelRotationMode.Gun);
			ActiveSelection?.Tick(ViewMdl, InMgr);
			FPSCamera.Update(CursorDisabled, ref Cam);

			// Use InputMgr for F1
			if (InMgr.IsInputPressed(InputKey.F1))
				ToggleMouse();

			// Keep OnKeyFuncs using Raylib for now (as they are mapped to KeyboardKey)
			foreach (var KV in OnKeyFuncs) {
				if (InMgr.IsInputPressed(KV.Key))
					KV.Value(new OnKeyPressedEventArg(KV.Key));
			}

			Position = FPSCamera.Position;

			ViewMdl.Update(this);
		}

		GUIItemBox Box_Health;
		GUILabel InfoLbl;
		GUIInventory Inventory;

		InventoryItem ActiveSelection;

		/// <summary>
		/// Gets the currently selected inventory item, or null if none selected.
		/// </summary>
		public InventoryItem GetActiveItem() => ActiveSelection;

		public void RecalcGUI(GameWindow Window) {
			//Box_Health.Pos = new Vector2(64, Window.Height - 128);

			//InfoLbl.Pos = new Vector2(16, 40);
			//InfoLbl.Size = new Vector2(300, 250);

			//Inventory.Pos = GUI.WindowScale(new Vector2(0.5f, 0.9f)) - new Vector2(Inventory.Size.X / 2, 0);
			GUI.Tick();
		}

		public void InitGUI(GameWindow Window) {
			Box_Health = new GUIItemBox(GUI, null);
			Box_Health.Text = "100";
			Box_Health.SetIcon(ResMgr.GetTexture("items/heart_full.png"), 3);
			Box_Health.FlexNode.nodeStyle.Set("position: absolute; left: 100; bottom: 100; width: 64; height: 64;");
			GUI.AddElement(Box_Health);

			InfoLbl = new GUILabel(GUI, null);
			InfoLbl.Clear();
			InfoLbl.WriteLine("Hello World!");
			InfoLbl.FlexNode.nodeStyle.Set("position: absolute; width: 300; height: 250; left: 20; top: 40;");
			GUI.AddElement(InfoLbl);

			Inventory = new GUIInventory(GUI, null);
			Inventory.FlexNode.nodeStyle.Set("width: 40%; height: 64; left: 30%; top: 87%; justify-content: center;");
			GUI.AddElement(Inventory);

			Inventory.OnActiveSelectionChanged = (E) => {
				if (ActiveSelection != null) {
					ActiveSelection.OnDeselected(ViewMdl);
					ActiveSelection = null;
				}

				ActiveSelection = E.ItmBox.Item;

				if (ActiveSelection != null) {
					ActiveSelection.OnSelected(ViewMdl);
				}
			};

			int ItmIdx = 0;
			SetInvItem(Inventory, ItmIdx++, new WeaponGun(this, "Gun"));
			SetInvItem(Inventory, ItmIdx++, new WeaponPicker(this, "Hammer"));
			SetInvItem(Inventory, ItmIdx++, new Weapon(this, BlockType.Dirt).SetCount(64));
			SetInvItem(Inventory, ItmIdx++, new Weapon(this, BlockType.Stone).SetCount(64));
			SetInvItem(Inventory, ItmIdx++, new Weapon(this, BlockType.Plank).SetCount(64));
			SetInvItem(Inventory, ItmIdx++, new Weapon(this, BlockType.Bricks).SetCount(10));
			SetInvItem(Inventory, ItmIdx++, new Weapon(this, BlockType.StoneBrick).SetCount(64));
			SetInvItem(Inventory, ItmIdx++, new Weapon(this, BlockType.Glowstone).SetCount(64));
			SetInvItem(Inventory, ItmIdx++, new Weapon(this, BlockType.Glass).SetCount(64));
			Inventory.SetSelectedIndex(0);

			GUI.Tick();
		}

		public void UpdateGUI() {
			InfoLbl.Enabled = false;

			if (Program.DebugMode) {
				InfoLbl.Enabled = true;
				InfoLbl.Clear();
				InfoLbl.WriteLine("Pos: {0:0.00}, {1:0.00}, {2:0.00}", MathF.Round(Position.X, 2), MathF.Round(Position.Y, 2), MathF.Round(Position.Z, 2));
				InfoLbl.WriteLine("Vel: {0:0.000}", MathF.Round(GetVelocity().Length(), 3));
				InfoLbl.WriteLine("NoClip (C): {0}", NoClip ? "ON" : "OFF");
				InfoLbl.WriteLine("OnGround: {0}", GetWasLastLegsOnFloor() ? "YES" : "NO");
				InfoLbl.WriteLine("ChunkDraws: {0}", Program.ChunkDrawCalls.ToString());

				Program.GameState.Particle.GetStats(out int OnScreen, out int Drawn, out int Max);
				InfoLbl.WriteLine("Particles: {0}/{1}/{2}", OnScreen, Drawn, Max);
			}
		}

		void SetInvItem(GUIInventory Inventory, int Idx, InventoryItem InvItem) {
			GUIItemBox Itm = Inventory.GetItem(Idx);
			Itm.UpdateTextFromItem = true;
			Itm.SetItem(Inventory, InvItem);
		}

		public void TickGUI(InputMgr InMgr, ChunkMap Map) {
			bool Left = InMgr.IsInputPressed(InputKey.Click_Left);
			bool Right = InMgr.IsInputPressed(InputKey.Click_Right);
			bool Middle = InMgr.IsInputPressed(InputKey.Click_Middle);
			float Wheel = InMgr.GetMouseWheel();
			const float MaxLen = 20;

			if (Wheel >= 1)
				Inventory.SelectNext();
			else if (Wheel <= -1)
				Inventory.SelectPrevious();
			if ((Left || Right || Middle) && CursorDisabled) {
				if (ActiveSelection != null) {
					Vector3 Start = Position;
					Vector3 Dir = GetForward();
					InventoryClickEventArgs E = new InventoryClickEventArgs(Map, Start, Dir, MaxLen);

					if (Left)
						ActiveSelection.OnLeftClick(E);

					if (Right)
						ActiveSelection.OnRightClick(E);

					if (Middle)
						ActiveSelection.OnMiddleClick(E);
				}
			}

			if (!CursorDisabled) {
				GUI.Tick();
				//Inventory.Update();
			} else {
				if (InMgr.IsInputPressed(InputKey.Q))
					Inventory.SelectPrevious();

				if (InMgr.IsInputPressed(InputKey.E)) {
					Vector3 Start = Position;
					Vector3 End = Map.RaycastPos(Start, 1.5f, GetForward(), out Vector3 Face);

					if (Face.Y == 1)
						End.Y -= 0.001f;

					PlacedBlock Blk = Map.GetPlacedBlock((int)End.X, (int)End.Y, (int)End.Z, out Chunk Chk);

					if (Blk.Type == BlockType.CraftingTable) {
						Console.WriteLine($"Craft! {Face}, ({End.X - Math.Floor(End.X)}, {End.Z - Math.Floor(End.Z)})");
						return;
					}
					Inventory.SelectNext();
				}
				Inventory.Update();
			}
		}

		public void Draw(float TimeAlpha, ref GameFrameInfo LastFrame, ref GameFrameInfo CurFame) {
			if (LocalPlayer) {

				RenderTexture2D RT = GUI.Window.ViewmodelRT;
				Raylib.BeginTextureMode(RT);
				{
					Raylib.ClearBackground(new Color(0, 0, 0, 0));

					Raylib.BeginMode3D(RenderCam); // Use interpolated render camera
					{
						Shader DefaultShader = ResMgr.GetShader("default");
						Raylib.BeginShaderMode(DefaultShader);
						ViewMdl.DrawViewModel(this, TimeAlpha, ref LastFrame, ref CurFame);
						Raylib.EndShaderMode();
					}
					Raylib.EndMode3D();

				}
				Raylib.EndTextureMode();
				Raylib.BeginTextureMode(GUI.Window.WindowG.Target);

				Rectangle Src = new Rectangle(0, 0, RT.Texture.Width, -RT.Texture.Height);
				Rectangle Dst = new Rectangle(0, 0, RT.Texture.Width, RT.Texture.Height);
				Raylib.DrawTexturePro(RT.Texture, Src, Dst, Vector2.Zero, 0, Color.White);
			}

			if (!DEBUG_PLAYER && LocalPlayer)
				return;
		}

		public void AddOnKeyPressed(InputKey K, Action<OnKeyPressedEventArg> Act) {
			OnKeyFuncs.Add(K, Act);
		}

		public void Write(System.IO.BinaryWriter writer) {
			// Write position
			writer.Write(Position.X);
			writer.Write(Position.Y);
			writer.Write(Position.Z);

			Vector3 CamAngle = GetCamAngle();
			// Write camera angle
			writer.Write(CamAngle.X);
			writer.Write(CamAngle.Y);
			writer.Write(CamAngle.Z);
			// Write camera (just position and target for now)
			writer.Write(Cam.Position.X);
			writer.Write(Cam.Position.Y);
			writer.Write(Cam.Position.Z);
			writer.Write(Cam.Target.X);
			writer.Write(Cam.Target.Y);
			writer.Write(Cam.Target.Z);
			// Write previous position
			writer.Write(PreviousPosition.X);
			writer.Write(PreviousPosition.Y);
			writer.Write(PreviousPosition.Z);
			// Write cursor state
			writer.Write(CursorDisabled);
		}

		public void Read(System.IO.BinaryReader reader) {
			// Read position
			SetPosition(new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()));
			// Read camera angle
			Vector3 CamAngle = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
			SetCamAngle(CamAngle);

			// Read camera
			Cam.Position.X = reader.ReadSingle();
			Cam.Position.Y = reader.ReadSingle();
			Cam.Position.Z = reader.ReadSingle();
			Cam.Target.X = reader.ReadSingle();
			Cam.Target.Y = reader.ReadSingle();
			Cam.Target.Z = reader.ReadSingle();
			// Read previous position
			PreviousPosition.X = reader.ReadSingle();
			PreviousPosition.Y = reader.ReadSingle();
			PreviousPosition.Z = reader.ReadSingle();
			// Read cursor state
			CursorDisabled = reader.ReadBoolean();
		}

		Vector3 Fwd;
		Vector3 Left;
		Vector3 Up;

		public Vector3 GetForward() => Fwd;
		public Vector3 GetLeft() => Left;
		public Vector3 GetUp() => Up;

		public void SetCamAngle(Vector3 CamAngle) {
			FPSCamera.CamAngle = CamAngle;
		}

		public Vector3 GetCamAngle() {
			return FPSCamera.CamAngle;
		}

		public void UpdateFPSCamera(ref GameFrameInfo FInfo) {
			//Cam = FInfo.Cam;
			//FPSCamera.Position = FInfo.Pos;

			Fwd = FPSCamera.GetForward();
			Left = FPSCamera.GetLeft();
			Up = FPSCamera.GetUp();
		}


		// Add accessors in Player for velocity and ground state for GUI
		public Vector3 GetVelocity() => PlyVelocity;

		public bool GetWasLastLegsOnFloor() => WasLastLegsOnFloor;

		// Add back the PhysicsHit method (was present in Player, but was return; previously)
		public void PhysicsHit(Vector3 Pos, float Force, bool Side, bool Feet, bool Walk, bool Jump) {
			Vector3 Fwd = FPSCamera.GetForward();
			if (Walk) {
				if (LegTimer.ElapsedMilliseconds > LastWalkSound + 350) {
					LastWalkSound = LegTimer.ElapsedMilliseconds;
					Snd.PlayCombo("walk", FPSCamera.Position, Fwd, Pos);
				}
			} else if (Jump) {
				if (LegTimer.ElapsedMilliseconds > LastJumpSound + 350) {
					LastJumpSound = LegTimer.ElapsedMilliseconds;
					Snd.PlayCombo("jump", FPSCamera.Position, Fwd, Pos);
				}
			} else if (Feet && !Side) {
				if (LegTimer.ElapsedMilliseconds > LastCrashSound + 350) {
					LastCrashSound = LegTimer.ElapsedMilliseconds;
					if (Force < 4) {
						Snd.PlayCombo("crash1", FPSCamera.Position, Fwd, Pos);
					} else if (Force >= 4 && Force < 8) {
						Snd.PlayCombo("crash2", FPSCamera.Position, Fwd, Pos);
					} else if (Force >= 8) {
						Snd.PlayCombo("crash3", FPSCamera.Position, Fwd, Pos);
					}
				}
			} else {
				// Console.WriteLine("Sid: {0}, Ft: {1}, F: {2}, W: {3}", Side, Feet, Force, Walk);
			}
		}
	}
}
