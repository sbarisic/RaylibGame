using Raylib_cs;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

using Voxelgine.Graphics;

using Windows.UI.WebUI;

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

			//Frustum F = new Frustum(ref Cam);
			//Console.WriteLine(F.ToString());
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
