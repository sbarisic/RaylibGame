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
		Fire,
		Blood,
		Spark
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

		public bool IsEmissive;
		public ParticleBlendMode BlendMode;
	}

	struct TracerLine
	{
		public bool Active;
		public Vector3 Start;
		public Vector3 End;
		public Color Color;
		public float SpawnedAt;
		public float LifeTime;
	}

	public delegate bool TestFunc(Vector3 Point);
	public delegate BlockType GetBlockFunc(Vector3 Point);
	public delegate Color GetLightColorFunc(Vector3 Point);

	public class ParticleSystem
	{
		Particle[] Particles = new Particle[256];
		int[] SortedIndices = new int[256];
		float[] DistanceCache = new float[256];
		float lastGameTime = 0;

		// Tracer lines
		TracerLine[] Tracers = new TracerLine[32];
		const float TracerLifetime = 0.15f; // How long tracers persist (seconds)

		public TestFunc Test;
		public GetBlockFunc GetBlock;
		public GetLightColorFunc GetLightColor;

		// Debug variables
		int OnScreen;
		int Drawn;
		int Max;

		public void Init(TestFunc Test, GetBlockFunc GetBlock, GetLightColorFunc GetLightColor)
		{
			this.Test = Test;
			this.GetBlock = GetBlock;
			this.GetLightColor = GetLightColor;

			for (int i = 0; i < Particles.Length; i++)
			{
				Particles[i] = new Particle();
				Particles[i].Draw = false;
			}

			for (int i = 0; i < Tracers.Length; i++)
			{
				Tracers[i] = new TracerLine();
				Tracers[i].Active = false;
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
					P.IsEmissive = false;
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
		public void SpawnFire(Vector3 Pos, Vector3 InitialForce, Color Clr, float ScaleFactor = 1.0f)
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
										P.InitialScale = (0.8f + Random.Shared.NextSingle() * 0.4f) * ScaleFactor; // 0.8 - 1.2
										P.Scale = P.InitialScale;
										P.Rnd = Random.Shared.NextSingle();
										P.Type = ParticleType.Fire;
										P.IsEmissive = true;
										P.BlendMode = ParticleBlendMode.FireType;

										return;
									}
								}
							}

							/// <summary>
							/// Spawns a blood particle effect.
							/// Blood is ejected from the normal direction, falls with gravity, and fades out over ~8 seconds.
							/// </summary>
							/// <param name="Pos">Spawn position (hit point on NPC)</param>
							/// <param name="Normal">Surface normal direction (blood ejects outward from this)</param>
							/// <param name="ScaleFactor">Size multiplier</param>
							public void SpawnBlood(Vector3 Pos, Vector3 Normal, float ScaleFactor = 1.0f)
							{
								for (int i = 0; i < Particles.Length; i++)
								{
									ref Particle P = ref Particles[i];

									if (P.Draw == false)
									{
										P.Draw = true;
										P.Pos = Pos;
										P.Color = Color.White; // Texture provides color

										// Eject from normal with random spread and slight upward bias
										float spreadX = (Random.Shared.NextSingle() - 0.5f) * 1.5f;
										float spreadY = (Random.Shared.NextSingle() - 0.5f) * 1.5f;
										float spreadZ = (Random.Shared.NextSingle() - 0.5f) * 1.5f;
										float speed = 2.0f + Random.Shared.NextSingle() * 3.0f; // 2-5 units/sec
										Vector3 spread = new Vector3(spreadX, spreadY + 0.5f, spreadZ);
										P.Vel = (Normal + spread) * speed;

										P.SpawnedAt = lastGameTime;
										P.LifeTime = 6.0f + Random.Shared.NextSingle() * 4.0f; // 6-10 seconds
										P.MovePhysics = true;
										P.Tex = ResMgr.GetFromCollection("blood");

										P.Scaler = 0; // Not used for blood
										P.InitialScale = (0.4f + Random.Shared.NextSingle() * 0.3f) * ScaleFactor; // 0.4 - 0.7
										P.Scale = P.InitialScale;
										P.Rnd = Random.Shared.NextSingle();
										P.Type = ParticleType.Blood;
										P.IsEmissive = false;
										P.BlendMode = ParticleBlendMode.Alpha;

										return;
									}
								}
							}

		/// <summary>
		/// Spawns a spark particle effect.
		/// Sparks are oriented along their movement direction, fall slowly with gravity,
		/// and live twice as long as fire (1.2-2.0s).
		/// </summary>
		/// <param name="Pos">Spawn position</param>
		/// <param name="Direction">Initial direction of travel</param>
		/// <param name="Clr">Tint color (use Color.White for default spark appearance)</param>
		/// <param name="ScaleFactor">Size multiplier</param>
		public void SpawnSpark(Vector3 Pos, Vector3 Direction, Color Clr, float ScaleFactor = 1.0f)
		{
			for (int i = 0; i < Particles.Length; i++)
			{
				ref Particle P = ref Particles[i];

				if (P.Draw == false)
				{
					P.Draw = true;
					P.Pos = Pos;
					P.Color = Clr;

					// Spark flies in given direction with random spread
					float spreadX = (Random.Shared.NextSingle() - 0.5f) * 1.0f;
					float spreadY = (Random.Shared.NextSingle() - 0.5f) * 1.0f;
					float spreadZ = (Random.Shared.NextSingle() - 0.5f) * 1.0f;
					float speed = 3.0f + Random.Shared.NextSingle() * 4.0f; // 3-7 units/sec
					P.Vel = (Direction + new Vector3(spreadX, spreadY, spreadZ)) * speed;

					P.SpawnedAt = lastGameTime;
					P.LifeTime = 1.2f + Random.Shared.NextSingle() * 0.8f; // 1.2-2.0 seconds (twice fire)
					P.MovePhysics = true;
					P.Tex = ResMgr.GetFromCollection("spark");

					P.Scaler = 0;
					P.InitialScale = (0.3f + Random.Shared.NextSingle() * 0.2f) * ScaleFactor; // 0.3 - 0.5
					P.Scale = P.InitialScale;
					P.Rnd = Random.Shared.NextSingle();
					P.Type = ParticleType.Spark;
					P.IsEmissive = true;
					P.BlendMode = ParticleBlendMode.Additive;

					return;
				}
			}
		}

		/// <summary>
		/// Spawns a tracer line effect from start to end position.
		/// The tracer will fade out over a short duration.
		/// </summary>
		/// <param name="start">Start position (gun muzzle).</param>
		/// <param name="end">End position (hit point or max range).</param>
		/// <param name="color">Tracer color (default: bright yellow).</param>
		public void SpawnTracer(Vector3 start, Vector3 end, Color? color = null)
		{
			for (int i = 0; i < Tracers.Length; i++)
			{
				ref TracerLine T = ref Tracers[i];

				if (!T.Active)
				{
					T.Active = true;
					T.Start = start;
					T.End = end;
					T.Color = color ?? new Color(255, 255, 150, 255); // Bright yellow-white
					T.SpawnedAt = lastGameTime;
					T.LifeTime = TracerLifetime;
					return;
				}
			}
		}

		public void Tick(float GameTime)
		{
			float Dt = GameTime - lastGameTime;

			// Update tracers (just check lifetime, they don't move)
			for (int i = 0; i < Tracers.Length; i++)
			{
				ref TracerLine T = ref Tracers[i];
				if (T.Active && T.SpawnedAt + T.LifeTime < GameTime)
				{
					T.Active = false;
				}
			}

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
						else if (P.Type == ParticleType.Blood)
						{
							// Blood falls with gravity
							P.Vel.Y -= 9.81f * Dt;

							// Blood fades out over lifetime (modify alpha in color)
							float lifeProgress = (GameTime - P.SpawnedAt) / P.LifeTime;
							// Start fading after 50% of lifetime
							if (lifeProgress > 0.5f)
							{
								float fadeProgress = (lifeProgress - 0.5f) * 2.0f; // 0.0 to 1.0 over second half
								byte alpha = (byte)(255 * (1.0f - fadeProgress));
								P.Color = new Color(P.Color.R, P.Color.G, P.Color.B, alpha);
							}

							// Blood velocity decays slightly (air resistance)
							P.Vel.X *= 0.99f;
							P.Vel.Z *= 0.99f;
						}
						else if (P.Type == ParticleType.Spark)
						{
							// Sparks fall slowly with gravity
							P.Vel.Y -= 3.0f * Dt;

							// Spark shrinks over lifetime
							float lifeProgress = (GameTime - P.SpawnedAt) / P.LifeTime;
							P.Scale = P.InitialScale * (1.0f - lifeProgress * 0.9f);

							// Spark velocity decays (air resistance)
							P.Vel *= 0.98f;
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

						// Draw tracer lines first (additive blend for glow effect)
						Raylib.BeginBlendMode(BlendMode.Additive);
						for (int i = 0; i < Tracers.Length; i++)
						{
							ref TracerLine T = ref Tracers[i];
							if (T.Active)
							{
								// Calculate fade based on lifetime progress
								float lifeProgress = (lastGameTime - T.SpawnedAt) / T.LifeTime;
								byte alpha = (byte)(255 * (1.0f - lifeProgress));
								Color fadeColor = new Color(T.Color.R, T.Color.G, T.Color.B, alpha);

								// Draw the tracer line
								Raylib.DrawLine3D(T.Start, T.End, fadeColor);

								// Draw a slightly thicker line by offsetting (gives more visibility)
								Vector3 offset = new Vector3(0.01f, 0, 0);
								Raylib.DrawLine3D(T.Start + offset, T.End + offset, fadeColor);
								Raylib.DrawLine3D(T.Start - offset, T.End - offset, fadeColor);
							}
						}
						Raylib.EndBlendMode();

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

				// Apply world lighting to particle color
				if (GetLightColor != null && !P.IsEmissive)
				{
					Color lightColor = GetLightColor(P.Pos);
					drawColor = new Color(
						(byte)(drawColor.R * lightColor.R / 255),
						(byte)(drawColor.G * lightColor.G / 255),
						(byte)(drawColor.B * lightColor.B / 255),
						drawColor.A
					);
				}

				if (P.Type == ParticleType.Spark && P.Vel.LengthSquared() > 0.01f)
				{
					// Orient spark billboard along movement direction
					Vector3 sparkUp = Vector3.Normalize(P.Vel);
					Vector2 sparkSize = new Vector2(P.Scale * 0.3f, P.Scale);
					Rectangle sparkSrc = new Rectangle(0, 0, P.Tex.Width, P.Tex.Height);
					Vector2 sparkOrigin = sparkSize * 0.5f;
					Raylib.DrawBillboardPro(Ply.Cam, P.Tex, sparkSrc, P.Pos, sparkUp, sparkSize, sparkOrigin, 0f, drawColor);
				}
				else
				{
					Raylib.DrawBillboard(Ply.Cam, P.Tex, P.Pos, P.Scale, drawColor);
				}
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
