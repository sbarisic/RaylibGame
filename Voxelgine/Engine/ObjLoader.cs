using Raylib_cs;
using Voxelgine.Graphics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using GenericMesh = Voxelgine.Graphics.VectorMesh;

namespace Voxelgine.Engine {
	static class Obj {
		public static GenericMesh[] LoadRaw(string Raw, bool SwapWindingOrder = true) {
			List<GenericMesh> Meshes = new List<GenericMesh>();
			GenericMesh CurMesh = null;

			//List<Vertex3> ObjVertices = new List<Vertex3>();

			string[] Lines = Raw.Replace("\r", "").Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
			List<Vector3> Verts = new List<Vector3>();
			List<Vector2> UVs = new List<Vector2>();
			List<Vector3> Norms = new List<Vector3>();

			for (int j = 0; j < Lines.Length; j++) {
				string Line = Lines[j].Trim().Replace('\t', ' ');

				while (Line.Contains("  "))
					Line = Line.Replace("  ", " ");

				if (Line.StartsWith("#"))
					continue;

				string[] Tokens = Line.Split(' ');
				switch (Tokens[0].ToLower()) {
					case "o":
						break;

					case "v": // Vertex
						Verts.Add(new Vector3(Tokens[1].ParseFloat(), Tokens[2].ParseFloat(), Tokens[3].ParseFloat()));
						break;

					case "vt": // Texture coordinate
						UVs.Add(new Vector2(Tokens[1].ParseFloat(), Tokens[2].ParseFloat()));
						break;

					case "vn": // Normal
						Norms.Add(new Vector3(Tokens[1].ParseFloat(), Tokens[2].ParseFloat(), Tokens[3].ParseFloat()));
						break;

					case "f": // Face
						if (CurMesh == null) {
							CurMesh = new GenericMesh("default");
							Meshes.Add(CurMesh);
						}

						for (int i = 2; i < Tokens.Length - 1; i++) {
							string[] V = Tokens[1].Split('/');
							CurMesh.AddVertex(new Vertex3(Verts[V[0].ParseInt(1) - 1], V.Length > 1 ? UVs[V[1].ParseInt(1) - 1] : Vector2.Zero, Vector3.Zero));

							V = Tokens[i].Split('/');
							CurMesh.AddVertex(new Vertex3(Verts[V[0].ParseInt(1) - 1], V.Length > 1 ? UVs[V[1].ParseInt(1) - 1] : Vector2.Zero, Vector3.Zero));

							V = Tokens[i + 1].Split('/');
							CurMesh.AddVertex(new Vertex3(Verts[V[0].ParseInt(1) - 1], V.Length > 1 ? UVs[V[1].ParseInt(1) - 1] : Vector2.Zero, Vector3.Zero));
						}

						break;

					case "usemtl":
						CurMesh = Meshes.Where(M => M.MaterialName == Tokens[1]).FirstOrDefault();
						if (CurMesh == null) {
							CurMesh = new GenericMesh(Tokens[1]);
							Meshes.Add(CurMesh);
						}
						break;

					default:
						break;
				}
			}

			if (SwapWindingOrder)
				foreach (var Msh in Meshes)
					Msh.SwapWindingOrder();

			return Meshes.ToArray();
		}

		public static GenericMesh[] LoadFromFile(string Src, bool SwapWindingOrder = true) {
			return LoadRaw(File.ReadAllText(Src), SwapWindingOrder);
		}
	}
}
