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
	public unsafe class BoneInformation {
		public string Name;
		public int ID;

		public Transform* Transform;
		public Matrix4x4 LocalTransform;

		public BoneInformation Parent;
		public List<BoneInformation> Children;

		public BoneInformation(string Name, int ID) {
			Children = new List<BoneInformation>();
			LocalTransform = Matrix4x4.Identity;

			this.Name = Name;
			this.ID = ID;
		}

		public void AddChild(BoneInformation Bone) {
			Bone.Parent = this;
			Children.Add(Bone);
		}

		public Matrix4x4 GetTransform() {
			if (Transform == null)
				return Matrix4x4.Identity;

			return Matrix4x4.CreateFromQuaternion(Transform->Rotation) * Matrix4x4.CreateScale(Transform->Scale) * Matrix4x4.CreateTranslation(Transform->Translation);
		}

		public Matrix4x4 GetLocalTransform() {
			if (Parent == null)
				return GetTransform();

			Matrix4x4 ParentWorld = Parent.GetTransform();
			Matrix4x4.Invert(ParentWorld, out Matrix4x4 ParentWorldInv);
			return ParentWorldInv * GetTransform();
		}

		public Matrix4x4 CalcWorldTransform() {
			if (Parent == null)
				return GetTransform();

			return Parent.CalcWorldTransform() * GetLocalTransform();
		}

		public void UpdateTransforms() {
			if (Transform == null)
				return;

			Matrix4x4.Decompose(CalcWorldTransform(), out Transform->Scale, out Transform->Rotation, out Transform->Translation);
		}

		public void RecalcTransforms() {
			UpdateTransforms();

			foreach (var C in Children)
				C.RecalcTransforms();
		}

		public override string ToString() {
			return string.Format("{0} - {1}", ID, Name);
		}

		public static BoneInformation FindBone(BoneInformation Root, int ID) {
			if (Root.ID == ID)
				return Root;

			foreach (var C in Root.Children) {
				BoneInformation B = FindBone(C, ID);

				if (B != null)
					return B;
			}

			return null;
		}

		public static BoneInformation FindBoneByName(BoneInformation Root, string Name) {
			if (Root.Name == Name)
				return Root;

			foreach (var C in Root.Children) {
				BoneInformation B = FindBoneByName(C, Name);

				if (B != null)
					return B;
			}

			return null;
		}
	}

	delegate void OnKeyPressedFunc();

	unsafe class Player {
		const bool DEBUG_PLAYER = true;

		public Camera3D Cam = new Camera3D(Vector3.Zero, Vector3.UnitX, Vector3.UnitY, 90, CameraProjection.Perspective);
		AnimatedEntity PlayerEntity;
		EntityAnimation CurAnim;
		GUIManager GUI;

		bool MoveFd;
		bool MoveBk;
		bool MoveLt;
		bool MoveRt;

		bool NoClip;

		Dictionary<KeyboardKey, Action> OnKeyFuncs = new Dictionary<KeyboardKey, Action>();

		Stopwatch LegTimer = Stopwatch.StartNew();
		long LastWalkSound = 0;
		long LastJumpSound = 0;
		long LastCrashSound = 0;

		Vector3 PreviousPosition;
		bool LocalPlayer;
		SoundMgr Snd;
		BlockType PlayerSelectedBlockType = BlockType.Dirt;

		public const float PlayerHeight = 1.7f;
		public const float PlayerEyeOffset = 1.6f;
		public const float PlayerRadius = 0.4f;

		public Vector3 Position;
		public Matrix4x4 Rotation;
		public Matrix4x4 UpperBodyRotation;
		public bool CursorDisabled = false;


		public Vector3 FeetPosition => Position - new Vector3(0, PlayerEyeOffset, 0);

		// --- Player movement/physics fields ---
		Vector3 PlyVelocity = Vector3.Zero;
		bool WasLastLegsOnFloor = false;
		float GroundGraceTimer = 0f; // Coyote time for ground detection
		Vector3 LastWallNormal = Vector3.Zero;
		Stopwatch JumpCounter = Stopwatch.StartNew();

		public Player(GUIManager GUI, string ModelName, bool LocalPlayer, SoundMgr Snd) {
			this.GUI = GUI;
			this.Snd = Snd;
			this.LocalPlayer = LocalPlayer;
			PlayerEntity = Entities.Load(ModelName);

			Position = Vector3.Zero;
			Rotation = Matrix4x4.Identity;
			UpperBodyRotation = Matrix4x4.Identity;


			// UPDATE: ?
			//Raylib.SetCameraMode(Cam, CameraMode.CAMERA_CUSTOM);
			ToggleMouse();




			BoneInformation BBB = BoneInformation.FindBoneByName(PlayerEntity.GetBones(), "Head");
			BBB.LocalTransform = Matrix4x4.CreateFromYawPitchRoll(0, 0, 90);
			BBB.RecalcTransforms();

			ToggleMouse(false);
		}

		public void Init(ChunkMap Map) {
			Stopwatch SWatch = Stopwatch.StartNew();

			AddOnKeyPressed(KeyboardKey.F2, () => {
				Console.WriteLine("Compute light!");
				SWatch.Restart();
				Map.ComputeLighting();
				SWatch.Stop();
				Console.Title = $"> {SWatch.ElapsedMilliseconds / 1000.0f} s";
			});

			AddOnKeyPressed(KeyboardKey.F3, () => { Program.DebugMode = !Program.DebugMode; });

			AddOnKeyPressed(KeyboardKey.F4, () => { Console.WriteLine("Clearing records"); Utils.ClearRaycastRecord(); });

			AddOnKeyPressed(KeyboardKey.C, () => {
				NoClip = !NoClip;
				Console.WriteLine($"No-clip mode: {(NoClip ? "ON" : "OFF")}");
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

		private Vector3 QuakeMoveWithCollision(ChunkMap Map, Vector3 pos, Vector3 velocity, float dt, float stepHeight = 0.5f, int maxSlides = 4, bool onGround = false) {
			float playerRadius = Player.PlayerRadius;
			float playerHeight = Player.PlayerHeight;
			Vector3 feetPos = FeetPosition;
			Vector3 move = velocity * dt;
			LastWallNormal = Vector3.Zero; // Reset before each move
			for (int slide = 0; slide < maxSlides; slide++) {
				Vector3 tryPos = feetPos + move;
				if (!HasBlocksInBounds(Map, tryPos - new Vector3(playerRadius, 0, playerRadius), tryPos + new Vector3(playerRadius, playerHeight, playerRadius))) {
					feetPos = tryPos;
					break;
				}
				Vector3 stepUp = feetPos + new Vector3(0, stepHeight, 0);
				Vector3 stepTry = stepUp + move;
				if (!HasBlocksInBounds(Map, stepTry - new Vector3(playerRadius, 0, playerRadius), stepTry + new Vector3(playerRadius, playerHeight, playerRadius))) {
					feetPos = stepTry;
					break;
				}
				Vector3 tryX = new Vector3(feetPos.X + move.X, feetPos.Y, feetPos.Z);
				if (!HasBlocksInBounds(Map, tryX - new Vector3(playerRadius, 0, playerRadius), tryX + new Vector3(playerRadius, playerHeight, playerRadius))) {
					feetPos = tryX;
					move.Z = 0;
					PlyVelocity = Utils.ProjectOnPlane(PlyVelocity, new Vector3(1, 0, 0), 1e-5f);
					LastWallNormal = new Vector3(MathF.Sign(move.X), 0, 0); // Store wall normal
					continue;
				}
				Vector3 tryZ = new Vector3(feetPos.X, feetPos.Y, feetPos.Z + move.Z);
				if (!HasBlocksInBounds(Map, tryZ - new Vector3(playerRadius, 0, playerRadius), tryZ + new Vector3(playerRadius, playerHeight, playerRadius))) {
					feetPos = tryZ;
					move.X = 0;
					PlyVelocity = Utils.ProjectOnPlane(PlyVelocity, new Vector3(0, 0, 1), 1e-5f);
					LastWallNormal = new Vector3(0, 0, MathF.Sign(move.Z)); // Store wall normal
					continue;
				}
				break;
			}
			return feetPos + new Vector3(0, Player.PlayerEyeOffset, 0);
		}

		bool HasBlocksInBounds(ChunkMap Map, Vector3 min, Vector3 max) {
			for (int x = (int)min.X; x <= (int)max.X; x++)
				for (int y = (int)min.Y; y <= (int)max.Y; y++)
					for (int z = (int)min.Z; z <= (int)max.Z; z++)
						if (Map.GetBlock(x, y, z) != BlockType.None)
							return true;
			return false;
		}

		public void UpdatePhysics(ChunkMap Map, PhysData PhysicsData, float Dt) {
			const float GroundHitBelowFeet = -0.075f;
			float playerHeight = Player.PlayerHeight;
			float playerRadius = Player.PlayerRadius;

			if (NoClip) {
				Vector3 move = Vector3.Zero;
				Vector3 fwd = GetForward();
				Vector3 lft = GetLeft();
				Vector3 up = GetUp();
				if (Raylib.IsKeyDown(KeyboardKey.W))
					move += fwd;
				if (Raylib.IsKeyDown(KeyboardKey.S))
					move -= fwd;
				if (Raylib.IsKeyDown(KeyboardKey.A))
					move += lft;
				if (Raylib.IsKeyDown(KeyboardKey.D))
					move -= lft;
				if (Raylib.IsKeyDown(KeyboardKey.Space))
					move += up;
				if (Raylib.IsKeyDown(KeyboardKey.LeftShift))
					move -= up;
				if (move != Vector3.Zero) {
					move = Vector3.Normalize(move) * PhysicsData.NoClipMoveSpeed * Dt;
					SetPosition(Position + move);
				}
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
			Vector3 fwd2 = GetForward();
			fwd2.Y = 0;
			fwd2 = Vector3.Normalize(fwd2);
			Vector3 lft2 = GetLeft();
			if (Raylib.IsKeyDown(KeyboardKey.W))
				wishdir += fwd2;
			if (Raylib.IsKeyDown(KeyboardKey.S))
				wishdir -= fwd2;
			if (Raylib.IsKeyDown(KeyboardKey.A))
				wishdir += lft2;
			if (Raylib.IsKeyDown(KeyboardKey.D))
				wishdir -= lft2;
			if (wishdir != Vector3.Zero)
				wishdir = Vector3.Normalize(wishdir);
			bool ledgeSafety = OnGroundGrace && Raylib.IsKeyDown(KeyboardKey.LeftShift);
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
			if (Raylib.IsKeyDown(KeyboardKey.Space) && OnGroundGrace && JumpCounter.ElapsedMilliseconds > 50) {
				JumpCounter.Restart();
				PlyVelocity.Y = PhysicsData.JumpImpulse;
				this.PhysicsHit(HitFloor, VelLen, false, false, false, true);
				GroundGraceTimer = 0;
			}
			if (OnGroundGrace) {
				Vector2 velH = new Vector2(PlyVelocity.X, PlyVelocity.Z);
				float speed = velH.Length();
				if (speed > 0) {
					float drop = speed * PhysicsData.GroundFriction * Dt;
					float newSpeed = MathF.Max(speed - drop, 0);
					if (newSpeed != speed) {
						newSpeed /= speed;
						PlyVelocity.X *= newSpeed;
						PlyVelocity.Z *= newSpeed;
					}
				}
			} else {
				PlyVelocity.X *= (1.0f - PhysicsData.AirFriction * Dt);
				PlyVelocity.Z *= (1.0f - PhysicsData.AirFriction * Dt);
			}
			if (!OnGroundGrace) {
				PlyVelocity.Y -= PhysicsData.Gravity * Dt;
			} else if (PlyVelocity.Y < 0) {
				PlyVelocity.Y = 0;
			}
			float stepHeight = OnGroundGrace ? 0.5f : 0.0f;
			Vector3 newPos = QuakeMoveWithCollision(Map, Position, PlyVelocity, Dt, stepHeight, 4, OnGroundGrace);
			if (newPos != Position)
				SetPosition(newPos);
			else
				PlyVelocity = Vector3.Zero;
			if (wishdir != Vector3.Zero) {
				Vector3 accelDir = wishdir;
				if (!OnGroundGrace && LastWallNormal != Vector3.Zero) {
					accelDir -= Vector3.Dot(accelDir, LastWallNormal) * LastWallNormal;
					if (accelDir.LengthSquared() > 1e-4f)
						accelDir = Vector3.Normalize(accelDir);
					else
						accelDir = Vector3.Zero;
				}
				bool canApplyAirAccel = OnGroundGrace || accelDir != Vector3.Zero;
				if (canApplyAirAccel) {
					float curSpeed = PlyVelocity.X * accelDir.X + PlyVelocity.Z * accelDir.Z;
					float addSpeed, accel;
					float maxGroundSpeed = Raylib.IsKeyDown(KeyboardKey.LeftShift) ? PhysicsData.MaxWalkSpeed : PhysicsData.MaxGroundSpeed;
					if (OnGroundGrace) {
						addSpeed = maxGroundSpeed - curSpeed;
						accel = PhysicsData.GroundAccel;
					} else {
						addSpeed = PhysicsData.MaxAirSpeed - curSpeed;
						accel = PhysicsData.AirAccel;
					}
					if (addSpeed > 0) {
						float accelSpeed = accel * Dt * maxGroundSpeed;
						if (accelSpeed > addSpeed)
							accelSpeed = addSpeed;
						PlyVelocity.X += accelSpeed * accelDir.X;
						PlyVelocity.Z += accelSpeed * accelDir.Z;
					}
				}
			}
			if (PlyVelocity.Y > 0) {
				float headEpsilon = 0.02f;
				Vector3 headPos = feetPos + new Vector3(0, playerHeight - headEpsilon, 0);
				Vector3[] headCheckPoints = new Vector3[] {
					new Vector3(headPos.X - playerRadius, headPos.Y, headPos.Z - playerRadius),
					new Vector3(headPos.X + playerRadius, headPos.Y, headPos.Z - playerRadius),
					new Vector3(headPos.X - playerRadius, headPos.Y, headPos.Z + playerRadius),
					new Vector3(headPos.X + playerRadius, headPos.Y, headPos.Z + playerRadius),
					headPos
				};
				foreach (var pt in headCheckPoints) {
					if (Map.GetBlock((int)MathF.Floor(pt.X), (int)MathF.Floor(pt.Y + 0.1f), (int)MathF.Floor(pt.Z)) != BlockType.None) {
						PlyVelocity.Y = 0;
						break;
					}
				}
			}
			Vector2 horizVel2 = new Vector2(PlyVelocity.X, PlyVelocity.Z);
			float horizSpeed2 = horizVel2.Length();
			float maxSpeed2 = OnGroundGrace ? (Raylib.IsKeyDown(KeyboardKey.LeftShift) ? PhysicsData.MaxWalkSpeed : PhysicsData.MaxGroundSpeed) : PhysicsData.MaxAirSpeed;
			if (horizSpeed2 > maxSpeed2) {
				float scale = maxSpeed2 / horizSpeed2;
				PlyVelocity.X *= scale;
				PlyVelocity.Z *= scale;
			}
			if (PlyVelocity.Y > 0) {
				float headEpsilon = 0.02f;
				Vector3 feetPos2 = FeetPosition;
				Vector3 headPos = feetPos2 + new Vector3(0, playerHeight - headEpsilon, 0);
				Vector3[] headCheckPoints = new Vector3[] {
					new Vector3(headPos.X - playerRadius, headPos.Y, headPos.Z - playerRadius),
					new Vector3(headPos.X + playerRadius, headPos.Y, headPos.Z - playerRadius),
					new Vector3(headPos.X - playerRadius, headPos.Y, headPos.Z + playerRadius),
					new Vector3(headPos.X + playerRadius, headPos.Y, headPos.Z + playerRadius),
					headPos
				};
				foreach (var pt in headCheckPoints) {
					if (Map.GetBlock((int)MathF.Floor(pt.X), (int)MathF.Floor(pt.Y + 0.1f), (int)MathF.Floor(pt.Z)) != BlockType.None) {
						PlyVelocity.Y = 0;
						break;
					}
				}
			}
			Utils.EndRaycastRecord();
		}

		public void Tick(InputMgr InMgr) {
			string AnimName = "idle";

			// Use InputMgr for movement keys
			if (MoveFd = InMgr.IsInputDown(InputKey.W))
				AnimName = "forward";
			else if (MoveLt = InMgr.IsInputDown(InputKey.A))
				AnimName = "left";
			else if (MoveRt = InMgr.IsInputDown(InputKey.D))
				AnimName = "right";
			else if (MoveBk = InMgr.IsInputDown(InputKey.S))
				AnimName = "backward";

			if (CurAnim == null)
				CurAnim = PlayerEntity.GetAnim(AnimName);

			if (CurAnim.Name != AnimName) {
				CurAnim = PlayerEntity.GetAnim(AnimName);
				CurAnim.Apply();
			}

			FPSCamera.Update(CursorDisabled, ref Cam);

			// Use InputMgr for F1
			if (InMgr.IsInputPressed(InputKey.F1))
				ToggleMouse();

			// Keep OnKeyFuncs using Raylib for now (as they are mapped to KeyboardKey)
			foreach (var KV in OnKeyFuncs) {
				if (Raylib.IsKeyPressed(KV.Key))
					KV.Value();
			}


			//PlayerEntity.GetBone("Neck", out int BoneID);

			Position = FPSCamera.Position;
			Rotation = Matrix4x4.CreateFromYawPitchRoll(0, (float)Math.PI / 2, 0) /** Matrix4x4.CreateFromYawPitchRoll(0, 0, (float)((Math.PI / 2) - (FPSCamera.CamAngle.X * (Math.PI / 180))))*/;
			/// CurAnim.Step();
		}

		GUIItemBox Box_Health;
		GUILabel InfoLbl;
		GUIInventory Inventory;

		public void InitGUI(GameWindow Window) {
			Box_Health = new GUIItemBox(GUI);
			Box_Health.Pos = new Vector2(64, Window.Height - 128);
			Box_Health.Text = "100";
			Box_Health.SetIcon(ResMgr.GetTexture("items/heart_full.png"), 3);
			GUI.AddElement(Box_Health);

			InfoLbl = new GUILabel(GUI);
			InfoLbl.Pos = new Vector2(16, 40);
			InfoLbl.Size = new Vector2(300, 250);
			InfoLbl.Clear();
			InfoLbl.WriteLine("Hello World!");
			GUI.AddElement(InfoLbl);

			Inventory = new GUIInventory(GUI);
			Inventory.Pos = GUI.WindowScale(new Vector2(0.5f, 0.9f));
			Inventory.Pos -= new Vector2(Inventory.Size.X / 2, 0);
			GUI.AddElement(Inventory);

			SetInvItem(Inventory, 0, BlockType.Dirt, (ItmBox, Idx) => PlayerSelectedBlockType = BlockType.Dirt);
			SetInvItem(Inventory, 1, BlockType.Stone, (ItmBox, Idx) => PlayerSelectedBlockType = BlockType.Stone);
			SetInvItem(Inventory, 2, BlockType.StoneBrick, (ItmBox, Idx) => PlayerSelectedBlockType = BlockType.StoneBrick);
			SetInvItem(Inventory, 3, BlockType.Bricks, (ItmBox, Idx) => PlayerSelectedBlockType = BlockType.Bricks);
			SetInvItem(Inventory, 4, BlockType.Plank, (ItmBox, Idx) => PlayerSelectedBlockType = BlockType.Plank);
			SetInvItem(Inventory, 5, BlockType.CraftingTable, (ItmBox, Idx) => PlayerSelectedBlockType = BlockType.CraftingTable);
			SetInvItem(Inventory, 6, BlockType.Glowstone, (ItmBox, Idx) => PlayerSelectedBlockType = BlockType.Glowstone);
		}

		public void UpdateGUI() {
			InfoLbl.Enabled = false;

			if (Program.DebugMode) {
				InfoLbl.Enabled = true;
				InfoLbl.Clear();
				InfoLbl.WriteLine("Pos: {0:0.00}, {1:0.00}, {2:0.00}", MathF.Round(Position.X, 2), MathF.Round(Position.Y, 2), MathF.Round(Position.Z, 2));
				InfoLbl.WriteLine("Vel: {0:0.000}", MathF.Round(GetVelocity().Length(), 3));
				InfoLbl.WriteLine("No-clip: {0}", NoClip ? "ON" : "OFF");
				InfoLbl.WriteLine("OnGround: {0}", GetWasLastLegsOnFloor() ? "YES" : "NO");
			}
		}

		void SetInvItem(GUIInventory Inventory, int Idx, BlockType BType, Action<GUIItemBox, int> OnClick) {
			GUIItemBox Itm = Inventory.GetItem(Idx);

			BlockInfo.GetBlockTexCoords(BType, new Vector3(0, 1, 0), out Vector2 UVSize, out Vector2 UVPos);

			Itm.SetIcon(ResMgr.AtlasTexture, 0.092f, UVPos, UVSize);
			Itm.OnClickedFunc = (E) => {
				Inventory.SetSelectedIndex(Idx);
				OnClick(E as GUIItemBox, Idx);
			};
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
				Vector3 Dir = GetForward();
				Vector3 Start = Position;
				if (Left) {
					Utils.Raycast(Start, Dir, MaxLen, (X, Y, Z, Face) => {
						if (Map.GetBlock(X, Y, Z) != BlockType.None) {
							Snd.PlayCombo("block_break", Start, Dir, new Vector3(X, Y, Z));
							Map.SetBlock(X, Y, Z, BlockType.None);
							return true;
						}
						return false;
					});
				}
				if (Right) {
					Utils.Raycast(Start, Dir, MaxLen, (X, Y, Z, Face) => {
						if (Map.GetBlock(X, Y, Z) != BlockType.None) {
							X += (int)Face.X;
							Y += (int)Face.Y;
							Z += (int)Face.Z;
							Snd.PlayCombo("block_place", Start, Dir, new Vector3(X, Y, Z));
							Map.SetBlock(X, Y, Z, PlayerSelectedBlockType);
							return true;
						}
						return false;
					});
				}
				if (Middle) {
					Utils.Raycast(Start, Dir, MaxLen, (X, Y, Z, Face) => {
						if (Map.GetBlock(X, Y, Z) != BlockType.None) {
							X += (int)Face.X;
							Y += (int)Face.Y;
							Z += (int)Face.Z;
							Snd.PlayCombo("block_place", Start, Dir, new Vector3(X, Y, Z));
							Map.SetBlock(X, Y, Z, BlockType.Campfire);
							return true;
						}
						return false;
					});
				}
			}
			if (!CursorDisabled) {
				GUI.Tick();
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

		public void Draw() {
			if (!DEBUG_PLAYER && LocalPlayer)
				return;

			Vector3 DrawPos = Position - new Vector3(0, 1.8f, 0);

			if (DEBUG_PLAYER)
				DrawPos = Vector3.Zero;

			PlayerEntity.Mdl.Transform = Rotation;
			Raylib.DrawModel(PlayerEntity.Mdl, DrawPos, 0.25f, Color.White);
		}

		public void AddOnKeyPressed(KeyboardKey K, Action Act) {
			OnKeyFuncs.Add(K, Act);
		}

		public void Write(System.IO.BinaryWriter writer) {
			// Write position
			writer.Write(Position.X);
			writer.Write(Position.Y);
			writer.Write(Position.Z);
			// Write camera angle
			writer.Write(CamAngle.X);
			writer.Write(CamAngle.Y);
			writer.Write(CamAngle.Z);
			// Write rotation (Matrix4x4 as 16 floats)
			for (int row = 0; row < 4; row++)
				for (int col = 0; col < 4; col++)
					writer.Write(Rotation[row, col]);
			// Write upper body rotation
			for (int row = 0; row < 4; row++)
				for (int col = 0; col < 4; col++)
					writer.Write(UpperBodyRotation[row, col]);
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
			CamAngle = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
			// Read rotation
			for (int row = 0; row < 4; row++)
				for (int col = 0; col < 4; col++)
					Rotation[row, col] = reader.ReadSingle();
			// Read upper body rotation
			for (int row = 0; row < 4; row++)
				for (int col = 0; col < 4; col++)
					UpperBodyRotation[row, col] = reader.ReadSingle();
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

		public Vector3 CamAngle {
			get => FPSCamera.CamAngle;
			set => FPSCamera.CamAngle = value;
		}

		Vector3 Fwd;
		Vector3 Left;
		Vector3 Up;

		public Vector3 GetForward() => Fwd;
		public Vector3 GetLeft() => Left;
		public Vector3 GetUp() => Up;

		public void UpdateFPSCamera() {
			Fwd = FPSCamera.GetForward();
			Left = FPSCamera.GetLeft();
			Up = FPSCamera.GetUp();
		}

		//public void UpdateCamera(bool handleRotation) => FPSCamera.Update(handleRotation, ref Cam);
		//public Vector2 GetPreviousMousePos() => FPSCamera.GetPreviousMousePos();

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
