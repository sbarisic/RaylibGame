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

		public float SpawnedAt;
		public float LifeTime;
	}

	public delegate bool TestFunc(Vector3 Point);

	public class ParticleSystem {
		Particle[] Particles = new Particle[256];
		float lastGameTime = 0;

		public TestFunc Test;

		public void Init(TestFunc Test) {
			this.Test = Test;

			for (int i = 0; i < Particles.Length; i++) {
				Particles[i] = new Particle();
				Particles[i].Draw = false;
			}
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
			Rlgl.DisableDepthMask();
			Raylib.BeginBlendMode(BlendMode.AlphaPremultiply);

			for (int i = 0; i < Particles.Length; i++) {
				ref Particle P = ref Particles[i];

				if (P.Draw) {

					if (Vector3.Distance(Ply.Position, P.Pos) > 4) {

						if (!Frust.IsInside(P.Pos)) {
							// Particle is outside the frustum, skip drawing
							continue;
						}
					}

					Raylib.DrawBillboard(Ply.Cam, P.Tex, P.Pos, P.Scale, P.Color);
				}
			}

			Raylib.EndBlendMode();
			Rlgl.EnableDepthMask();
		}
	}
}
