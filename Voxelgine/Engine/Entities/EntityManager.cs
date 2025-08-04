using RaylibGame.States;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

using Voxelgine.Graphics;

namespace Voxelgine.Engine {
	public class EntityManager {
		List<VoxEntity> Entities;

		public EntityManager() {
			Entities = new List<VoxEntity>();
		}

		public void Spawn(GameState GState, VoxEntity Ent) {
			if (Ent == null)
				return;

			Ent.SetEntityManager(this);
			Ent.SetGameState(GState);
			Entities.Add(Ent);
		}

		void UpdateEntityPhysics(VoxEntity Ent, float Dt) {
			GameState GS = Ent.GetGameState();
			ChunkMap map = GS.Map;

			const float Gravity = 9.81f;
			// Apply gravity
			Ent.Velocity.Y -= Gravity * Dt;
			// Try to move entity by velocity, axis by axis (AABB sweep)
			Vector3 newPos = Ent.Position;
			Vector3 move = Ent.Velocity * Dt;
			// X axis
			if (!map.HasBlocksInBounds(new Vector3(newPos.X + move.X, newPos.Y, newPos.Z), Ent.Size))
				newPos.X += move.X;
			else
				Ent.Velocity.X = 0;
			// Y axis
			if (!map.HasBlocksInBounds(new Vector3(newPos.X, newPos.Y + move.Y, newPos.Z), Ent.Size))
				newPos.Y += move.Y;
			else {
				Ent.Velocity.Y = 0;
			}
			// Z axis
			if (!map.HasBlocksInBounds(new Vector3(newPos.X, newPos.Y, newPos.Z + move.Z), Ent.Size))
				newPos.Z += move.Z;
			else
				Ent.Velocity.Z = 0;

			Ent.Position = newPos;

			// --- Player collision check ---
			// TODO: Use Player.BBox 
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
				Vector3 entMin = Ent.Position;
				Vector3 entMax = Ent.Position + Ent.Size;
				bool touching =
					entMin.X <= playerMax.X && entMax.X >= playerMin.X &&
					entMin.Y <= playerMax.Y && entMax.Y >= playerMin.Y &&
					entMin.Z <= playerMax.Z && entMax.Z >= playerMin.Z;
				if (touching && !Ent._WasPlayerTouching) {
					Ent.OnPlayerTouch(GS.Ply);
					Ent._WasPlayerTouching = true;
				} else if (!touching) {
					Ent._WasPlayerTouching = false;
				}
			}
		}

		public void UpdateLockstep(float TotalTime, float Dt, InputMgr InMgr) {
			for (int i = 0; i < Entities.Count; i++) {
				VoxEntity Ent = Entities[i];

				if (Ent == null)
					continue;

				UpdateEntityPhysics(Ent, Dt);
				Ent.UpdateLockstep(TotalTime, Dt, InMgr);
				Ent.OnUpdatePhysics(Dt);
			}
		}

		public void Draw3D(float TimeAlpha, ref GameFrameInfo LastFrame) {
			for (int i = 0; i < Entities.Count; i++) {
				VoxEntity Ent = Entities[i];

				if (Ent == null)
					continue;

				Ent.Draw3D(TimeAlpha, ref LastFrame);
			}
		}
	}
}
