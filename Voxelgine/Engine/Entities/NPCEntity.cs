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
		BoundingBox BBox;

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

			EntModelName = MdlName;
			MinecraftModel JMdl = ResMgr.GetJsonModel("npc/humanoid.json");
			CModel = MeshGenerator.Generate(JMdl);
			HasModel = true;
			BBox = CModel.GetBoundingBox();

			if (Size != Vector3.Zero) {
				//ModelOffset = new Vector3(Size.X / 2, ModelOffset.Y, Size.Y / 2);

				Vector3 Off = (BBox.Max - BBox.Min) / 2;
				ModelOffset = new Vector3(Size.X / 2, 0, Size.Z / 2);
			}
		}

		public override void Draw3D(float TimeAlpha, ref GameFrameInfo LastFrame) {
			if (HasModel) {
				BBox = CModel.GetBoundingBox();

				CModel.Position = Position + ModelOffset;
				CModel.LookDirection = Vector3.UnitZ;
				CModel.Draw();

				Raylib.DrawBoundingBox(BBox, Color.Blue);
				//Raylib.DrawModelEx(EntModel, Position + ModelOffset + (BobbingLerp?.GetVec3() ?? Vector3.Zero), Vector3.UnitY, ModelRotationDeg, ModelScale, ModelColor);
			}

			DrawCollisionBox();

		}
	}
}