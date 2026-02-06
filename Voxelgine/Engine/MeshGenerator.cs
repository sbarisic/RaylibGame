using Raylib_cs;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Voxelgine.Engine.DI;

namespace Voxelgine.Engine {
	/// <summary>
	/// Material wrapper for custom models.
	/// </summary>
	public unsafe class CustomMaterial {
		public Material Mat;

		public CustomMaterial() {
			Mesh CubeMesh = Raylib.GenMeshCube(1.0f, 1.0f, 1.0f);
			Model Mdl = Raylib.LoadModelFromMesh(CubeMesh);
			Mat = Mdl.Materials[0];

			Mat.Maps[0].Texture = ResMgr.GetTexture("npc/humanoid.png");
		}
	}

	/// <summary>
	/// A single mesh element within a CustomModel. Supports animation transforms and parent-child hierarchy.
	/// </summary>
	public class CustomMesh {
		public string Name;
		public Vector3 RotationOrigin;

		public CustomMaterial Material;
		public Mesh Mesh;
		public BoundingBox BBox;

		public Matrix4x4 Matrix;

		// Parent-child hierarchy for attachment points
		/// <summary>Parent mesh that this mesh is attached to. Null if root element.</summary>
		public CustomMesh Parent;
		/// <summary>Attachment point offset relative to parent's rotation origin.</summary>
		public Vector3 AttachmentPoint;

		// Model base pose (from JSON)
		/// <summary>Base rotation from model definition (angle around single axis, stored as degrees).</summary>
		public Vector3 BaseRotation;

		// Animation properties
		/// <summary>Current animation rotation in degrees (X=pitch, Y=yaw, Z=roll).</summary>
		public Vector3 AnimationRotation;
		/// <summary>Current animation position offset.</summary>
		public Vector3 AnimationPosition;
		/// <summary>Rotation-only animation matrix (used for hierarchy propagation).</summary>
		public Matrix4x4 AnimationRotationMatrix = Matrix4x4.Identity;
		/// <summary>Combined animation matrix including rotation and translation.</summary>
		public Matrix4x4 AnimationMatrix = Matrix4x4.Identity;

		public CustomMesh(Mesh M) {
			Mesh = M;
			Material = new CustomMaterial();
			Matrix = Matrix4x4.Identity;
			BaseRotation = Vector3.Zero;
			AnimationRotation = Vector3.Zero;
			AnimationPosition = Vector3.Zero;
			AnimationRotationMatrix = Matrix4x4.Identity;
			Parent = null;
			AttachmentPoint = Vector3.Zero;

			BBox = Raylib.GetMeshBoundingBox(M);
		}

		/// <summary>
		/// Sets the parent mesh for this element, establishing attachment hierarchy.
		/// </summary>
		public void SetParent(CustomMesh parent, Vector3 attachmentPoint = default) {
			Parent = parent;
			AttachmentPoint = attachmentPoint;
		}

		/// <summary>
		/// Updates the animation matrix from the current AnimationRotation and AnimationPosition.
		/// Composites base rotation (from model) with animation rotation.
		/// </summary>
		public void UpdateAnimationMatrix() {
			// Base rotation defines the model's rest pose as designed in Blockbench - keep as-is
			// Animation rotation needs negation to correct for Z-axis orientation difference
			// between Minecraft model format (+Z = south/forward) and engine (+Z = north/backward)
			float pitch = Utils.ToRad(BaseRotation.X - AnimationRotation.X);
			float yaw = Utils.ToRad(BaseRotation.Y + AnimationRotation.Y);
			float roll = Utils.ToRad(BaseRotation.Z + AnimationRotation.Z);

			// Build rotation-only matrix (used for hierarchy propagation)
			AnimationRotationMatrix = Matrix4x4.CreateRotationX(pitch) *
									  Matrix4x4.CreateRotationY(yaw) *
									  Matrix4x4.CreateRotationZ(roll);

			// Build full animation matrix (rotation + translation)
			AnimationMatrix = AnimationRotationMatrix * Matrix4x4.CreateTranslation(AnimationPosition);
		}

		/// <summary>
		/// Gets the combined animation matrix including all parent transforms.
		/// Each rotation is properly applied around its respective pivot point.
		/// </summary>
		public Matrix4x4 GetCombinedAnimationMatrix() {
			// This mesh's animation around its own pivot point
			Matrix4x4 localAnimAroundPivot = 
				Matrix4x4.CreateTranslation(-RotationOrigin) * 
				AnimationRotationMatrix * 
				Matrix4x4.CreateTranslation(RotationOrigin) *
				Matrix4x4.CreateTranslation(AnimationPosition);

			if (Parent == null) {
				return localAnimAroundPivot;
			}

			// Parent's full transform (recursive - includes all ancestor pivots)
			Matrix4x4 parentCombined = Parent.GetCombinedAnimationMatrix();

			// First apply local animation around local pivot,
			// then parent's animation moves us around parent's pivot
			return localAnimAroundPivot * parentCombined;
		}

		public Matrix4x4 GetWorldMatrix(Matrix4x4 Model) {
			// GetCombinedAnimationMatrix now handles all pivot/rotation properly
			// Just apply base matrix and model transform
			Matrix4x4 MeshMat = Matrix * GetCombinedAnimationMatrix() * Model;
			return MeshMat;
		}

		public void Draw(Matrix4x4 Model) {
			Raylib.DrawMesh(Mesh, Material.Mat, Matrix4x4.Transpose(GetWorldMatrix(Model)));
		}
	}

	/// <summary>
	/// A model composed of multiple CustomMesh elements, typically loaded from JSON.
	/// Supports animation via NPCAnimator.
	/// </summary>
	public class CustomModel {
		public static IFishLogging Logging;
		public Vector3 Position;
		public Vector3 LookDirection;
		public List<CustomMesh> Meshes = new List<CustomMesh>();

		public CustomModel(Vector3 Position, Vector3 LookDirection) {
			this.Position = Position;
			this.LookDirection = Vector3.Normalize(LookDirection);
		}

		/// <summary>
		/// Finds a mesh by its element name.
		/// </summary>
		public CustomMesh GetMeshByName(string name) {
			foreach (var mesh in Meshes) {
				if (mesh.Name == name)
					return mesh;
			}
			return null;
		}

		public BoundingBox GetBoundingBox() {
			if (Meshes.Count == 0)
				return new BoundingBox();

			Vector3 min = Meshes[0].BBox.Min;
			Vector3 max = Meshes[0].BBox.Max;

			for (int i = 1; i < Meshes.Count; i++) {
				var bbox = Meshes[i].BBox;
				min = Vector3.Min(min, bbox.Min);
				max = Vector3.Max(max, bbox.Max);
			}


			min += Position;
			max += Position;
			return new BoundingBox(min, max);
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
					Logging?.WriteLine("Hit!");

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

		/// <summary>
		/// Sets up parent-child relationships between mesh elements.
		/// Call this after loading the model to establish attachment hierarchy.
		/// </summary>
		/// <param name="parentName">Name of the parent element.</param>
		/// <param name="childNames">Names of elements that should be children of the parent.</param>
		public void SetupHierarchy(string parentName, params string[] childNames) {
			var parent = GetMeshByName(parentName);
			if (parent == null) return;

			foreach (var childName in childNames) {
				var child = GetMeshByName(childName);
				if (child != null) {
					child.SetParent(parent);
				}
			}
		}

		/// <summary>
		/// Sets up the standard humanoid skeleton hierarchy:
		/// - body is the root
		/// - head, hand_l, hand_r are attached to body
		/// - leg_l, leg_r are attached to body
		/// </summary>
		public void SetupHumanoidHierarchy() {
			// Body is the root - everything attaches to it
			SetupHierarchy("body", "head", "hand_l", "hand_r", "leg_l", "leg_r");
		}
	}

	static unsafe class MeshGenerator {
		//static Vector2 UVSize;
		//static Vector2 UVPos;

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
				// Rotation origin must be transformed the same way as vertices:
				// divide by GlobalScale AND add GlobalOffset to match vertex positions
				Vector3 RotOrig = Utils.ToVec3(E.Rotation.Origin) / GlobalScale + GlobalOffset;

				// Convert model's single-axis rotation to Vector3
				Vector3 baseRotation = AxisAngleToVector3(E.Rotation.Axis, E.Rotation.Angle);

				Vertex3[] ElementVerts = Generate(JMdl, E).ToArray();

				Mesh ElMesh = ToMesh(ElementVerts);
				CustomMesh CMesh = CMdl.AddMesh(ElMesh);
				CMesh.Name = E.Name;
				CMesh.RotationOrigin = RotOrig;
				CMesh.BaseRotation = baseRotation;
				CMesh.UpdateAnimationMatrix(); // Apply base rotation immediately
			}

			return CMdl;
		}

		/// <summary>
		/// Converts Minecraft's single-axis rotation (axis name + angle) to a Vector3 (pitch, yaw, roll) in degrees.
		/// </summary>
		static Vector3 AxisAngleToVector3(string axis, float angle) {
			return axis?.ToLowerInvariant() switch {
				"x" => new Vector3(angle, 0, 0),  // Pitch
				"y" => new Vector3(0, angle, 0),  // Yaw
				"z" => new Vector3(0, 0, angle),  // Roll
				_ => Vector3.Zero
			};
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
				// UV coordinates from JSON are [u1, v1, u2, v2] in Minecraft's 0-16 UV space
				// GenUV1 = (u1, v1) = top-left corner of texture region
				// GenUV2 = (u2, v2) = bottom-right corner of texture region
				// Lerp maps vertex UV (0-1) to the actual texture region
				float XVal = float.Lerp(GenUV1.X, GenUV2.X, UV.X);
				float YVal = float.Lerp(GenUV1.Y, GenUV2.Y, UV.Y);

				// Normalize UVs - Minecraft UV coordinates are in 0-16 range, convert to 0-1
				XVal /= 16.0f;
				YVal /= 16.0f;

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

			// X++ (east face) - viewed from +X looking at -X
			if (!XPosSkipFace) {
				Vector3 CurDir = new Vector3(1, 0, 0);

				SetBlockTextureUV(CurDir, UseUVs);

				yield return Gen(new Vector3(1, 1, 0), new Vector2(1, 0), new Vector3(1, 0, 0), FaceClr);
				yield return Gen(new Vector3(1, 1, 1), new Vector2(0, 0), new Vector3(1, 0, 0), FaceClr);
				yield return Gen(new Vector3(1, 0, 1), new Vector2(0, 1), new Vector3(1, 0, 0), FaceClr);
				yield return Gen(new Vector3(1, 0, 0), new Vector2(1, 1), new Vector3(1, 0, 0), FaceClr);
				yield return Gen(new Vector3(1, 1, 0), new Vector2(1, 0), new Vector3(1, 0, 0), FaceClr);
				yield return Gen(new Vector3(1, 0, 1), new Vector2(0, 1), new Vector3(1, 0, 0), FaceClr);
			}

			// X-- (west face) - viewed from -X looking at +X
			if (!XNegSkipFace) {
				Vector3 CurDir = new Vector3(-1, 0, 0);

				SetBlockTextureUV(CurDir, UseUVs);

				yield return Gen(new Vector3(0, 1, 1), new Vector2(1, 0), new Vector3(-1, 0, 0), FaceClr);
				yield return Gen(new Vector3(0, 1, 0), new Vector2(0, 0), new Vector3(-1, 0, 0), FaceClr);
				yield return Gen(new Vector3(0, 0, 0), new Vector2(0, 1), new Vector3(-1, 0, 0), FaceClr);
				yield return Gen(new Vector3(0, 0, 1), new Vector2(1, 1), new Vector3(-1, 0, 0), FaceClr);
				yield return Gen(new Vector3(0, 1, 1), new Vector2(1, 0), new Vector3(-1, 0, 0), FaceClr);
				yield return Gen(new Vector3(0, 0, 0), new Vector2(0, 1), new Vector3(-1, 0, 0), FaceClr);
			}

			// Y++ (up face) - viewed from above
			if (!YPosSkipFace) {
				Vector3 CurDir = new Vector3(0, 1, 0);

				SetBlockTextureUV(CurDir, UseUVs);

				yield return Gen(new Vector3(1, 1, 0), new Vector2(1, 0), new Vector3(0, 1, 0), FaceClr);
				yield return Gen(new Vector3(0, 1, 0), new Vector2(0, 0), new Vector3(0, 1, 0), FaceClr);
				yield return Gen(new Vector3(0, 1, 1), new Vector2(0, 1), new Vector3(0, 1, 0), FaceClr);
				yield return Gen(new Vector3(1, 1, 1), new Vector2(1, 1), new Vector3(0, 1, 0), FaceClr);
				yield return Gen(new Vector3(1, 1, 0), new Vector2(1, 0), new Vector3(0, 1, 0), FaceClr);
				yield return Gen(new Vector3(0, 1, 1), new Vector2(0, 1), new Vector3(0, 1, 0), FaceClr);
			}

			// Y-- (down face) - viewed from below
			if (!YNegSkipFace) {
				Vector3 CurDir = new Vector3(0, -1, 0);

				SetBlockTextureUV(CurDir, UseUVs);

				yield return Gen(new Vector3(1, 0, 1), new Vector2(1, 0), new Vector3(0, -1, 0), FaceClr);
				yield return Gen(new Vector3(0, 0, 1), new Vector2(0, 0), new Vector3(0, -1, 0), FaceClr);
				yield return Gen(new Vector3(0, 0, 0), new Vector2(0, 1), new Vector3(0, -1, 0), FaceClr);
				yield return Gen(new Vector3(1, 0, 0), new Vector2(1, 1), new Vector3(0, -1, 0), FaceClr);
				yield return Gen(new Vector3(1, 0, 1), new Vector2(1, 0), new Vector3(0, -1, 0), FaceClr);
				yield return Gen(new Vector3(0, 0, 0), new Vector2(0, 1), new Vector3(0, -1, 0), FaceClr);
			}

			// Z++ (south face) - viewed from +Z looking at -Z
			if (!ZPosSkipFace) {
				Vector3 CurDir = new Vector3(0, 0, 1);

				SetBlockTextureUV(CurDir, UseUVs);

				yield return Gen(new Vector3(1, 0, 1), new Vector2(0, 1), new Vector3(0, 0, 1), FaceClr);
				yield return Gen(new Vector3(1, 1, 1), new Vector2(0, 0), new Vector3(0, 0, 1), FaceClr);
				yield return Gen(new Vector3(0, 1, 1), new Vector2(1, 0), new Vector3(0, 0, 1), FaceClr);
				yield return Gen(new Vector3(0, 0, 1), new Vector2(1, 1), new Vector3(0, 0, 1), FaceClr);
				yield return Gen(new Vector3(1, 0, 1), new Vector2(0, 1), new Vector3(0, 0, 1), FaceClr);
				yield return Gen(new Vector3(0, 1, 1), new Vector2(1, 0), new Vector3(0, 0, 1), FaceClr);
			}

			// Z-- (north face) - viewed from -Z looking at +Z
			if (!ZNegSkipFace) {
				Vector3 CurDir = new Vector3(0, 0, -1);

				SetBlockTextureUV(CurDir, UseUVs);

				yield return Gen(new Vector3(1, 1, 0), new Vector2(0, 0), new Vector3(0, 0, -1), FaceClr);
				yield return Gen(new Vector3(1, 0, 0), new Vector2(0, 1), new Vector3(0, 0, -1), FaceClr);
				yield return Gen(new Vector3(0, 0, 0), new Vector2(1, 1), new Vector3(0, 0, -1), FaceClr);
				yield return Gen(new Vector3(0, 1, 0), new Vector2(1, 0), new Vector3(0, 0, -1), FaceClr);
				yield return Gen(new Vector3(1, 1, 0), new Vector2(0, 0), new Vector3(0, 0, -1), FaceClr);
				yield return Gen(new Vector3(0, 0, 0), new Vector2(1, 1), new Vector3(0, 0, -1), FaceClr);
			}
		}
	}
}
