using Raylib_cs;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Voxelgine.Engine {
	unsafe class GameEntity {
		public Vector3 Position;
		public Vector3 Size;

		//public Texture2D Texture;
		public Model Model;

		CustomModel CModel;

		public GameEntity(Vector3 Position) {
			this.Position = Position;

			MinecraftModel JMdl = ResMgr.GetJsonModel("npc/humanoid.json");
			CModel = MeshGenerator.Generate(JMdl);
			CModel.Position = Position;

			//Texture = ResMgr.GetTexture("npc/humanoid.png");
			/*Model = ResMgr.GetModel("npc/humanoid.fbx");

			for (int i = 0; i < Model.MaterialCount; i++) {
				Raylib.SetMaterialTexture(ref Model.Materials[i], MaterialMapIndex.Diffuse, Texture);
			}

			for (int i = 0; i < Model.MeshCount; i++) {
				ref Mesh M = ref Model.Meshes[i];


				Console.WriteLine(M);
			}*/
		}

		public virtual void Update(float Dt) {
		}

		public virtual void Draw() {

			CModel.Draw();

			//Raylib.DrawModel(Model, Position, 1, Color.White);
		}
	}
}
