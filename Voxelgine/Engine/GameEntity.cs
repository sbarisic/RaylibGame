using Raylib_cs;

using RaylibGame.States;

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

		public bool HasCollision = true;

		
		public CustomModel Model;
		GameState State;

		public GameEntity(GameState State, Vector3 Position) {
			this.Position = Position;
			this.State = State;

			MinecraftModel JMdl = ResMgr.GetJsonModel("npc/humanoid.json");
			Model = MeshGenerator.Generate(JMdl);
			Model.Position = Position;

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
			//Model.LookDirection = Vector3.Normalize(State.Ply.Position - Model.Position);
		}

		public virtual void Draw() {

			Model.Draw();

			//Raylib.DrawModel(Model, Position, 1, Color.White);
		}
	}
}
