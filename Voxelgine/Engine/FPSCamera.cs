using Raylib_cs;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

using Voxelgine.Graphics;

namespace Voxelgine.Engine {
	static class FPSCamera {
		//const bool UseCameraMove = false;

		const float MouseMoveSen = 0.5f;
		const float PlyMoveSen = 0.2f;
		const float FocusDist = 25.0f;

		static Vector2 MousePrev;
		static bool MousePrevInit = false;

		public static Vector3 CamAngle;
		public static Vector3 Position;

		static Vector3 UpNormal = Vector3.UnitY;
		static Vector3 LeftNormal = Vector3.UnitX;
		static Vector3 ForwardNormal = Vector3.UnitZ;

		public static Vector2 GetPreviousMousePos() {
			return MousePrev;
		}


		// TODO: Check if this works
		public static Frustum CalcViewFrustum(ref Camera3D Cam) {
			// Implements extraction of frustum planes from the combined view-projection matrix
			// Reference: https://gamedevs.org/uploads/fast-extraction-viewing-frustum-planes-from-world-view-projection-matrix.pdf
			Frustum F = new Frustum();

			// Build view matrix
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
				//float f = 1.0f / MathF.Tan(fovYRad / 2.0f);
				Matrix4x4 proj = Raylib.GetCameraProjectionMatrix(ref Cam, aspect);
				Matrix4x4 vp = view * proj;

				F.Left = new Vector4(vp.M14 + vp.M11, vp.M24 + vp.M21, vp.M34 + vp.M31, vp.M44 + vp.M41);
				F.Right = new Vector4(vp.M14 - vp.M11, vp.M24 - vp.M21, vp.M34 - vp.M31, vp.M44 - vp.M41);
				F.Bottom = new Vector4(vp.M14 + vp.M12, vp.M24 + vp.M22, vp.M34 + vp.M32, vp.M44 + vp.M42);
				F.Top = new Vector4(vp.M14 - vp.M12, vp.M24 - vp.M22, vp.M34 - vp.M32, vp.M44 - vp.M42);
				F.Near = new Vector4(vp.M13, vp.M23, vp.M33, vp.M43);
				F.Far = new Vector4(vp.M14 - vp.M13, vp.M24 - vp.M23, vp.M34 - vp.M33, vp.M44 - vp.M43);
			} else {
				throw new NotImplementedException();
			}

			// Normalize planes
			F.Left = NormalizePlane(F.Left);
			F.Right = NormalizePlane(F.Right);
			F.Bottom = NormalizePlane(F.Bottom);
			F.Top = NormalizePlane(F.Top);
			F.Near = NormalizePlane(F.Near);
			F.Far = NormalizePlane(F.Far);

			return F;
		}

		// Helper to normalize a Vector4 plane (only xyz part)
		static Vector4 NormalizePlane(Vector4 plane) {
			Vector3 normal = new Vector3(plane.X, plane.Y, plane.Z);
			float length = normal.Length();
			return plane / length;
		}


		public static void Update(bool HandleRotation, ref Camera3D Cam) {
			Vector2 MousePos = new Vector2(Raylib.GetMouseX(), Raylib.GetMouseY());

			if (!HandleRotation) {
				MousePos = MousePrev;
			}

			if (!MousePrevInit) {
				MousePrevInit = true;
				MousePrev = MousePos;
			}

			Vector2 MouseDelta = MousePos - MousePrev;
			MousePrev = MousePos;

			CamAngle += new Vector3(-MouseDelta.X, MouseDelta.Y, 0) * MouseMoveSen;

			// Clamps 'nd shit
			CamAngle.X = (float)Utils.NormalizeLoop(CamAngle.X, -360, 360);
			CamAngle.Y = (float)Utils.NormalizeLoop(CamAngle.Y, -360, 360);
			CamAngle.Z = (float)Utils.NormalizeLoop(CamAngle.Z, -360, 360);

			if (CamAngle.Y > 89.9f)
				CamAngle.Y = 89.9f;

			if (CamAngle.Y < -89.9f)
				CamAngle.Y = -89.9f;

			Vector3 Forward = GetForward();

			Cam.Position = Position;
			Cam.Target = Position + (Forward * FocusDist);

			Frustum F = CalcViewFrustum(ref Cam);
			Console.WriteLine(F.ToString());
		}

		public static Matrix4x4 GetRotationMatrix() {
			Vector3 CamAngleRad = CamAngle * ((float)Math.PI / 180.0f);
			return Matrix4x4.CreateFromYawPitchRoll(CamAngleRad.X, CamAngleRad.Y, CamAngleRad.Z);
		}

		public static Vector3 GetForward() {
			return Vector3.Transform(ForwardNormal, GetRotationMatrix());
		}

		public static Vector3 GetLeft() {
			return Vector3.Transform(LeftNormal, GetRotationMatrix());
		}

		public static Vector3 GetUp() {
			return Vector3.Transform(UpNormal, GetRotationMatrix());
		}

		public static void LookAt(Vector3 Target) {
			CamAngle = Utils.EulerBetweenVectors(Position, Target);
		}
	}
}
