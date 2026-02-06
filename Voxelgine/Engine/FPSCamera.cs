using System;
using System.Numerics;
using Raylib_cs;

namespace Voxelgine.Engine {
	public class FPSCamera {
		const float PlyMoveSen = 0.2f;
		const float FocusDist = 25.0f;

		static readonly Vector3 UpNormal = Vector3.UnitY;
		static readonly Vector3 LeftNormal = Vector3.UnitX;
		static readonly Vector3 ForwardNormal = Vector3.UnitZ;

		public float MouseMoveSen = 0.35f;

		Vector2 MousePrev;
		bool MousePrevInit = false;

		public Vector3 CamAngle;
		public Vector3 Position;

		public FPSCamera(float mouseSensitivity = 0.35f) {
			MouseMoveSen = mouseSensitivity;
		}

		public Vector2 GetPreviousMousePos() {
			return MousePrev;
		}

		public void Update(bool HandleRotation, ref Camera3D Cam, Vector2 mousePos) {
			if (!HandleRotation) {
				mousePos = MousePrev;
			}

			if (!MousePrevInit) {
				MousePrevInit = true;
				MousePrev = mousePos;
			}

			Vector2 MouseDelta = mousePos - MousePrev;
			MousePrev = mousePos;

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
		}

		public Matrix4x4 GetRotationMatrix() {
			Vector3 CamAngleRad = CamAngle * ((float)Math.PI / 180.0f);
			return Matrix4x4.CreateFromYawPitchRoll(CamAngleRad.X, CamAngleRad.Y, CamAngleRad.Z);
		}

		public Vector3 GetForward() {
			return Vector3.Transform(ForwardNormal, GetRotationMatrix());
		}

		public Vector3 GetLeft() {
			return Vector3.Transform(LeftNormal, GetRotationMatrix());
		}

		public Vector3 GetUp() {
			return Vector3.Transform(UpNormal, GetRotationMatrix());
		}

		public void LookAt(Vector3 Target) {
			CamAngle = Utils.EulerBetweenVectors(Position, Target);
		}
	}
}
