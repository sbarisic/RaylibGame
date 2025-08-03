using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

using Raylib_cs;

using RaylibGame.States;

using Voxelgine.Graphics;

namespace Voxelgine.Engine {
	class NPCEntity : BaseEntity {
		CustomModel CModel;

		public NPCEntity() : base() {
			IsRotating = false;
			IsBobbing = false;
		}

		public override void SetModel(string MdlName) {
			HasModel = false;
			ModelOffset = Vector3.Zero;
			ModelRotationDeg = 0;
			ModelColor = Color.White;
			ModelScale = Vector3.One;

			if (Size != Vector3.Zero) {
				//ModelOffset = new Vector3(Size.X / 2, ModelOffset.Y, Size.Y / 2);

			}

			EntModelName = MdlName;
			MinecraftModel JMdl = ResMgr.GetJsonModel("npc/humanoid.json");
			CModel = MeshGenerator.Generate(JMdl);
			HasModel = true;
		}

		public override void Draw3D(float TimeAlpha, ref GameFrameInfo LastFrame) {
			if (HasModel) {
				CModel.Position = Position + ModelOffset;
				CModel.LookDirection = Vector3.UnitZ;
				CModel.Draw();
				//Raylib.DrawModelEx(EntModel, Position + ModelOffset + (BobbingLerp?.GetVec3() ?? Vector3.Zero), Vector3.UnitY, ModelRotationDeg, ModelScale, ModelColor);
			}

			DrawCollisionBox();

		}
	}
}