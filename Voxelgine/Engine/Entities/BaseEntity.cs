using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

using Raylib_cs;

namespace Voxelgine.Engine {
	class BaseEntity : IEntity {
		Vector3 Position;
		Vector3 Size;

		public virtual void UpdateLockstep(float TotalTime, float Dt, InputMgr InMgr) {
		}

		public virtual void Draw3D(float TimeAlpha, ref GameFrameInfo LastFrame) {
			DrawPlayerCollisionBox(Position);
		}

		 void DrawPlayerCollisionBox(Vector3 feetPos) {
			float playerRadius = Player.PlayerRadius;
			float playerHeight = Player.PlayerHeight;
			Vector3 min = new Vector3(feetPos.X - playerRadius, feetPos.Y, feetPos.Z - playerRadius);
			Vector3 max = new Vector3(feetPos.X + playerRadius, feetPos.Y + playerHeight, feetPos.Z + playerRadius);
			Color color = Color.Red;
			Vector3[] corners = new Vector3[8];
			corners[0] = new Vector3(min.X, min.Y, min.Z);
			corners[1] = new Vector3(max.X, min.Y, min.Z);
			corners[2] = new Vector3(max.X, min.Y, max.Z);
			corners[3] = new Vector3(min.X, min.Y, max.Z);
			corners[4] = new Vector3(min.X, max.Y, min.Z);
			corners[5] = new Vector3(max.X, max.Y, min.Z);
			corners[6] = new Vector3(max.X, max.Y, max.Z);
			corners[7] = new Vector3(min.X, max.Y, max.Z);
			Raylib.DrawLine3D(corners[0], corners[1], color);
			Raylib.DrawLine3D(corners[1], corners[2], color);
			Raylib.DrawLine3D(corners[2], corners[3], color);
			Raylib.DrawLine3D(corners[3], corners[0], color);
			Raylib.DrawLine3D(corners[4], corners[5], color);
			Raylib.DrawLine3D(corners[5], corners[6], color);
			Raylib.DrawLine3D(corners[6], corners[7], color);
			Raylib.DrawLine3D(corners[7], corners[4], color);
			Raylib.DrawLine3D(corners[0], corners[4], color);
			Raylib.DrawLine3D(corners[1], corners[5], color);
			Raylib.DrawLine3D(corners[2], corners[6], color);
			Raylib.DrawLine3D(corners[3], corners[7], color);
		}

		public Vector3 GetPosition() {
			return Position;
		}

		public void SetPosition(Vector3 Pos) {
			Position = Pos;
		}

		public Vector3 GetSize() {
			return Size;
		}

		public void SetSize(Vector3 Size) {
			this.Size = Size;
		}
	}
}
