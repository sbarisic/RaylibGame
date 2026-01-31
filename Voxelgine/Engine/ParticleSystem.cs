using Raylib_cs;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

using Voxelgine.Graphics;

namespace Voxelgine.Engine {
	struct Particle {
		public bool Draw;
		public Vector3 Pos;
		public Vector3 Vel;
		public Color Color;
		public Texture2D Tex;

		public float Scale;
		public float Scaler;
		public float Rnd;

		public bool MovePhysics;
		public bool IsUnderwater;

		public float SpawnedAt;
		public float LifeTime;
	}

	public delegate bool TestFunc(Vector3 Point);
	public delegate BlockType GetBlockFunc(Vector3 Point);

	public class ParticleSystem {
		Particle[] Particles = new Particle[256];
		int[] SortedIndices = new int[256];
		float[] DistanceCache = new float[256];
		float lastGameTime = 0;

		public TestFunc Test;
		public GetBlockFunc GetBlock;

		// Debug variables
		int OnScreen;
		int Drawn;
		int Max;

		public void Init(TestFunc Test, GetBlockFunc GetBlock) {
			this.Test = Test;
			this.GetBlock = GetBlock;

			for (int i = 0; i < Particles.Length; i++) {
				Particles[i] = new Particle();
				Particles[i].Draw = false;
			}
		}

		public void GetStats(out int OnScreen, out int Drawn, out int Max) {
			OnScreen = this.OnScreen;
			Drawn = this.Drawn;
			Max = this.Max;
		}

		public void SpawnSmoke(Vector3 Pos, Vector3 Vel, Color Clr) {
			for (int i = 0; i < Particles.Length; i++) {
				ref Particle P = ref Particles[i];

				if (P.Draw == false) {
					P.Draw = true;
					P.Pos = Pos;
					P.Color = Clr;
					P.Vel = Vel;
					P.SpawnedAt = lastGameTime;
					P.LifeTime = 23.0f;
					P.MovePhysics = true;
					P.Tex = ResMgr.GetFromCollection("smoke");
					P.Scaler = 0.4f;
					P.Scale = 1.0f;
					P.Rnd = Random.Shared.NextSingle();

					return;
				}
			}
		}

		public void Tick(float GameTime) {
			float Dt = GameTime - lastGameTime;

			for (int i = 0; i < Particles.Length; i++) {
				ref Particle P = ref Particles[i];

				if (P.Draw) {
					if (P.SpawnedAt + P.LifeTime < GameTime) {
						P.Draw = false;
						continue;
					}


					if (P.MovePhysics) {
						float WindHeight = 63;
						float WindHeightScale = 1.0f / 20;

						float Wind = Math.Max(0, P.Pos.Y - WindHeight) * WindHeightScale;
						Vector3 WindAccel = new Vector3(Wind, 0, Wind) * (P.Rnd + 0.5f) * Dt;

						// Check if particle is underwater
						P.IsUnderwater = GetBlock(P.Pos) == BlockType.Water;

						// Apply underwater physics: slower movement, reduced velocity
						if (P.IsUnderwater) {
							P.Vel *= 0.95f; // Water resistance
							WindAccel = Vector3.Zero; // No wind underwater
						}

						P.Pos += (P.Vel * Dt) + WindAccel;
						P.Scale = P.Scale + (P.Scaler * (P.Rnd + 0.5f)) * Dt;

						if (Test(P.Pos)) {
							P.MovePhysics = false; // Stop moving if the test condition is met
						}
					}
				}
			}

			lastGameTime = GameTime;
		}

		public void Draw(Player Ply, ref Frustum Frust) {
			Max = 0;
			Drawn = 0;
			OnScreen = 0;

			// Build list of visible particles with distance from camera
			int visibleCount = 0;
			Vector3 camPos = Ply.Position;

			for (int i = 0; i < Particles.Length; i++) {
				ref Particle P = ref Particles[i];
				Max++;

				if (P.Draw) {
					Drawn++;

					if (Vector3.Distance(camPos, P.Pos) > 2) {
						if (!Frust.IsInside(P.Pos)) {
							continue;
						}
					}

					SortedIndices[visibleCount] = i;
					DistanceCache[visibleCount] = Vector3.DistanceSquared(camPos, P.Pos);
					visibleCount++;
				}
			}

			// Sort by distance (back-to-front for proper alpha blending)
			for (int i = 0; i < visibleCount - 1; i++) {
				for (int j = i + 1; j < visibleCount; j++) {
					if (DistanceCache[i] < DistanceCache[j]) {
						(SortedIndices[i], SortedIndices[j]) = (SortedIndices[j], SortedIndices[i]);
						(DistanceCache[i], DistanceCache[j]) = (DistanceCache[j], DistanceCache[i]);
					}
				}
			}

			Rlgl.DisableDepthMask();
			Raylib.BeginBlendMode(BlendMode.AlphaPremultiply);

			for (int i = 0; i < visibleCount; i++) {
				ref Particle P = ref Particles[SortedIndices[i]];
				OnScreen++;

				// Apply underwater tint for particles in water
				Color drawColor = P.Color;
				if (P.IsUnderwater) {
					drawColor = new Color(
						(byte)(P.Color.R * 0.6f),
						(byte)(P.Color.G * 0.8f),
						(byte)Math.Min(255, P.Color.B * 1.1f),
						(byte)(P.Color.A * 0.7f)
					);
				}

				Raylib.DrawBillboard(Ply.Cam, P.Tex, P.Pos, P.Scale, drawColor);
			}

			Raylib.EndBlendMode();
			Rlgl.EnableDepthMask();
		}
	}
}
