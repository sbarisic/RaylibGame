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

		public NPCEntity(GameState State, string Name, Vector3 Pos, Vector3 Size) : base(State, Name, Pos, Size) {
			IsRotating = false;
			IsBobbing = false;

			SetModel("npc/humanoid.json");
		}

		public override void SetModel(string MdlName) {
			HasModel = false;
			ModelOffset = Vector3.Zero;
			ModelRotationDeg = 0;
			ModelColor = Color.White;
			ModelScale = Vector3.One;

			if (Size != Vector3.Zero) {
				ModelOffset = new Vector3(Size.X / 2, ModelOffset.Y, Size.Y / 2);
			}

			EntModelName = MdlName;
			MinecraftModel JMdl = ResMgr.GetJsonModel("npc/humanoid.json");
			EntModel = MeshGenerator.Generate(JMdl);
			HasModel = EntModel.MeshCount > 0;
		}

	}
}