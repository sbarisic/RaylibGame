﻿using Raylib_cs;
using System.Numerics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Voxelgine.Graphics {
	class VectorMesh {
		public List<Vertex3> Vertices;

		public string MaterialName;

		public VectorMesh(string MaterialName) {
			this.MaterialName = MaterialName;
			Vertices = new List<Vertex3>();
		}

		public void AddVertex(Vertex3 V) {
			Vertices.Add(V);
		}

		public void SwapWindingOrder() {

		}
	}
}
