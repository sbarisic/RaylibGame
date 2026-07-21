#if WINDOWS
using FishGfx;
using FishGfx.Graphics;
using System.Numerics;
using Voxelgine.FishGfxClient.Assets;

namespace Voxelgine.FishGfxClient.Effects;

public enum ParticleEffectKind
{
	Smoke,
	Fire,
	Blood,
	Spark,
}

public sealed class FishGfxParticlePreview : IDisposable
{
	private const int Capacity = 256;
	private readonly AssetHandle<Texture> smoke;
	private readonly AssetHandle<Texture> fire;
	private readonly AssetHandle<Texture> blood;
	private readonly AssetHandle<Texture> spark;
	private readonly BillboardBatch batch;
	private readonly PreviewParticle[] particles = new PreviewParticle[Capacity];
	private bool disposed;

	public FishGfxParticlePreview(IFishGfxGameWindow window)
	{
		ArgumentNullException.ThrowIfNull(window);
		smoke = window.Assets.LoadColorTexture("preview.smoke", "data/textures/smoke/1.png");
		fire = window.Assets.LoadColorTexture("preview.fire", "data/textures/fire/1.png");
		blood = window.Assets.LoadColorTexture("preview.blood", "data/textures/blood/1.png");
		spark = window.Assets.LoadColorTexture("preview.spark", "data/textures/spark/1.png");
		batch = new BillboardBatch(window.RenderWindow.Graphics);
	}

	public int ActiveCount => particles.Count(particle => particle.Active);

	public void Spawn(
		ParticleEffectKind type,
		int count,
		float speed,
		float scale,
		float lifetime,
		Color color,
		float spread = 0.3f
	)
	{
		ObjectDisposedException.ThrowIf(disposed, this);
		for (int index = 0; index < Math.Max(1, count); index++)
		{
			int slot = Array.FindIndex(particles, particle => !particle.Active);
			if (slot < 0)
			{
				return;
			}

			Vector3 random = new(
				Random.Shared.NextSingle() - 0.5f,
				Random.Shared.NextSingle(),
				Random.Shared.NextSingle() - 0.5f
			);
			Vector3 velocity = type switch
			{
				ParticleEffectKind.Blood => Vector3.Normalize(random + Vector3.UnitY * 0.25f) * speed,
				ParticleEffectKind.Spark => Vector3.Normalize(random + Vector3.UnitY * 0.5f) * speed,
				ParticleEffectKind.Fire => new Vector3(random.X * spread, 0.6f + random.Y, random.Z * spread) * speed,
				_ => new Vector3(random.X * spread, 0.25f + random.Y * 0.5f, random.Z * spread) * speed,
			};
			particles[slot] = new PreviewParticle
			{
				Active = true,
				Type = type,
				Position = new Vector3(random.X * spread, 1 + random.Y * spread, random.Z * spread),
				Velocity = velocity,
				Color = color,
				Size = MathF.Max(0.02f, scale * (0.6f + Random.Shared.NextSingle() * 0.4f)),
				Lifetime = MathF.Max(0.05f, lifetime),
			};
		}
	}

	public void Clear()
	{
		Array.Clear(particles);
	}

	public void Update(float deltaTime)
	{
		ObjectDisposedException.ThrowIf(disposed, this);
		for (int index = 0; index < particles.Length; index++)
		{
			ref PreviewParticle particle = ref particles[index];
			if (!particle.Active)
			{
				continue;
			}

			particle.Age += deltaTime;
			if (particle.Age >= particle.Lifetime)
			{
				particle = default;
				continue;
			}

			if (particle.Type is ParticleEffectKind.Blood or ParticleEffectKind.Spark)
			{
				particle.Velocity += new Vector3(0, -5.5f, 0) * deltaTime;
			}
			particle.Position += particle.Velocity * deltaTime;
			if (particle.Position.Y < 0)
			{
				particle.Position.Y = 0;
				particle.Velocity.Y *= -0.25f;
			}
		}
	}

	public void Render(RenderPass pass, Vector3 cameraPosition, Vector3 cameraTarget)
	{
		ObjectDisposedException.ThrowIf(disposed, this);
		Vector3 direction = cameraTarget - cameraPosition;
		float lengthSquared = direction.LengthSquared();
		Vector3 forward = float.IsFinite(lengthSquared) && lengthSquared > 1e-12f
			? direction / MathF.Sqrt(lengthSquared)
			: Vector3.UnitZ;
		Vector3 right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
		Vector3 up = Vector3.Normalize(Vector3.Cross(right, forward));
		batch.Begin(cameraPosition, right, up);
		foreach (PreviewParticle particle in particles)
		{
			if (!particle.Active)
			{
				continue;
			}

			float life = 1 - particle.Age / particle.Lifetime;
			float size = particle.Type == ParticleEffectKind.Fire
				? particle.Size * MathF.Max(0.05f, life)
				: particle.Size * (1 + (1 - life) * 0.4f);
			Color color = new(
				particle.Color.R,
				particle.Color.G,
				particle.Color.B,
				(byte)(particle.Color.A * Math.Clamp(life, 0, 1))
			);
			batch.Add(
				TextureFor(particle.Type),
				particle.Position,
				new Vector2(size),
				color,
				particle.Type is ParticleEffectKind.Fire or ParticleEffectKind.Spark
					? BillboardBlendMode.Additive
					: BillboardBlendMode.Alpha
			);
		}
		batch.Flush(pass);
	}

	public void Dispose()
	{
		if (disposed)
		{
			return;
		}
		batch.Dispose();
		disposed = true;
	}

	private Texture TextureFor(ParticleEffectKind type)
	{
		return type switch
		{
			ParticleEffectKind.Fire => fire.Value,
			ParticleEffectKind.Blood => blood.Value,
			ParticleEffectKind.Spark => spark.Value,
			_ => smoke.Value,
		};
	}

	private struct PreviewParticle
	{
		internal bool Active;
		internal ParticleEffectKind Type;
		internal Vector3 Position;
		internal Vector3 Velocity;
		internal Color Color;
		internal float Size;
		internal float Age;
		internal float Lifetime;
	}
}
#endif
