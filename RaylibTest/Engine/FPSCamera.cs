using RaylibSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace RaylibTest.Engine {
	static class FPSCamera {
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

		public static void Update(ref Camera3D Cam) {
			Vector2 MousePos = new Vector2(Raylib.GetMouseX(), Raylib.GetMouseY());

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
			Vector3 Left = GetLeft();
			Vector3 Up = GetUp();

			if (Raylib.IsKeyDown('W'))
				Position += Forward * PlyMoveSen;
			if (Raylib.IsKeyDown('S'))
				Position -= Forward * PlyMoveSen;

			if (Raylib.IsKeyDown('A'))
				Position += Left * PlyMoveSen;
			if (Raylib.IsKeyDown('D'))
				Position -= Left * PlyMoveSen;

			if (Raylib.IsKeyDown(' '))
				Position += Up * PlyMoveSen;
			if (Raylib.IsKeyDown('C'))
				Position -= Up * PlyMoveSen;

			Cam.position = Position;
			Cam.target = Position + (Forward * FocusDist);

			//Vector4 RES = RotMat * new Vector4(0, 0, 0, 0);

			//Console.WriteLine("new Vector3({0}, {1}, {2})", (int)Position.X, (int)Position.Y, (int)Position.Z);
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

		// TODO
		/*public static void LookAt(Vector3 Target) {
			CamAngle = Utils.EulerBetweenVectors(Position, Target);
		}*/
	}
}
