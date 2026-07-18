using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Voxelgine.Engine.DI;

namespace Voxelgine.Engine
{
	/// <summary>
	/// Base class for authoritative voxel-world entities. Rendering resources are
	/// owned by client adapters; the entity retains only stable asset and pose data.
	/// </summary>
	public abstract class VoxEntity
	{
		/// <summary>
		/// Unique network identifier assigned by <see cref="EntityManager.Spawn"/>.
		/// Zero means unassigned.
		/// </summary>
		public int NetworkId { get; internal set; }

		/// <summary>World position of the entity (bottom-center for physics).</summary>
		public Vector3 Position;

		/// <summary>Entity bounding-box size for collision.</summary>
		public Vector3 Size;

		/// <summary>Current velocity, updated by entity physics.</summary>
		public Vector3 Velocity;

		/// <summary>Light emission level from zero through fifteen.</summary>
		public byte LightEmission;

		/// <summary>Whether emitted light casts shadows.</summary>
		public bool LightCastsShadows = true;

		private string _modelAssetId;
		private Vector3 _presentationOffset;
		private float _rotationDegrees;
		private IFishEngineRunner _engine;
		private EntityManager _entityManager;
		private GameSimulation _simulation;

		/// <summary>Renderer-neutral tint consumed by client presentation adapters.</summary>
		public Rgba32 Tint { get; set; } = Rgba32.White;

		/// <summary>Rotate the entity's presentation around the Y axis.</summary>
		public bool IsRotating;

		/// <summary>Presentation rotation speed in degrees per second.</summary>
		public float RotationSpeed = 30f;

		public IFishEngineRunner Eng
		{
			get => _engine;
			set
			{
				_engine = value;
				Logging = value.DI.GetRequiredService<IFishLogging>();
			}
		}

		protected IFishLogging Logging;

		/// <summary>Network spawn type name.</summary>
		public virtual string EntityTypeName => GetType().Name;

		/// <summary>Stable model content identifier used by client render adapters.</summary>
		public string ModelAssetId => _modelAssetId ?? string.Empty;

		/// <summary>Current cosmetic rotation, independent of a graphics backend.</summary>
		public float RotationDegrees => _rotationDegrees;

		/// <summary>Current presentation offset, such as pickup bobbing.</summary>
		public Vector3 PresentationOffset => _presentationOffset;

		/// <summary>How this entity participates in world and player physics.</summary>
		public virtual EntityPhysicsProperties PhysicsProperties => EntityPhysicsProperties.DynamicTrigger;

		/// <summary>World-space collision bounds using the documented bottom-center position.</summary>
		public AABB WorldBounds => PhysicsUtils.CreateEntityAABB(Position, Size);

		/// <summary>Current vertical presentation offset used by bobbing entities.</summary>
		public float VerticalModelOffset => _presentationOffset.Y;

		public virtual void OnInit()
		{
		}

		/// <summary>
		/// Writes spawn properties. Field order and encodings are part of the wire format.
		/// </summary>
		public void WriteSpawnProperties(BinaryWriter writer)
		{
			writer.Write(Size.X);
			writer.Write(Size.Y);
			writer.Write(Size.Z);
			writer.Write(_modelAssetId ?? string.Empty);
			WriteSpawnPropertiesExtra(writer);
		}

		/// <summary>
		/// Reads spawn properties without loading any graphics resources.
		/// </summary>
		public void ReadSpawnProperties(BinaryReader reader)
		{
			Size = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

			string modelAssetId = reader.ReadString();
			if (!string.IsNullOrEmpty(modelAssetId))
				SetModel(modelAssetId);

			ReadSpawnPropertiesExtra(reader);
		}

		protected virtual void WriteSpawnPropertiesExtra(BinaryWriter writer)
		{
		}

		protected virtual void ReadSpawnPropertiesExtra(BinaryReader reader)
		{
		}

		/// <summary>Stores a model asset ID without loading a GPU resource.</summary>
		public void SetModelName(string modelAssetId)
		{
			_modelAssetId = modelAssetId;
		}

		/// <summary>
		/// Records a model asset ID and resets the renderer-neutral presentation pose.
		/// </summary>
		public virtual void SetModel(string modelAssetId)
		{
			_modelAssetId = modelAssetId;
			_presentationOffset = Vector3.Zero;
			_rotationDegrees = 0f;
			Tint = Rgba32.White;
		}

		protected void SetPresentationOffset(Vector3 offset)
		{
			_presentationOffset = offset;
		}

		public virtual Vector3 GetPosition() => Position;

		public virtual void SetPosition(Vector3 position)
		{
			Position = position;
		}

		public virtual Vector3 GetSize() => Size;

		public virtual void SetSize(Vector3 size)
		{
			Size = size;
		}

		public virtual Vector3 GetVelocity() => Velocity;

		public virtual void SetVelocity(Vector3 velocity)
		{
			Velocity = velocity;
		}

		public virtual EntityManager GetEntityManager() => _entityManager;

		public virtual void SetEntityManager(EntityManager entityManager)
		{
			_entityManager = entityManager;
		}

		public virtual GameSimulation GetSimulation() => _simulation;

		public virtual void SetSimulation(GameSimulation simulation)
		{
			_simulation = simulation;
		}

		public virtual void OnPlayerTouch(Player player)
		{
			Logging.WriteLine("Player touched me!");
		}

		public virtual void UpdateLockstep(float totalTime, float deltaTime, InputMgr inputManager)
		{
			UpdateRotation(deltaTime);
		}

		/// <summary>
		/// Updates cosmetic pose state without running authoritative AI or physics.
		/// </summary>
		public virtual void UpdateVisuals(float deltaTime)
		{
			UpdateRotation(deltaTime);
		}

		private void UpdateRotation(float deltaTime)
		{
			if (IsRotating)
				_rotationDegrees = (_rotationDegrees + RotationSpeed * deltaTime) % 360f;
		}

		/// <summary>Players currently overlapping this entity, by player ID.</summary>
		public readonly HashSet<int> _TouchingPlayerIds = new();

		public virtual void OnUpdatePhysics(float deltaTime)
		{
		}

		/// <summary>World-space center used as the entity's light source.</summary>
		public Vector3 GetLightSourcePosition() => Position + new Vector3(0f, Size.Y * 0.5f, 0f);

		public bool EmitsLight() => LightEmission > 0;

		/// <summary>
		/// Writes a compact entity snapshot. Field order and encodings are preserved.
		/// </summary>
		public void WriteSnapshot(BinaryWriter writer)
		{
			writer.Write(Position.X);
			writer.Write(Position.Y);
			writer.Write(Position.Z);

			writer.Write(Velocity.X);
			writer.Write(Velocity.Y);
			writer.Write(Velocity.Z);

			writer.Write(IsRotating);
			writer.Write(_rotationDegrees);

			WriteSnapshotExtra(writer);
		}

		/// <summary>Reads a compact entity snapshot in the established field order.</summary>
		public void ReadSnapshot(BinaryReader reader)
		{
			Position = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
			Velocity = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
			IsRotating = reader.ReadBoolean();
			_rotationDegrees = reader.ReadSingle();
			ReadSnapshotExtra(reader);
		}

		protected virtual void WriteSnapshotExtra(BinaryWriter writer)
		{
		}

		protected virtual void ReadSnapshotExtra(BinaryReader reader)
		{
		}

		/// <summary>Serializes the complete replicated entity state.</summary>
		public byte[] CaptureSnapshot()
		{
			using var stream = new MemoryStream();
			using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
				WriteSnapshot(writer);
			return stream.ToArray();
		}

		/// <summary>Applies state produced by <see cref="CaptureSnapshot"/>.</summary>
		public void ApplySnapshot(byte[] snapshot)
		{
			if (snapshot == null)
				throw new ArgumentNullException(nameof(snapshot));

			using var stream = new MemoryStream(snapshot, writable: false);
			using var reader = new BinaryReader(stream);
			ReadSnapshot(reader);
			if (stream.Position != stream.Length)
				throw new InvalidDataException("Entity snapshot contains trailing data.");
		}
	}
}
