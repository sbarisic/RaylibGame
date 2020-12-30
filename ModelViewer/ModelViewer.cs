using RaylibSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace ModelViewer {
	class AnimationState {
		ModelAnimation Anim;
		int CurFrame;
		int FrameCount;

		public AnimationState(ModelAnimation Anim) {
			this.Anim = Anim;

			CurFrame = 0;
			FrameCount = Anim.frameCount;
		}

		public void Step(Model Mdl) {
			Raylib.UpdateModelAnimation(Mdl, Anim, CurFrame++);

			if (CurFrame >= FrameCount)
				CurFrame = 0;
		}
	}

	unsafe class Program {
		static void Main(string[] args) {
			Raylib.InitWindow(800, 600, "ModelViewer");
			Raylib.SetTargetFPS(60);

			Model IqmModel = LoadModel("models/snoutx10k/snoutx10k.iqm");
			Raylib.SetMaterialTexture(&IqmModel.materials[1], MaterialMapType.MAP_ALBEDO, Raylib.LoadTexture("models/snoutx10k/upper.png"));
			Raylib.SetMaterialTexture(&IqmModel.materials[0], MaterialMapType.MAP_ALBEDO, Raylib.LoadTexture("models/snoutx10k/lower.png"));

			AnimationState Anim_Idle = LoadAnim("models/snoutx10k/idle.md5anim.iqm");
			AnimationState Anim_Left = LoadAnim("models/snoutx10k/left.md5anim.iqm");
			AnimationState Anim_Right = LoadAnim("models/snoutx10k/right.md5anim.iqm");
			AnimationState Anim_Back = LoadAnim("models/snoutx10k/backward.md5anim.iqm");
			AnimationState Anim_Forward = LoadAnim("models/snoutx10k/forward.md5anim.iqm");
			AnimationState Anim_Jump = LoadAnim("models/snoutx10k/jump.md5anim.iqm");

			BoundingBox BBox = Raylib.MeshBoundingBox(IqmModel.meshes[0]);
			float CamDist = (BBox.max - BBox.min).Length() / 2;

			Camera3D Cam = new Camera3D(new Vector3(CamDist), new Vector3(0, 0, 0), Vector3.UnitY);
			Raylib.SetCameraMode(Cam, CameraMode.CAMERA_ORBITAL);


			while (!Raylib.WindowShouldClose()) {

				if (Raylib.IsKeyDown(KeyboardKey.KEY_A))
					Anim_Left.Step(IqmModel);
				else if (Raylib.IsKeyDown(KeyboardKey.KEY_D))
					Anim_Right.Step(IqmModel);
				else if (Raylib.IsKeyDown(KeyboardKey.KEY_SPACE))
					Anim_Jump.Step(IqmModel);
				else if (Raylib.IsKeyDown(KeyboardKey.KEY_S))
					Anim_Back.Step(IqmModel);
				else if (Raylib.IsKeyDown(KeyboardKey.KEY_W))
					Anim_Forward.Step(IqmModel);
				else
					Anim_Idle.Step(IqmModel);


				Raylib.UpdateCamera(ref Cam);

				Raylib.BeginDrawing();
				{
					Raylib.ClearBackground(new Color(100, 100, 100));
					Raylib.BeginMode3D(Cam);
					{

						Raylib.DrawModel(IqmModel, Vector3.Zero, 1, Color.White);

					}
					Raylib.EndMode3D();
					Raylib.DrawFPS(10, 10);
				}
				Raylib.EndDrawing();
			}
		}

		static Model LoadModel(string ModelPath) {
			Model IqmModel = Raylib.LoadModel(ModelPath);
			IqmModel.transform = Matrix4x4.CreateFromYawPitchRoll(0, (float)Math.PI / 2, 0);

			IqmModel.materialCount = IqmModel.meshCount;
			IqmModel.materials = (Material*)Marshal.AllocHGlobal(IqmModel.materialCount * sizeof(Material));

			for (int i = 0; i < IqmModel.materialCount; i++) {
				IqmModel.materials[i] = Raylib.LoadMaterialDefault();
				IqmModel.meshMaterial[i] = i;
			}

			return IqmModel;
		}

		static AnimationState LoadAnim(string AnimPath) {
			int AnimCount = 0;
			ModelAnimation* Anims = Raylib.LoadModelAnimations(AnimPath, &AnimCount);
			return new AnimationState(Anims[0]);
		}
	}
}
