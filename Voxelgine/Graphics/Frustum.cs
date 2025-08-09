using Raylib_cs;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Voxelgine.Graphics {
	public struct Frustum {
		public Vector4 Left;
		public Vector4 Right;
		public Vector4 Top;
		public Vector4 Bottom;
		public Vector4 Near;
		public Vector4 Far;

		public Vector3[] Corners;

		public Frustum(ref Camera3D Cam) {
			Vector3 up = Raylib.GetCameraUp(ref Cam);
			Vector3 forward = Raylib.GetCameraForward(ref Cam);
			Vector3 right = Raylib.GetCameraRight(ref Cam);

			//Matrix4x4 view = Matrix4x4.CreateLookAt(Cam.Position, Cam.Target, camUp);
			Matrix4x4 view = Raylib.GetCameraMatrix(Cam);

			// Build projection matrix
			float fovYRad = Cam.FovY * MathF.PI / 180.0f;
			float aspect = 16.0f / 9.0f; // TODO: Use actual aspect ratio if available
			float near = 0.01f; // Near plane
			float far = 1000.0f; // Far plane


			if (Cam.Projection == CameraProjection.Perspective) {
				Matrix4x4 proj = Raylib.GetCameraProjectionMatrix(ref Cam, aspect);
				Matrix4x4 vp = proj * view; // Correct order: projection * view

				// Extract planes using correct elements (row-major)
				Left = new Vector4(vp.M41 + vp.M11, vp.M42 + vp.M12, vp.M43 + vp.M13, vp.M44 + vp.M14);
				Right = new Vector4(vp.M41 - vp.M11, vp.M42 - vp.M12, vp.M43 - vp.M13, vp.M44 - vp.M14);
				Bottom = new Vector4(vp.M41 + vp.M21, vp.M42 + vp.M22, vp.M43 + vp.M23, vp.M44 + vp.M24);
				Top = new Vector4(vp.M41 - vp.M21, vp.M42 - vp.M22, vp.M43 - vp.M23, vp.M44 - vp.M24);
				Near = new Vector4(vp.M41 + vp.M31, vp.M42 + vp.M32, vp.M43 + vp.M33, vp.M44 + vp.M34);
				Far = new Vector4(vp.M41 - vp.M31, vp.M42 - vp.M32, vp.M43 - vp.M33, vp.M44 - vp.M34);
			} else {
				throw new NotImplementedException();
			}

			// Normalize planes
			Left = Utils.NormalizePlane(Left);
			Right = Utils.NormalizePlane(Right);
			Bottom = Utils.NormalizePlane(Bottom);
			Top = Utils.NormalizePlane(Top);
			Near = Utils.NormalizePlane(Near);
			Far = Utils.NormalizePlane(Far);

			Update();
		}

		public void Update() {
			// Calculate the 8 corners of the frustum by intersecting 3 planes at a time
			// The planes are: Left, Right, Top, Bottom, Near, Far
			// The corners are:
			// Near plane: left-top-near, right-top-near, right-bottom-near, left-bottom-near
			// Far plane: left-top-far, right-top-far, right-bottom-far, left-bottom-far

			Corners = new Vector3[8];
			// Near plane
			Corners[0] = IntersectPlanes(Near, Left, Top);    // left-top-near
			Corners[1] = IntersectPlanes(Near, Right, Top);   // right-top-near
			Corners[2] = IntersectPlanes(Near, Right, Bottom);// right-bottom-near
			Corners[3] = IntersectPlanes(Near, Left, Bottom); // left-bottom-near
															  // Far plane
			Corners[4] = IntersectPlanes(Far, Left, Top);     // left-top-far
			Corners[5] = IntersectPlanes(Far, Right, Top);    // right-top-far
			Corners[6] = IntersectPlanes(Far, Right, Bottom); // right-bottom-far
			Corners[7] = IntersectPlanes(Far, Left, Bottom);  // left-bottom-far
		}

		public bool IsInside(Vector3 point) {
			// TODO: Implement point-in-frustum check

			return false;
		}

		public override string ToString() {
			return $"(L: {Left}; R: {Right}; T: {Top}; B: {Bottom}; N: {Near}; F: {Far};)";
		}

		static Vector3 IntersectPlanes(Vector4 p1, Vector4 p2, Vector4 p3) {
			// Ax + By + Cz + D = 0
			// See: https://stackoverflow.com/questions/2824478/shortest-distance-between-two-lines-in-3d
			// and https://github.com/erich666/GraphicsGems/blob/master/gems/Frustum.c
			Vector3 n1 = new Vector3(p1.X, p1.Y, p1.Z);
			Vector3 n2 = new Vector3(p2.X, p2.Y, p2.Z);
			Vector3 n3 = new Vector3(p3.X, p3.Y, p3.Z);
			float d1 = p1.W;
			float d2 = p2.W;
			float d3 = p3.W;
			Vector3 cross23 = Vector3.Cross(n2, n3);
			Vector3 cross31 = Vector3.Cross(n3, n1);
			Vector3 cross12 = Vector3.Cross(n1, n2);
			float denom = Vector3.Dot(n1, cross23);
			if (Math.Abs(denom) < 1e-6f)
				return Vector3.Zero; // Planes are parallel or degenerate
			Vector3 result = (-d1 * cross23 - d2 * cross31 - d3 * cross12) / denom;
			return result;
		}

		public void Draw() {
			// Draw frustum edges
			Color color = Color.Pink;
			// Near plane edges
			Raylib.DrawLine3D(Corners[0], Corners[1], color);
			Raylib.DrawLine3D(Corners[1], Corners[2], color);
			Raylib.DrawLine3D(Corners[2], Corners[3], color);
			Raylib.DrawLine3D(Corners[3], Corners[0], color);
			// Far plane edges
			Raylib.DrawLine3D(Corners[4], Corners[5], color);
			Raylib.DrawLine3D(Corners[5], Corners[6], color);
			Raylib.DrawLine3D(Corners[6], Corners[7], color);
			Raylib.DrawLine3D(Corners[7], Corners[4], color);
			// Connect near and far planes
			Raylib.DrawLine3D(Corners[0], Corners[4], color);
			Raylib.DrawLine3D(Corners[1], Corners[5], color);
			Raylib.DrawLine3D(Corners[2], Corners[6], color);
			Raylib.DrawLine3D(Corners[3], Corners[7], color);
		}
	}
}
