using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace RaylibSharp {
	public unsafe static partial class Raylib {
		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern Mesh GenMeshRaw(void* vertices, int vertices_len, void* indices, int indices_len, void* texcoords, int texcoords_len, void* normals, int normals_len, void* colors, int colors_len);

		public static Mesh GenMeshRaw(Vector3[] Vertices, ushort[] Indices = null, Vector2[] Texcoords = null, Vector3[] Normals = null, Color[] Colors = null) {
			fixed (Vector3* VerticesPtr = Vertices)
			fixed (ushort* IndicesPtr = Indices)
			fixed (Vector2* TexcoordsPtr = Texcoords)
			fixed (Vector3* NormalsPtr = Normals)
			fixed (Color* ColorsPtr = Colors) {
				int VerticesLen = Vertices.Length;
				int IndicesLen = Indices?.Length ?? 0;
				int TexcoordsLen = Texcoords?.Length ?? 0;
				int NormalsLen = Normals?.Length ?? 0;
				int ColorsLen = Colors?.Length ?? 0;

				VerticesLen *= sizeof(Vector3);
				IndicesLen *= sizeof(ushort);
				TexcoordsLen *= sizeof(Vector2);
				NormalsLen *= sizeof(Vector3);
				ColorsLen *= sizeof(Color);

				return GenMeshRaw(VerticesPtr, VerticesLen, IndicesPtr, IndicesLen, TexcoordsPtr, TexcoordsLen, NormalsPtr, NormalsLen, ColorsPtr, ColorsLen);
			}
		}

		public static Mesh GenMeshRaw(Vertex3[] Verts) {
			Vector3[] Positions = new Vector3[Verts.Length];
			Vector2[] Texcoords = new Vector2[Verts.Length];
			Color[] Colors = new Color[Verts.Length];

			for (int i = 0; i < Verts.Length; i++) {
				Positions[i] = Verts[i].Position;
				Texcoords[i] = Verts[i].UV;
				Colors[i] = Verts[i].Color;
			}

			return GenMeshRaw(Positions, null, Texcoords, null, Colors);
		}
	}
}
