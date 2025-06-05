using Raylib_cs;
using Voxelgine.Engine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Voxelgine.Engine {
	class Skybox {
		public Skybox() {
			Mesh CubeMesh = Raylib.GenMeshCube(1, 1, 1);
			Model CubeModel = Raylib.LoadModelFromMesh(CubeMesh);
		}

		public void Draw() {

		}
	}
}
