using Raylib_cs;
using System.Numerics;
using Voxelgine.Engine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Voxelgine.Graphics {
	static class GraphicsUtils {
		static Dictionary<Vector3, List<Vertex3>> CubeSides;

		public static void Init() {
			Vertex3[] CubeVertices = Obj.LoadRaw(@"o Cube
				v 0.500000 -0.500000 -0.500000
				v 0.500000 -0.500000 0.500000
				v -0.500000 -0.500000 0.500000
				v -0.500000 -0.500000 -0.500000
				v 0.500000 0.500000 -0.500000
				v 0.500000 0.500000 0.500000
				v -0.500000 0.500000 0.500000
				v -0.500000 0.500000 -0.500000
				vt 0.000000 0.000000
				vt 1.000000 0.000000
				vt 1.000000 1.000000
				vt 0.000000 1.000000
				vn 0.000000 -1.000000 0.000000
				vn 0.000000 1.000000 0.000000
				vn 1.000000 0.000000 0.000001
				vn -0.000000 -0.000000 1.000000
				vn -1.000000 -0.000000 -0.000000
				vn 0.000000 0.000000 -1.000000
				vn 1.000000 -0.000000 0.000000
				s off
				f 2/1/1 3/2/1 4/3/1
				f 5/3/2 8/4/2 7/1/2
				f 5/3/3 6/4/3 2/1/3
				f 2/2/4 6/3/4 7/4/4
				f 7/3/5 8/4/5 4/1/5
				f 5/4/6 1/1/6 4/2/6
				f 1/4/1 2/1/1 4/3/1
				f 6/2/2 5/3/2 7/1/2
				f 1/2/7 5/3/7 2/1/7
				f 3/1/4 2/2/4 7/4/4
				f 3/2/5 7/3/5 4/1/5
				f 8/3/6 5/4/6 4/2/6")[0].Vertices.ToArray();

			// Offset by .5
			for (int i = 0; i < CubeVertices.Length; i++) {
				Vertex3 V = CubeVertices[i];
				V.Position = V.Position + new Vector3(0.5f, 0.5f, 0.5f);
				CubeVertices[i] = V;
			}

			CubeSides = new Dictionary<Vector3, List<Vertex3>>();
			Vector3[] Normals = { new Vector3(1, 0, 0), new Vector3(-1, 0, 0), new Vector3(0, 1, 0), new Vector3(0, -1, 0), new Vector3(0, 0, 1), new Vector3(0, 0, -1), };

			for (int ni = 0; ni < Normals.Length; ni++) {
				Vector3 N = Normals[ni];

				if (!CubeSides.ContainsKey(N))
					CubeSides.Add(N, new List<Vertex3>());

				for (int i = 0; i < CubeVertices.Length; i += 3) {
					Vertex3 A = CubeVertices[i];
					Vertex3 B = CubeVertices[i + 1];
					Vertex3 C = CubeVertices[i + 2];
					Vector3 TriNormal = GetNormal(A.Position, B.Position, C.Position);

					if (TriNormal == N) {
						CubeSides[N].Add(A);
						CubeSides[N].Add(B);
						CubeSides[N].Add(C);
					}
				}
			}
		}

		public static void AppendBlockVertices(List<Vertex3> VertList, Vector3 Norm, Vector3 Pos, Vector3 Size, Vector2 UVPos, Vector2 UVSize, Color Clr) {
			List<Vertex3> Side = CubeSides[Norm];

			for (int i = 0; i < Side.Count; i++) {
				Vertex3 V = Side[i];

				Console.WriteLine(Utils.ToString(V.UV));

				V.Position = V.Position * Size + Pos;
				V.UV = V.UV * UVSize + UVPos;
				V.Color = Clr;

				VertList.Add(V);
			}
		}

		public static Vector3 GetNormal(Vector3 A, Vector3 B, Vector3 C) {
			return Vector3.Normalize(Vector3.Cross(C - B, A - B));
		}
	}
}
