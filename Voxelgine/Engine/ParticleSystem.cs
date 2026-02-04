using Raylib_cs;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

using Voxelgine.Graphics;

namespace Voxelgine.Engine
{
	enum ParticleType
	{
		Smoke,
		Fire
	}

	enum ParticleBlendMode
	{
		Additive,
		FireType,
		AlphaPremul,
		Multiply,
		Alpha
	}

	struct Particle
	{
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

		public ParticleType Type;
		public float InitialScale; // For fire shrinking effect

		public ParticleBlendMode BlendMode;
	}

	public delegate bool TestFunc(Vector3 Point);
	public delegate BlockType GetBlockFunc(Vector3 Point);

	public class ParticleSystem
	{
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

		public void Init(TestFunc Test, GetBlockFunc GetBlock)
		{
			this.Test = Test;
			this.GetBlock = GetBlock;

			for (int i = 0; i < Particles.Length; i++)
			{
				Particles[i] = new Particle();
				Particles[i].Draw = false;
			}
		}

		public void GetStats(out int OnScreen, out int Drawn, out int Max)
		{
			OnScreen = this.OnScreen;
			Drawn = this.Drawn;
			Max = this.Max;
		}

		public void SpawnSmoke(Vector3 Pos, Vector3 Vel, Color Clr)
		{
			for (int i = 0; i < Particles.Length; i++)
			{
				ref Particle P = ref Particles[i];

				if (P.Draw == false)
				{
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
					P.Type = ParticleType.Smoke;
					P.BlendMode = ParticleBlendMode.AlphaPremul;

					return;
				}
			}
		}

		/// <summary>
		/// Spawns a fire particle effect.
		/// Fire rises upward, is semi-transparent, short-lived, and decreases in size over lifetime.
		/// </summary>
		/// <param name="Pos">Spawn position</param>
		/// <param name="InitialForce">Initial force direction (e.g., wall impact normal). Fire will combine this with upward rise.</param>
		/// <param name="Clr">Tint color (use Color.White for default fire appearance)</param>
		public void SpawnFire(Vector3 Pos, Vector3 InitialForce, Color Clr)
		{
			for (int i = 0; i < Particles.Length; i++)
			{
				ref Particle P = ref Particles[i];

				if (P.Draw == false)
				{
					P.Draw = true;
					P.Pos = Pos;
					P.Color = new Color(Clr.R, Clr.G, Clr.B, (byte)100); // Semi-transparent

					// Combine initial force with upward rise velocity
					float rndX = (Random.Shared.NextSingle() - 0.5f) * 0.5f;
					float rndZ = (Random.Shared.NextSingle() - 0.5f) * 0.5f;
					Vector3 upwardVel = new Vector3(rndX, 2.0f + Random.Shared.NextSingle(), rndZ);
					P.Vel = InitialForce * 0.5f + upwardVel;

					P.SpawnedAt = lastGameTime;
					P.LifeTime = 0.6f + Random.Shared.NextSingle() * 0.4f; // Short-lived: 0.6-1.0 seconds
					P.MovePhysics = true;
					P.Tex = ResMgr.GetFromCollection("fire");

					P.Scaler = 0; // Not used for fire, we use custom shrinking
					P.InitialScale = 0.8f + Random.Shared.NextSingle() * 0.4f; // 0.8 - 1.2
					P.Scale = P.InitialScale;
					P.Rnd = Random.Shared.NextSingle();
					P.Type = ParticleType.Fire;
					P.BlendMode = ParticleBlendMode.FireType;

					return;
				}
			}
		}

		public void Tick(float GameTime)
		{
			float Dt = GameTime - lastGameTime;

			for (int i = 0; i < Particles.Length; i++)
			{
				ref Particle P = ref Particles[i];

				if (P.Draw)
				{
					if (P.SpawnedAt + P.LifeTime < GameTime)
					{
						P.Draw = false;
						continue;
					}


					if (P.MovePhysics)
					{
						float WindHeight = 63;
						float WindHeightScale = 1.0f / 20;

						float Wind = Math.Max(0, P.Pos.Y - WindHeight) * WindHeightScale;
						Vector3 WindAccel = new Vector3(Wind, 0, Wind) * (P.Rnd + 0.5f) * Dt;

						// Check if particle is underwater
						P.IsUnderwater = GetBlock(P.Pos) == BlockType.Water;

						// Apply underwater physics: slower movement, reduced velocity
						if (P.IsUnderwater)
						{
							P.Vel *= 0.95f; // Water resistance
							WindAccel = Vector3.Zero; // No wind underwater
						}

						P.Pos += (P.Vel * Dt) + WindAccel;

						// Handle scale based on particle type
						if (P.Type == ParticleType.Fire)
						{
							// Fire shrinks over lifetime
							float lifeProgress = (GameTime - P.SpawnedAt) / P.LifeTime; // 0.0 to 1.0
							P.Scale = P.InitialScale * (1.0f - lifeProgress * 0.8f); // Shrink to 20% of original

							// Fire velocity decays over time
							P.Vel *= 0.97f;
						}
						else
						{
							// Smoke grows over time (original behavior)
							P.Scale = P.Scale + (P.Scaler * (P.Rnd + 0.5f)) * Dt;
						}

						if (Test(P.Pos))
						{
							P.MovePhysics = false; // Stop moving if the test condition is met
						}
					}
				}
			}

			lastGameTime = GameTime;
		}

		public void Draw(Player Ply, ref Frustum Frust)
		{
			Max = 0;
			Drawn = 0;
			OnScreen = 0;

			// Build list of visible particles with distance from camera
			int visibleCount = 0;
			Vector3 camPos = Ply.Position;

			for (int i = 0; i < Particles.Length; i++)
			{
				ref Particle P = ref Particles[i];
				Max++;

				if (P.Draw)
				{
					Drawn++;

					if (Vector3.Distance(camPos, P.Pos) > 2)
					{
						if (!Frust.IsInside(P.Pos))
						{
							continue;
						}
					}

					SortedIndices[visibleCount] = i;
					DistanceCache[visibleCount] = Vector3.DistanceSquared(camPos, P.Pos);
					visibleCount++;
				}
			}

			// Sort by distance (back-to-front for proper alpha blending)
			for (int i = 0; i < visibleCount - 1; i++)
			{
				for (int j = i + 1; j < visibleCount; j++)
				{
					if (DistanceCache[i] < DistanceCache[j])
					{
						(SortedIndices[i], SortedIndices[j]) = (SortedIndices[j], SortedIndices[i]);
						(DistanceCache[i], DistanceCache[j]) = (DistanceCache[j], DistanceCache[i]);
					}
				}
			}

			Rlgl.DisableDepthMask();

			bool BlendModeSet = false;
			ParticleBlendMode CurBlendMode = ParticleBlendMode.Multiply;
			SetParticleBlendMode(CurBlendMode);

			for (int i = 0; i < visibleCount; i++)
			{
				ref Particle P = ref Particles[SortedIndices[i]];
				OnScreen++;

				if (P.BlendMode != CurBlendMode)
				{
					CurBlendMode = P.BlendMode;
					Raylib.EndBlendMode();
					SetParticleBlendMode(CurBlendMode);
				}

				// Apply underwater tint for particles in water
				Color drawColor = P.Color;
				if (P.IsUnderwater)
				{
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

		void SetParticleBlendMode(ParticleBlendMode Mode)
		{
			switch (Mode)
			{
				case ParticleBlendMode.Additive:
					Raylib.BeginBlendMode(BlendMode.Additive);
					break;

				case ParticleBlendMode.Multiply:
					Raylib.BeginBlendMode(BlendMode.Multiplied);
					break;

				case ParticleBlendMode.Alpha:
					Raylib.BeginBlendMode(BlendMode.Alpha);
					break;

				case ParticleBlendMode.AlphaPremul:
					Raylib.BeginBlendMode(BlendMode.AlphaPremultiply);
					break;

				case ParticleBlendMode.FireType:
					Raylib.BeginBlendMode(BlendMode.Multiplied);
					break;

				default:
					throw new NotImplementedException();
			}
		}
	}
}
