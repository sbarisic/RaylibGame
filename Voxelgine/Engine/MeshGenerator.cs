using Raylib_cs;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Voxelgine.Engine {
	unsafe class CustomMaterial {
		public Material Mat;

		public CustomMaterial() {
			//Mat = new Material();
			//Mat.Shader = ResMgr.GetShader("default/default");

			Mesh CubeMesh = Raylib.GenMeshCube(1.0f, 1.0f, 1.0f);
			Model Mdl = Raylib.LoadModelFromMesh(CubeMesh);
			Mat = Mdl.Materials[0];

			Mat.Maps[0].Texture = ResMgr.GetTexture("npc/humanoid.png");
		}
	}

	class CustomMesh {
		public string Name;
		public Vector3 RotationOrigin;

		public CustomMaterial Material;
		public Mesh Mesh;

		public Matrix4x4 Matrix;

		public CustomMesh(Mesh M) {
			Mesh = M;
			Material = new CustomMaterial();
			Matrix = Matrix4x4.Identity;
		}

		public Matrix4x4 GetWorldMatrix(Matrix4x4 Model) {
			Matrix4x4 MeshMat = Matrix4x4.Identity;

			//if (Name != "body") {
			MeshMat = MeshMat * Matrix4x4.CreateTranslation(-RotationOrigin);
			MeshMat = MeshMat * Matrix;
			MeshMat = MeshMat * Matrix4x4.CreateTranslation(RotationOrigin);
			//}

			MeshMat = MeshMat * Model;
			return MeshMat;
		}

		public void Draw(Matrix4x4 Model) {
			Raylib.DrawMesh(Mesh, Material.Mat, Matrix4x4.Transpose(GetWorldMatrix(Model)));
		}
	}

	class CustomModel {
		public Vector3 Position;
		public Vector3 LookDirection;
		public List<CustomMesh> Meshes = new List<CustomMesh>();

		public CustomModel(Vector3 Position, Vector3 LookDirection) {
			this.Position = Position;
			this.LookDirection = Vector3.Normalize(LookDirection);
		}

		public CustomMesh AddMesh(Mesh M) {
			CustomMesh CMesh = new CustomMesh(M);
			Meshes.Add(CMesh);
			return CMesh;
		}

		public RayCollision Collide(Ray R, out CustomMesh HitMesh) {
			Matrix4x4 Model = GetModelMatrix();
			HitMesh = null;

			foreach (CustomMesh CM in Meshes) {
				Matrix4x4 World = CM.GetWorldMatrix(Model);

				RayCollision Col = Raylib.GetRayCollisionMesh(R, CM.Mesh, Matrix4x4.Transpose(World));
				if (Col.Hit) {
					Console.WriteLine("Hit!");

					HitMesh = CM;
					return Col;
				}
			}

			return new RayCollision() { Hit = false };
		}

		Matrix4x4 GetModelMatrix() {
			Vector2 Dir = Vector2.Normalize(new Vector2(LookDirection.X, LookDirection.Z));

			float R = MathF.Sqrt(Dir.X * Dir.X + Dir.Y * Dir.Y);
			float Ang = MathF.Atan2(Dir.X, Dir.Y) + Utils.ToRad(-180);
			//float Ang = Utils.ToRad(45);


			//Console.WriteLine(Utils.ToDeg(Ang));

			Matrix4x4 RotMat = Matrix4x4.CreateRotationY(Ang);

			Matrix4x4 Model = RotMat * Matrix4x4.CreateTranslation(Position);
			return Model;
		}

		public void Draw() {
			Matrix4x4 Model = GetModelMatrix();

			foreach (var Msh in Meshes) {
				Msh.Draw(Model);
			}
		}
	}

	static unsafe class MeshGenerator {
		static Vector2 UVSize;
		static Vector2 UVPos;

		public static Mesh ToMesh(Vertex3[] Vts) {
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

		public static CustomModel Generate(MinecraftModel JMdl) {
			CustomModel CMdl = new CustomModel(Vector3.Zero, new Vector3(1, 0, 1));

			foreach (MinecraftMdlElement E in JMdl.Elements) {
				Vector3 RotOrig = Utils.ToVec3(E.Rotation.Origin) / GlobalScale;

				Vertex3[] ElementVerts = Generate(JMdl, E).ToArray();

				Mesh ElMesh = ToMesh(ElementVerts);
				CustomMesh CMesh = CMdl.AddMesh(ElMesh);
				CMesh.Name = E.Name;
				CMesh.RotationOrigin = RotOrig;
			}

			return CMdl;
		}

		static Vector3 CompassToDir(string SDir) {
			switch (SDir) {
				case "north":
					return new Vector3(0, 0, -1);

				case "east":
					return new Vector3(1, 0, 0);

				case "south":
					return -CompassToDir("north");

				case "west":
					return -CompassToDir("east");

				case "up":
					return new Vector3(0, 1, 0);

				case "down":
					return -CompassToDir("up");

				default:
					throw new NotImplementedException();
			}
		}

		static IEnumerable<Vertex3> Generate(MinecraftModel Mdl, MinecraftMdlElement El) {
			Vector3 From = Utils.ToVec3(El.From);
			Vector3 To = Utils.ToVec3(El.To);
			Vector3 Size = To - From;

			GenDivideUV = Mdl.TextureSize;

			foreach (var Face in El.Faces) {
				Vector3 FaceDir = CompassToDir(Face.Key);

				foreach (var Vert in GenerateCube(From, Size, FaceDir, Utils.ToVec2(Face.Value.UV).ToArray())) {
					yield return Vert;
				}
			}

			GenDivideUV = Vector2.One;
		}

		static Vector3 GlobalScale = new Vector3(16, 16, 16);
		static Vector3 GlobalOffset = new Vector3(-0.5f, 0, -0.5f);

		static Vector3 GenSize = Vector3.Zero;
		static Vector3 GenOffset = Vector3.Zero;
		static Vector2 GenDivideUV = Vector2.One;

		static bool GenUseUVs = false;
		static Vector2 GenUV1 = Vector2.Zero;
		static Vector2 GenUV2 = Vector2.Zero;

		static Vertex3 Gen(Vector3 Pos, Vector2 UV, Vector3 Normal, Color Clr) {

			if (GenUseUVs) {
				float XVal = float.Lerp(GenUV1.X, GenUV2.X, UV.X);
				float YVal = float.Lerp(GenUV2.Y, GenUV1.Y, UV.Y);


				XVal /= GlobalScale.X;
				YVal /= GlobalScale.Y;

				return new Vertex3((GenOffset / GlobalScale) + (Pos * (GenSize / GlobalScale)) + GlobalOffset, new Vector2(XVal, YVal), Normal, Clr);
			} else {
				return new Vertex3((GenOffset / GlobalScale) + (Pos * (GenSize / GlobalScale)) + GlobalOffset, UV, Normal, Clr);
			}
		}

		static void SetBlockTextureUV(Vector3 CurDir, Vector2[] UseUVs) {
			GenUseUVs = UseUVs != null;

			if (GenUseUVs) {
				GenUV1 = UseUVs[0];
				GenUV2 = UseUVs[1];
			}
		}

		public static IEnumerable<Vertex3> GenerateCube(Vector3 Pos, Vector3 Size, Vector3? GenFace = null, Vector2[] UseUVs = null) {
			bool XPosSkipFace = false;
			bool XNegSkipFace = false;
			bool YPosSkipFace = false;
			bool YNegSkipFace = false;
			bool ZPosSkipFace = false;
			bool ZNegSkipFace = false;

			if (GenFace != null) {
				XPosSkipFace = GenFace != new Vector3(1, 0, 0);
				XNegSkipFace = GenFace != new Vector3(-1, 0, 0);
				YPosSkipFace = GenFace != new Vector3(0, 1, 0);
				YNegSkipFace = GenFace != new Vector3(0, -1, 0);
				ZPosSkipFace = GenFace != new Vector3(0, 0, 1);
				ZNegSkipFace = GenFace != new Vector3(0, 0, -1);
			}

			Color FaceClr = Color.White;

			GenSize = Size;
			GenOffset = Pos;

			// X++
			if (!XPosSkipFace) {
				Vector3 CurDir = new Vector3(1, 0, 0);

				SetBlockTextureUV(CurDir, UseUVs);

				yield return Gen(new Vector3(1, 1, 0), new Vector2(1, 1), new Vector3(1, 0, 0), FaceClr);
				yield return Gen(new Vector3(1, 1, 1), new Vector2(0, 1), new Vector3(1, 0, 0), FaceClr);
				yield return Gen(new Vector3(1, 0, 1), new Vector2(0, 0), new Vector3(1, 0, 0), FaceClr);
				yield return Gen(new Vector3(1, 0, 0), new Vector2(1, 0), new Vector3(1, 0, 0), FaceClr);
				yield return Gen(new Vector3(1, 1, 0), new Vector2(1, 1), new Vector3(1, 0, 0), FaceClr);
				yield return Gen(new Vector3(1, 0, 1), new Vector2(0, 0), new Vector3(1, 0, 0), FaceClr);
			}

			// X--
			if (!XNegSkipFace) {
				Vector3 CurDir = new Vector3(-1, 0, 0);

				SetBlockTextureUV(CurDir, UseUVs);

				yield return Gen(new Vector3(0, 1, 1), new Vector2(1, 1), new Vector3(-1, 0, 0), FaceClr);
				yield return Gen(new Vector3(0, 1, 0), new Vector2(0, 1), new Vector3(-1, 0, 0), FaceClr);
				yield return Gen(new Vector3(0, 0, 0), new Vector2(0, 0), new Vector3(-1, 0, 0), FaceClr);
				yield return Gen(new Vector3(0, 0, 1), new Vector2(1, 0), new Vector3(-1, 0, 0), FaceClr);
				yield return Gen(new Vector3(0, 1, 1), new Vector2(1, 1), new Vector3(-1, 0, 0), FaceClr);
				yield return Gen(new Vector3(0, 0, 0), new Vector2(0, 0), new Vector3(-1, 0, 0), FaceClr);
			}

			// Y++
			if (!YPosSkipFace) {
				Vector3 CurDir = new Vector3(0, 1, 0);

				SetBlockTextureUV(CurDir, UseUVs);

				yield return Gen(new Vector3(1, 1, 0), new Vector2(1, 1), new Vector3(0, 1, 0), FaceClr);
				yield return Gen(new Vector3(0, 1, 0), new Vector2(0, 1), new Vector3(0, 1, 0), FaceClr);
				yield return Gen(new Vector3(0, 1, 1), new Vector2(0, 0), new Vector3(0, 1, 0), FaceClr);
				yield return Gen(new Vector3(1, 1, 1), new Vector2(1, 0), new Vector3(0, 1, 0), FaceClr);
				yield return Gen(new Vector3(1, 1, 0), new Vector2(1, 1), new Vector3(0, 1, 0), FaceClr);
				yield return Gen(new Vector3(0, 1, 1), new Vector2(0, 0), new Vector3(0, 1, 0), FaceClr);
			}

			// Y--
			if (!YNegSkipFace) {
				Vector3 CurDir = new Vector3(0, -1, 0);

				SetBlockTextureUV(CurDir, UseUVs);

				yield return Gen(new Vector3(1, 0, 1), new Vector2(0, 0), new Vector3(0, -1, 0), FaceClr);
				yield return Gen(new Vector3(0, 0, 1), new Vector2(1, 0), new Vector3(0, -1, 0), FaceClr);
				yield return Gen(new Vector3(0, 0, 0), new Vector2(1, 1), new Vector3(0, -1, 0), FaceClr);
				yield return Gen(new Vector3(1, 0, 0), new Vector2(0, 1), new Vector3(0, -1, 0), FaceClr);
				yield return Gen(new Vector3(1, 0, 1), new Vector2(0, 0), new Vector3(0, -1, 0), FaceClr);
				yield return Gen(new Vector3(0, 0, 0), new Vector2(1, 1), new Vector3(0, -1, 0), FaceClr);
			}

			// Z++
			if (!ZPosSkipFace) {
				Vector3 CurDir = new Vector3(0, 0, 1);

				SetBlockTextureUV(CurDir, UseUVs);

				yield return Gen(new Vector3(1, 0, 1), new Vector2(1, 0), new Vector3(0, 0, 1), FaceClr);
				yield return Gen(new Vector3(1, 1, 1), new Vector2(1, 1), new Vector3(0, 0, 1), FaceClr);
				yield return Gen(new Vector3(0, 1, 1), new Vector2(0, 1), new Vector3(0, 0, 1), FaceClr);
				yield return Gen(new Vector3(0, 0, 1), new Vector2(0, 0), new Vector3(0, 0, 1), FaceClr);
				yield return Gen(new Vector3(1, 0, 1), new Vector2(1, 0), new Vector3(0, 0, 1), FaceClr);
				yield return Gen(new Vector3(0, 1, 1), new Vector2(0, 1), new Vector3(0, 0, 1), FaceClr);
			}

			// Z--
			if (!ZNegSkipFace) {
				Vector3 CurDir = new Vector3(0, 0, -1);

				SetBlockTextureUV(CurDir, UseUVs);

				yield return Gen(new Vector3(1, 1, 0), new Vector2(0, 1), new Vector3(0, 0, -1), FaceClr);
				yield return Gen(new Vector3(1, 0, 0), new Vector2(0, 0), new Vector3(0, 0, -1), FaceClr);
				yield return Gen(new Vector3(0, 0, 0), new Vector2(1, 0), new Vector3(0, 0, -1), FaceClr);
				yield return Gen(new Vector3(0, 1, 0), new Vector2(1, 1), new Vector3(0, 0, -1), FaceClr);
				yield return Gen(new Vector3(1, 1, 0), new Vector2(0, 1), new Vector3(0, 0, -1), FaceClr);
				yield return Gen(new Vector3(0, 0, 0), new Vector2(1, 0), new Vector3(0, 0, -1), FaceClr);
			}
		}
	}
}
