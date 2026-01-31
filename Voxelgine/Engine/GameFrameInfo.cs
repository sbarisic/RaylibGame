using Raylib_cs;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

using Voxelgine.Graphics;

namespace Voxelgine.Engine {
	public struct GameFrameInfo {
		public bool Empty;
		public Camera3D Cam;
		public Vector3 Pos;
		public Vector3 CamAngle;
		public Quaternion ViewModelRot;
		public Vector3 ViewModelOffset; // Offset from camera, not absolute position
		public Vector3 FeetPosition;
		public Frustum Frustum;

		public GameFrameInfo() {
			Empty = true;
		}

		/// <summary>
		/// Interpolates an angle handling wrapping (for angles in degrees)
		/// </summary>
		static float LerpAngle(float from, float to, float t) {
			float diff = to - from;

			// Handle angle wrapping - take the shortest path
			while (diff > 180f) diff -= 360f;
			while (diff < -180f) diff += 360f;

			return from + diff * t;
		}

		public GameFrameInfo Interpolate(GameFrameInfo Old, float T) {
			// State = CurrentState * TimeAlpha + PreviousState * (1.0f - TimeAlpha);

			GameFrameInfo New = new GameFrameInfo();

			New.Cam.FovY = float.Lerp(Old.Cam.FovY, Cam.FovY, T);
			New.Cam.Position = Vector3.Lerp(Old.Cam.Position, Cam.Position, T);
			New.Cam.Target = Vector3.Lerp(Old.Cam.Target, Cam.Target, T);
			New.Cam.Up = Vector3.Lerp(Old.Cam.Up, Cam.Up, T);
			New.Cam.Projection = Cam.Projection;
			New.Pos = Vector3.Lerp(Old.Pos, Pos, T);

			// Use angle-aware interpolation for camera angles to handle wrapping
			New.CamAngle = new Vector3(
				LerpAngle(Old.CamAngle.X, CamAngle.X, T),
				LerpAngle(Old.CamAngle.Y, CamAngle.Y, T),
				LerpAngle(Old.CamAngle.Z, CamAngle.Z, T)
			);

			New.ViewModelOffset = Vector3.Lerp(Old.ViewModelOffset, ViewModelOffset, T);
			New.ViewModelRot = Quaternion.Slerp(Old.ViewModelRot, ViewModelRot, T);
			New.FeetPosition = Vector3.Lerp(Old.FeetPosition, FeetPosition, T);
			New.Frustum = Frustum;

			return New;
		}

		public static Camera3D Lerp(Camera3D Old, Camera3D Cam, float T) {
			Camera3D New = new Camera3D();

			New.Projection = Cam.Projection;
			New.FovY = float.Lerp(Old.FovY, Cam.FovY, T);
			New.Position = Vector3.Lerp(Old.Position, Cam.Position, T);
			New.Target = Vector3.Lerp(Old.Target, Cam.Target, T);
			New.Up = Cam.Up;

			return New;
		}
	}

}
