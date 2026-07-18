#if WINDOWS
using System.Collections.Concurrent;
using System.Numerics;
using FishGfx;
using FishGfx.Graphics;
using Voxelgine.Engine;
using Voxelgine.FishGfxClient.Voxels;

namespace Voxelgine.FishGfxClient.Effects;

/// <summary>
/// FishGfx-native gameplay particles. Event producers only enqueue value data;
/// simulation and rendering remain on the client frame thread.
/// </summary>
public sealed class FishGfxGameplayParticles : IDisposable
{
	private const float TracerLifetime = 0.15f;
	private readonly FishGfxGameplayParticleAssets assets;
	private readonly FishGfxVoxelScene scene;
	private readonly BillboardBatch batch;
	private readonly Particle[] particles;
	private readonly Tracer[] tracers;
	private readonly ConcurrentQueue<SpawnRequest> pendingParticles = new();
	private readonly ConcurrentQueue<TracerRequest> pendingTracers = new();
	private readonly VoxelFireEmissionScheduler voxelFireScheduler = new();
	private readonly List<VoxelFireEmission> voxelFireEmissions = new();
	private readonly Random random;
	private int pendingParticleCount;
	private int pendingTracerCount;
	private bool disposed;

	public FishGfxGameplayParticles(
		GraphicsContext graphics,
		FishGfxGameplayParticleAssets assets,
		FishGfxVoxelScene scene,
		int capacity = 256,
		int tracerCapacity = 32,
		Random random = null)
	{
		ArgumentNullException.ThrowIfNull(graphics);
		this.scene = scene ?? throw new ArgumentNullException(nameof(scene));
		if (capacity <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(capacity));
		}
		if (tracerCapacity <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(tracerCapacity));
		}

		this.assets = assets;
		this.random = random ?? Random.Shared;
		particles = new Particle[capacity];
		tracers = new Tracer[tracerCapacity];
		batch = new BillboardBatch(graphics);
	}

	public int Capacity => particles.Length;

	public int TracerCapacity => tracers.Length;

	public int ActiveCount => particles.Count(static particle => particle.Active);

	public int ActiveTracerCount => tracers.Count(static tracer => tracer.Active);

	public bool EnqueueSmoke(Vector3 position, Vector3 velocity, Rgba32 color)
	{
		return Enqueue(new SpawnRequest(
			ParticleKind.Smoke,
			position,
			velocity,
			color,
			1,
			23,
			0.4f,
			MovePhysics: true,
			NoCollision: false,
			Emissive: false,
			BlendMode: BillboardBlendMode.PremultipliedAlpha));
	}

	public bool EnqueueShortSmoke(Vector3 position, Vector3 velocity, Rgba32 color)
	{
		return Enqueue(new SpawnRequest(
			ParticleKind.Smoke,
			position,
			velocity,
			color,
			1,
			0.4f,
			0.3f,
			MovePhysics: true,
			NoCollision: false,
			Emissive: false,
			BlendMode: BillboardBlendMode.PremultipliedAlpha,
			LifetimeJitter: 0.1f));
	}

	public bool EnqueueFire(
		Vector3 position,
		Vector3 initialForce,
		Rgba32 color,
		float scaleFactor = 1,
		bool noCollision = false,
		float lifetime = 0.6f,
		float initialScale = 0.8f,
		bool additive = false)
	{
		ValidatePositiveFinite(scaleFactor, nameof(scaleFactor));
		ValidatePositiveFinite(lifetime, nameof(lifetime));
		ValidatePositiveFinite(initialScale, nameof(initialScale));
		return Enqueue(new SpawnRequest(
			ParticleKind.Fire,
			position,
			initialForce,
			color,
			initialScale * scaleFactor,
			lifetime,
			0,
			MovePhysics: true,
			NoCollision: noCollision,
			Emissive: true,
			BlendMode: additive ? BillboardBlendMode.Additive : BillboardBlendMode.Alpha,
			LifetimeJitter: 0.4f,
			ScaleJitter: 0.4f * scaleFactor));
	}

	public bool EnqueueBlood(Vector3 position, Vector3 normal, float scaleFactor = 1)
	{
		ValidatePositiveFinite(scaleFactor, nameof(scaleFactor));
		return Enqueue(new SpawnRequest(
			ParticleKind.Blood,
			position,
			normal,
			Rgba32.White,
			0.4f * scaleFactor,
			6,
			0,
			MovePhysics: true,
			NoCollision: false,
			Emissive: false,
			BlendMode: BillboardBlendMode.Alpha,
			LifetimeJitter: 4,
			ScaleJitter: 0.3f * scaleFactor));
	}

	public bool EnqueueSpark(
		Vector3 position,
		Vector3 direction,
		Rgba32 color,
		float scaleFactor = 1)
	{
		ValidatePositiveFinite(scaleFactor, nameof(scaleFactor));
		if (!IsFiniteNonZero(direction))
		{
			throw new ArgumentException("Spark direction must be finite and non-zero.", nameof(direction));
		}

		return Enqueue(new SpawnRequest(
			ParticleKind.Spark,
			position,
			direction,
			color,
			0.3f * scaleFactor,
			1.2f,
			0,
			MovePhysics: true,
			NoCollision: false,
			Emissive: true,
			BlendMode: BillboardBlendMode.Additive,
			LifetimeJitter: 0.8f,
			ScaleJitter: 0.2f * scaleFactor));
	}

	public bool EnqueueTracer(Vector3 start, Vector3 end, Rgba32? color = null)
	{
		ThrowIfDisposed();
		if (!IsFinite(start) || !IsFinite(end))
		{
			throw new ArgumentException("Tracer endpoints must be finite.");
		}

		if (Interlocked.Increment(ref pendingTracerCount) > tracers.Length * 4)
		{
			Interlocked.Decrement(ref pendingTracerCount);
			return false;
		}

		pendingTracers.Enqueue(new TracerRequest(
			start,
			end,
			color ?? new Rgba32(255, 255, 150)));
		return true;
	}

	public void UpdateVoxelEmitters(
		float deltaSeconds,
		Vector3 listenerPosition,
		IReadOnlyList<VoxelFireEmitter> emitters)
	{
		ThrowIfDisposed();
		voxelFireScheduler.Advance(
			deltaSeconds,
			listenerPosition,
			emitters,
			voxelFireEmissions
		);

		foreach (VoxelFireEmission emission in voxelFireEmissions)
		{
			Vector3 center = emission.Emitter.Position;
			if (emission.Kind == VoxelFireEmissionKind.Smoke)
			{
				Vector3 smokePosition = center + new Vector3(
					NextSigned(0.12f),
					0.55f,
					NextSigned(0.12f)
				);
				EnqueueShortSmoke(
					smokePosition,
					new Vector3(NextSigned(0.08f), 0.7f, NextSigned(0.08f)),
					new Rgba32(150, 145, 135)
				);
				continue;
			}

			bool isCampfire = emission.Emitter.Type == BlockType.Campfire;
			float spread = isCampfire ? 0.18f : 0.04f;
			Vector3 flamePosition = center + new Vector3(
				NextSigned(spread),
				isCampfire ? 0.18f : 0.3f,
				NextSigned(spread)
			);
			EnqueueFire(
				flamePosition,
				new Vector3(NextSigned(0.1f), 0.25f, NextSigned(0.1f)),
				Rgba32.White,
				isCampfire ? 0.55f : 0.25f,
				noCollision: true,
				lifetime: isCampfire ? 0.65f : 0.5f,
				initialScale: 0.8f,
				additive: true
			);
		}
	}

	public void Update(float deltaSeconds)
	{
		ThrowIfDisposed();
		if (!float.IsFinite(deltaSeconds) || deltaSeconds < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
		}

		DrainRequests();
		float frameScale = deltaSeconds * 60;
		for (int index = 0; index < particles.Length; index++)
		{
			ref Particle particle = ref particles[index];
			if (!particle.Active)
			{
				continue;
			}

			particle.Age += deltaSeconds;
			if (particle.Age >= particle.Lifetime)
			{
				particle = default;
				continue;
			}

			if (!particle.MovePhysics)
			{
				continue;
			}

			particle.IsUnderwater = scene.IsInsideMaterial(
				particle.Position,
				BlockType.Water);
			Vector3 wind = Vector3.Zero;
			if (particle.IsUnderwater)
			{
				particle.Velocity *= MathF.Pow(0.95f, frameScale);
			}
			else
			{
				float windStrength = Math.Max(0, particle.Position.Y - 63) / 20;
				wind = new Vector3(windStrength, 0, windStrength)
					* (particle.RandomFactor + 0.5f);
			}

			particle.Position += (particle.Velocity + wind) * deltaSeconds;
			float lifeProgress = particle.Age / particle.Lifetime;
			switch (particle.Kind)
			{
				case ParticleKind.Fire:
					particle.Size = particle.InitialSize * (1 - lifeProgress * 0.8f);
					particle.Velocity *= MathF.Pow(0.97f, frameScale);
					break;
				case ParticleKind.Blood:
					particle.Velocity.Y -= 9.81f * deltaSeconds;
					float bloodDrag = MathF.Pow(0.99f, frameScale);
					particle.Velocity.X *= bloodDrag;
					particle.Velocity.Z *= bloodDrag;
					break;
				case ParticleKind.Spark:
					particle.Velocity.Y -= 3 * deltaSeconds;
					particle.Size = particle.InitialSize * (1 - lifeProgress * 0.9f);
					particle.Velocity *= MathF.Pow(0.98f, frameScale);
					break;
				default:
					particle.Size += particle.SizeGrowth
						* (particle.RandomFactor + 0.5f)
						* deltaSeconds;
					break;
			}

			if (!particle.NoCollision && scene.IsSolid(particle.Position))
			{
				particle.MovePhysics = false;
			}
		}

		for (int index = 0; index < tracers.Length; index++)
		{
			ref Tracer tracer = ref tracers[index];
			if (!tracer.Active)
			{
				continue;
			}

			tracer.Age += deltaSeconds;
			if (tracer.Age >= tracer.Lifetime)
			{
				tracer = default;
			}
		}
	}

	/// <summary>
	/// Submit after FishGfx's transparent voxel bucket. The batch preserves
	/// global back-to-front order even when textures and blend modes alternate.
	/// </summary>
	public void Render(
		RenderPass pass,
		Vector3 cameraPosition,
		Vector3 cameraTarget,
		Vector3 cameraUp)
	{
		ThrowIfDisposed();
		ArgumentNullException.ThrowIfNull(pass);
		Vector3 forward = cameraTarget - cameraPosition;
		if (!IsFiniteNonZero(forward) || !IsFiniteNonZero(cameraUp))
		{
			throw new ArgumentException("Camera direction and up vectors must be finite and non-zero.");
		}
		forward = Vector3.Normalize(forward);
		Vector3 right = Vector3.Cross(forward, Vector3.Normalize(cameraUp));
		if (right.LengthSquared() < 0.000001f)
		{
			throw new ArgumentException("Camera direction and up vectors must not be parallel.");
		}
		right = Vector3.Normalize(right);
		Vector3 up = Vector3.Normalize(Vector3.Cross(right, forward));

		batch.Begin(cameraPosition, right, up);
		foreach (Particle particle in particles)
		{
			if (!particle.Active)
			{
				continue;
			}

			float lifeProgress = Math.Clamp(particle.Age / particle.Lifetime, 0, 1);
			Rgba32 rgba = ApplyLifetimeAlpha(particle, lifeProgress);
			if (particle.IsUnderwater)
			{
				rgba = new Rgba32(
					(byte)(rgba.R * 0.6f),
					(byte)(rgba.G * 0.8f),
					(byte)Math.Min(byte.MaxValue, rgba.B * 1.1f),
					(byte)(rgba.A * 0.7f));
			}
			if (!particle.Emissive)
			{
				rgba = Multiply(rgba, scene.SampleLight(particle.Position));
			}

			Vector2 size = particle.Kind == ParticleKind.Spark
				? new Vector2(particle.Size * 0.3f, particle.Size)
				: new Vector2(particle.Size);
			batch.Add(
				TextureFor(particle.Kind),
				particle.Position,
				size,
				ToFishColor(rgba),
				particle.BlendMode,
				particle.Kind == ParticleKind.Spark ? particle.Velocity : null);
		}
		batch.Flush(pass);
		RenderTracers(pass);
	}

	public void Clear()
	{
		ThrowIfDisposed();
		Array.Clear(particles);
		Array.Clear(tracers);
		voxelFireScheduler.Reset();
		voxelFireEmissions.Clear();
		while (pendingParticles.TryDequeue(out _))
		{
			Interlocked.Decrement(ref pendingParticleCount);
		}
		while (pendingTracers.TryDequeue(out _))
		{
			Interlocked.Decrement(ref pendingTracerCount);
		}
	}

	public void Dispose()
	{
		if (disposed)
		{
			return;
		}

		Clear();
		batch.Dispose();
		disposed = true;
	}

	private bool Enqueue(in SpawnRequest request)
	{
		ThrowIfDisposed();
		if (!IsFinite(request.Position) || !IsFinite(request.Vector))
		{
			throw new ArgumentException("Particle position and velocity values must be finite.");
		}

		if (Interlocked.Increment(ref pendingParticleCount) > particles.Length * 4)
		{
			Interlocked.Decrement(ref pendingParticleCount);
			return false;
		}

		pendingParticles.Enqueue(request);
		return true;
	}

	private void DrainRequests()
	{
		while (pendingParticles.TryDequeue(out SpawnRequest request))
		{
			Interlocked.Decrement(ref pendingParticleCount);
			int slot = Array.FindIndex(particles, static particle => !particle.Active);
			if (slot < 0)
			{
				continue;
			}

			particles[slot] = CreateParticle(request);
		}

		while (pendingTracers.TryDequeue(out TracerRequest request))
		{
			Interlocked.Decrement(ref pendingTracerCount);
			int slot = Array.FindIndex(tracers, static tracer => !tracer.Active);
			if (slot < 0)
			{
				continue;
			}

			tracers[slot] = new Tracer
			{
				Active = true,
				Start = request.Start,
				End = request.End,
				Color = request.Color,
				Lifetime = TracerLifetime,
			};
		}
	}

	private Particle CreateParticle(in SpawnRequest request)
	{
		Vector3 velocity = request.Vector;
		switch (request.Kind)
		{
			case ParticleKind.Fire:
				velocity = request.Vector * 0.5f + new Vector3(
					NextSigned(0.25f),
					2 + random.NextSingle(),
					NextSigned(0.25f));
				break;
			case ParticleKind.Blood:
				velocity = (request.Vector + new Vector3(
					NextSigned(0.75f),
					NextSigned(0.75f) + 0.5f,
					NextSigned(0.75f))) * (2 + random.NextSingle() * 0.1f);
				break;
			case ParticleKind.Spark:
				float sparkSpeed = Math.Max(3, request.Vector.Length());
				Vector3 sparkDirection = Vector3.Normalize(request.Vector);
				velocity = (sparkDirection + new Vector3(
					NextSigned(0.5f),
					NextSigned(0.5f),
					NextSigned(0.5f))) * (sparkSpeed + random.NextSingle() * 0.4f);
				break;
		}

		float size = request.Size + random.NextSingle() * request.ScaleJitter;
		return new Particle
		{
			Active = true,
			Kind = request.Kind,
			Position = request.Position,
			Velocity = velocity,
			Color = request.Kind == ParticleKind.Fire
				? request.Color with { A = 100 }
				: request.Color,
			Size = size,
			InitialSize = size,
			SizeGrowth = request.SizeGrowth,
			Lifetime = request.Lifetime + random.NextSingle() * request.LifetimeJitter,
			RandomFactor = random.NextSingle(),
			MovePhysics = request.MovePhysics,
			NoCollision = request.NoCollision,
			Emissive = request.Emissive,
			BlendMode = request.BlendMode,
		};
	}

	private void RenderTracers(RenderPass pass)
	{
		RenderState additiveState = pass.State with
		{
			DepthTestEnabled = true,
			DepthWriteEnabled = false,
			BlendEnabled = true,
			SourceBlend = BlendFactor.SourceAlpha,
			DestinationBlend = BlendFactor.One,
		};
		using IDisposable stateScope = pass.PushState(additiveState);
		foreach (Tracer tracer in tracers)
		{
			if (!tracer.Active)
			{
				continue;
			}

			float remaining = Math.Clamp(1 - tracer.Age / tracer.Lifetime, 0, 1);
			Rgba32 rgba = tracer.Color with
			{
				A = (byte)(tracer.Color.A * remaining),
			};
			Color color = ToFishColor(rgba);
			pass.DrawLine(
				new FishGfx.Vertex3(tracer.Start, color),
				new FishGfx.Vertex3(tracer.End, color),
				2);
		}
	}

	private Texture TextureFor(ParticleKind kind)
	{
		return kind switch
		{
			ParticleKind.Fire => assets.Fire.Value,
			ParticleKind.Blood => assets.Blood.Value,
			ParticleKind.Spark => assets.Spark.Value,
			_ => assets.Smoke.Value,
		};
	}

	private float NextSigned(float radius)
	{
		return (random.NextSingle() * 2 - 1) * radius;
	}

	private static Rgba32 ApplyLifetimeAlpha(in Particle particle, float lifeProgress)
	{
		float alphaScale = particle.Kind switch
		{
			ParticleKind.Blood when lifeProgress > 0.5f => 2 * (1 - lifeProgress),
			ParticleKind.Fire or ParticleKind.Spark => 1 - lifeProgress,
			ParticleKind.Smoke when lifeProgress > 0.8f => 5 * (1 - lifeProgress),
			_ => 1,
		};

		return particle.Color with
		{
			A = (byte)(particle.Color.A * Math.Clamp(alphaScale, 0, 1)),
		};
	}

	private static Rgba32 Multiply(Rgba32 color, Rgba32 light)
	{
		return new Rgba32(
			(byte)(color.R * light.R / byte.MaxValue),
			(byte)(color.G * light.G / byte.MaxValue),
			(byte)(color.B * light.B / byte.MaxValue),
			color.A);
	}

	private static Color ToFishColor(Rgba32 color)
	{
		return new Color(color.R, color.G, color.B, color.A);
	}

	private static void ValidatePositiveFinite(float value, string parameterName)
	{
		if (!float.IsFinite(value) || value <= 0)
		{
			throw new ArgumentOutOfRangeException(parameterName);
		}
	}

	private static bool IsFinite(Vector3 value)
	{
		return float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
	}

	private static bool IsFiniteNonZero(Vector3 value)
	{
		return IsFinite(value) && value.LengthSquared() >= 0.000001f;
	}

	private void ThrowIfDisposed()
	{
		ObjectDisposedException.ThrowIf(disposed, this);
	}

	private enum ParticleKind
	{
		Smoke,
		Fire,
		Blood,
		Spark,
	}

	private readonly record struct SpawnRequest(
		ParticleKind Kind,
		Vector3 Position,
		Vector3 Vector,
		Rgba32 Color,
		float Size,
		float Lifetime,
		float SizeGrowth,
		bool MovePhysics,
		bool NoCollision,
		bool Emissive,
		BillboardBlendMode BlendMode,
		float LifetimeJitter = 0,
		float ScaleJitter = 0);

	private readonly record struct TracerRequest(Vector3 Start, Vector3 End, Rgba32 Color);

	private struct Particle
	{
		internal bool Active;
		internal ParticleKind Kind;
		internal Vector3 Position;
		internal Vector3 Velocity;
		internal Rgba32 Color;
		internal float Size;
		internal float InitialSize;
		internal float SizeGrowth;
		internal float Age;
		internal float Lifetime;
		internal float RandomFactor;
		internal bool MovePhysics;
		internal bool NoCollision;
		internal bool IsUnderwater;
		internal bool Emissive;
		internal BillboardBlendMode BlendMode;
	}

	private struct Tracer
	{
		internal bool Active;
		internal Vector3 Start;
		internal Vector3 End;
		internal Rgba32 Color;
		internal float Age;
		internal float Lifetime;
	}
}
#endif
