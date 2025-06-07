using Raylib_cs;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Voxelgine.Engine {
	delegate Color AOCalcFunc(Vector3 VertexPos);

	unsafe class MeshBuilder {
		List<Vertex3> Verts;

		Vector3 PosOffset;
		Vector2 UVPos;
		Vector2 UVSize;

		public MeshBuilder() {
			Verts = new List<Vertex3>();
			SetPositionOffset(Vector3.Zero);
			SetUVOffsetSize(Vector2.Zero, Vector2.One);
		}

		void FreeBuffers(ref Mesh M) {
			if (M.Indices != null) {
				Marshal.FreeHGlobal((nint)M.Indices);
				M.Indices = null;
			}

			if (M.Vertices != null) {
				Marshal.FreeHGlobal((nint)M.Vertices);
				M.Vertices = null;
			}


			if (M.Colors != null) {
				Marshal.FreeHGlobal((nint)M.Colors);
				M.Colors = null;
			}


			if (M.TexCoords != null) {
				Marshal.FreeHGlobal((nint)M.TexCoords);
				M.TexCoords = null;
			}


			if (M.TexCoords2 != null) {
				Marshal.FreeHGlobal((nint)M.TexCoords2);
				M.TexCoords2 = null;
			}
		}

		public void SetPositionOffset(Vector3 Pos) {
			PosOffset = Pos;
		}

		public void SetUVOffsetSize(Vector2 UVPos, Vector2 UVSize) {
			this.UVPos = UVPos;
			this.UVSize = UVSize;
		}

		public void Add(Vertex3 Vert) {
			Verts.Add(Vert);
		}

		public void Add(Vector3 Pos) {
			Add(new Vertex3(Pos + PosOffset, Vector2.Zero, Vector3.Zero));
		}

		public void Add(Vector3 Pos, Vector2 UV) {
			Add(new Vertex3(Pos + PosOffset, UVPos + UV * UVSize, Vector3.Zero));
		}

		public void Add(Vector3 Pos, Vector2 UV, Vector3 Normal, Color Clr) {
			Add(new Vertex3(Pos + PosOffset, UVPos + UV * UVSize, Normal, Clr));
		}

		/*public void Add(Vector3 Pos, Vector2 UV, Color Clr) {
			Add(new Vertex3(Pos + PosOffset, UVPos + UV * UVSize, Clr));
		}*/

		/*public Vertex3[] ToArray() {
			return Verts.ToArray();
		}*/

		public Mesh ToMesh() {
			Vertex3[] Vts = Verts.ToArray();

			Mesh M = new Mesh(Vts.Length, Vts.Length / 3);

			M.Vertices = (float*)Marshal.AllocHGlobal(sizeof(Vector3) * Vts.Length);
			M.Normals = (float*)Marshal.AllocHGlobal(sizeof(Vector3) * Vts.Length);
			M.TexCoords = (float*)Marshal.AllocHGlobal(sizeof(Vector2) * Vts.Length);
			M.Indices = (ushort*)Marshal.AllocHGlobal(sizeof(ushort) * Vts.Length);
			M.Colors = (byte*)Marshal.AllocHGlobal(sizeof(Color) * Vts.Length);

			for (int i = 0; i < Vts.Length; i++) {
				M.IndicesAs<ushort>()[i] = (ushort)i;

				M.VerticesAs<Vector3>()[i] = Vts[i].Position;
				M.TexCoordsAs<Vector2>()[i] = Vts[i].UV;
				M.ColorsAs<Color>()[i] = Vts[i].Color;
				M.NormalsAs<Vector3>()[i] = Vts[i].Normal;
			}

			Raylib.UploadMesh(ref M, false);

			return M;
		}
	}
}
