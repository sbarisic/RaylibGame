using RaylibGame.Engine;

using Raylib_cs;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

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

		bool MoveFd;
		bool MoveBk;
		bool MoveLt;
		bool MoveRt;

		public Vector3 Position;
		public Matrix4x4 Rotation;
		public Matrix4x4 UpperBodyRotation;

		bool CursorDisabled = false;
		bool LocalPlayer;

		Dictionary<KeyboardKey, Action> OnKeyFuncs = new Dictionary<KeyboardKey, Action>();

		Stopwatch LegTimer = Stopwatch.StartNew();
		long LastWalkSound = 0;
		long LastJumpSound = 0;
		long LastCrashSound = 0;

		SoundMgr Snd;

		public Player(string ModelName, bool LocalPlayer, SoundMgr Snd) {
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
		}

		void ToggleMouse() {
			if (CursorDisabled)
				Raylib.EnableCursor();
			else
				Raylib.DisableCursor();

			CursorDisabled = !CursorDisabled;
		}

		public void SetPosition(int X, int Y, int Z) {
			Position = FPSCamera.Position = new Vector3(X, Y, Z);
		}

		public void SetPosition(Vector3 Pos) {
			Position = FPSCamera.Position = Pos;
		}

		public void UpdatePhysics(float Dt) {

		}

		public void PhysicsHit(float Force, bool Side, bool Feet, bool Walk, bool Jump) {
			if (Walk) {
				if (LegTimer.ElapsedMilliseconds > LastWalkSound + 350) {
					LastWalkSound = LegTimer.ElapsedMilliseconds;

					//Console.WriteLine("Walk");
					Snd.PlayCombo("walk");
				}
			} else if (Jump) {
				if (LegTimer.ElapsedMilliseconds > LastJumpSound + 350) {
					LastJumpSound = LegTimer.ElapsedMilliseconds;

					//Console.WriteLine("Walk");
					Snd.PlayCombo("jump");
				}
			} else if (Feet && !Side) {

				if (LegTimer.ElapsedMilliseconds > LastCrashSound + 350) {
					LastCrashSound = LegTimer.ElapsedMilliseconds;

					if (Force < 4) {
						Snd.PlayCombo("crash1");
					} else if (Force >= 4 && Force < 8) {
						Snd.PlayCombo("crash2");
					} else if (Force >= 8) {
						Snd.PlayCombo("crash3");
					}
				}
			} else {
				Console.WriteLine("Sid: {0}, Ft: {1}, F: {2}, W: {3}", Side, Feet, Force, Walk);
			}
		}

		public void Parkour(Vector3 NewPos) {
			Console.WriteLine("Parkour!");
			SetPosition(NewPos);
		}

		public void Update() {
			string AnimName = "idle";

			if (MoveFd = Raylib.IsKeyDown(KeyboardKey.W))
				AnimName = "forward";
			else if (MoveLt = Raylib.IsKeyDown(KeyboardKey.A))
				AnimName = "left";
			else if (MoveRt = Raylib.IsKeyDown(KeyboardKey.D))
				AnimName = "right";
			else if (MoveBk = Raylib.IsKeyDown(KeyboardKey.S))
				AnimName = "backward";

			if (CurAnim == null)
				CurAnim = PlayerEntity.GetAnim(AnimName);

			if (CurAnim.Name != AnimName) {
				CurAnim = PlayerEntity.GetAnim(AnimName);
				CurAnim.Apply();
			}

			FPSCamera.Update(CursorDisabled, ref Cam);

			if (Raylib.IsKeyPressed(KeyboardKey.F1))
				ToggleMouse();

			foreach (var KV in OnKeyFuncs) {
				if (Raylib.IsKeyPressed(KV.Key))
					KV.Value();
			}


			//PlayerEntity.GetBone("Neck", out int BoneID);

			Position = FPSCamera.Position;
			Rotation = Matrix4x4.CreateFromYawPitchRoll(0, (float)Math.PI / 2, 0) /** Matrix4x4.CreateFromYawPitchRoll(0, 0, (float)((Math.PI / 2) - (FPSCamera.CamAngle.X * (Math.PI / 180))))*/;
			/// CurAnim.Step();
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
	}
}
