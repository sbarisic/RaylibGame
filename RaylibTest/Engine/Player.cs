using RaylibSharp;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace RaylibTest.Engine {
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

			return Matrix4x4.CreateFromQuaternion(Transform->rotation) * Matrix4x4.CreateScale(Transform->scale) * Matrix4x4.CreateTranslation(Transform->translation);
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

			Matrix4x4.Decompose(CalcWorldTransform(), out Transform->scale, out Transform->rotation, out Transform->translation);
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

		public Camera3D Cam = new Camera3D(Vector3.Zero, Vector3.UnitX, Vector3.UnitY);
		AnimatedEntity PlayerEntity;
		EntityAnimation CurAnim;

		bool MoveFd;
		bool MoveBk;
		bool MoveLt;
		bool MoveRt;

		Vector3 Position;
		Matrix4x4 Rotation;
		Matrix4x4 UpperBodyRotation;

		bool CursorDisabled = false;
		bool LocalPlayer;

		public KeyboardKey FuncKey;
		public OnKeyPressedFunc OnKeyPressed;

		public Player(string ModelName, bool LocalPlayer) {
			this.LocalPlayer = LocalPlayer;
			PlayerEntity = Entities.Load(ModelName);

			Position = Vector3.Zero;
			Rotation = Matrix4x4.Identity;
			UpperBodyRotation = Matrix4x4.Identity;

			Raylib.SetCameraMode(Cam, CameraMode.CAMERA_CUSTOM);
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

		public void Update() {
			string AnimName = "idle";

			if (MoveFd = Raylib.IsKeyDown(KeyboardKey.KEY_W))
				AnimName = "forward";
			else if (MoveLt = Raylib.IsKeyDown(KeyboardKey.KEY_A))
				AnimName = "left";
			else if (MoveRt = Raylib.IsKeyDown(KeyboardKey.KEY_D))
				AnimName = "right";
			else if (MoveBk = Raylib.IsKeyDown(KeyboardKey.KEY_S))
				AnimName = "backward";

			if (CurAnim == null)
				CurAnim = PlayerEntity.GetAnim(AnimName);

			if (CurAnim.Name != AnimName) {
				CurAnim = PlayerEntity.GetAnim(AnimName);
				CurAnim.Apply();
			}

			if (CursorDisabled)
				FPSCamera.Update(ref Cam);

			if (Raylib.IsKeyPressed(KeyboardKey.KEY_F1))
				ToggleMouse();

			if (Raylib.IsKeyPressed(FuncKey))
				OnKeyPressed();


			//PlayerEntity.GetBone("Neck", out int BoneID);

			Position = FPSCamera.Position - new Vector3(0, 1.8f, 0);
			Rotation = Matrix4x4.CreateFromYawPitchRoll(0, (float)Math.PI / 2, 0) /** Matrix4x4.CreateFromYawPitchRoll(0, 0, (float)((Math.PI / 2) - (FPSCamera.CamAngle.X * (Math.PI / 180))))*/;
			/// CurAnim.Step();
		}

		public void Draw() {
			if (!DEBUG_PLAYER && LocalPlayer)
				return;

			if (DEBUG_PLAYER)
				Position = Vector3.Zero;

			PlayerEntity.Mdl.transform = Rotation;
			Raylib.DrawModel(PlayerEntity.Mdl, Position, 0.25f, Color.White);
		}
	}
}
