using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

using Raylib_cs;

using RaylibGame.States;

using Voxelgine.Graphics;

namespace Voxelgine.Engine {
	class BaseEntity : IEntity {
		Vector3 Position;
		Vector3 Size;
		Vector3 Velocity;

		public virtual void UpdateLockstep(float TotalTime, float Dt, InputMgr InMgr) {
			GameState GS = GetGameState();
			UpdatePhysics(GS.Map, Dt);
		}

		public virtual void Draw3D(float TimeAlpha, ref GameFrameInfo LastFrame) {
			DrawCollisionBox();
		}

		// Draws the collision box at Position with Size
		void DrawCollisionBox() {
			Vector3 min = Position;
			Vector3 max = Position + Size;
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

		// Applies simple physics: gravity, velocity integration, and block collision (AABB sweep, no input)
		// Also checks for collision with the player and triggers OnPlayerTouch only once per entry
		private bool wasPlayerTouching = false;
		public virtual void UpdatePhysics(ChunkMap map, float Dt) {
			GameState GS = GetGameState();

			const float Gravity = 9.81f;
			// Apply gravity
			Velocity.Y -= Gravity * Dt;
			// Try to move entity by velocity, axis by axis (AABB sweep)
			Vector3 newPos = Position;
			Vector3 move = Velocity * Dt;
			// X axis
			if (!HasBlocksInBounds(map, new Vector3(newPos.X + move.X, newPos.Y, newPos.Z), Size))
				newPos.X += move.X;
			else
				Velocity.X = 0;
			// Y axis
			if (!HasBlocksInBounds(map, new Vector3(newPos.X, newPos.Y + move.Y, newPos.Z), Size))
				newPos.Y += move.Y;
			else {
				Velocity.Y = 0;
			}
			// Z axis
			if (!HasBlocksInBounds(map, new Vector3(newPos.X, newPos.Y, newPos.Z + move.Z), Size))
				newPos.Z += move.Z;
			else
				Velocity.Z = 0;

			Position = newPos;

			// --- Player collision check ---
			if (GS != null && GS.Ply != null) {
				// Player AABB
				Vector3 playerFeet = GS.Ply.Position - new Vector3(0, Player.PlayerEyeOffset, 0);
				Vector3 playerMin = new Vector3(
					playerFeet.X - Player.PlayerRadius,
					playerFeet.Y,
					playerFeet.Z - Player.PlayerRadius
				);
				Vector3 playerMax = new Vector3(
					playerFeet.X + Player.PlayerRadius,
					playerFeet.Y + Player.PlayerHeight,
					playerFeet.Z + Player.PlayerRadius
				);
				// Entity AABB
				Vector3 entMin = Position;
				Vector3 entMax = Position + Size;
				bool touching =
					entMin.X <= playerMax.X && entMax.X >= playerMin.X &&
					entMin.Y <= playerMax.Y && entMax.Y >= playerMin.Y &&
					entMin.Z <= playerMax.Z && entMax.Z >= playerMin.Z;
				if (touching && !wasPlayerTouching) {
					OnPlayerTouch(GS.Ply);
					wasPlayerTouching = true;
				} else if (!touching) {
					wasPlayerTouching = false;
				}
			}
		}

		// Checks if any blocks are present in the AABB at pos with size
		bool HasBlocksInBounds(ChunkMap map, Vector3 pos, Vector3 size) {
			Vector3 min = pos;
			Vector3 max = pos + size;
			for (int x = (int)MathF.Floor(min.X); x <= (int)MathF.Floor(max.X); x++)
				for (int y = (int)MathF.Floor(min.Y); y <= (int)MathF.Floor(max.Y); y++)
					for (int z = (int)MathF.Floor(min.Z); z <= (int)MathF.Floor(max.Z); z++)
						if (map.GetBlock(x, y, z) != BlockType.None)
							return true;
			return false;
		}

		GameState GameState;

		public GameState GetGameState() {
			return GameState;
		}

		public void SetGameState(GameState State) {
			GameState = State;
		}

		public void OnPlayerTouch(Player Ply) {
			Console.WriteLine("Player touched me!");
		}

		EntityManager EntMgr;

		public EntityManager GetEntityManager() {
			return EntMgr;
		}

		public void SetEntityManager(EntityManager EntMgr) {
			this.EntMgr = EntMgr;
		}
	}
}
