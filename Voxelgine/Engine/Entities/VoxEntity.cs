using Raylib_cs;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Voxelgine.Engine.DI;
using Voxelgine.Graphics;

namespace Voxelgine.Engine
{
	/// <summary>
	/// Base class for all voxel world entities. Provides position, velocity, size,
	/// model rendering, and integration with the EntityManager physics system.
	/// </summary>
	/// <remarks>
	/// Subclasses should override <see cref="OnPlayerTouch"/> for interaction,
	/// <see cref="UpdateLockstep"/> for physics/logic, and <see cref="Draw3D"/> for custom rendering.
	/// </remarks>
	public abstract class VoxEntity
	{
		/// <summary>
		/// Unique network identifier assigned by <see cref="EntityManager.Spawn"/>.
		/// 0 means unassigned. Used for network synchronization (entity spawn/remove/snapshot).
		/// </summary>
		public int NetworkId { get; internal set; }

		/// <summary>World position of the entity (bottom-center for physics).</summary>
		public Vector3 Position;
		/// <summary>Entity bounding box size for collision.</summary>
		public Vector3 Size;
		/// <summary>Current velocity (updated by EntityManager physics).</summary>
		public Vector3 Velocity;

		/// <summary>Light emission level (0-15). Set to 0 for no light emission.</summary>
		public byte LightEmission = 0;
		/// <summary>If true, emitted light casts shadows (uses ray tracing, more expensive).</summary>
		public bool LightCastsShadows = true;

		protected bool HasModel;
		protected string EntModelName;
		protected Model EntModel;

		protected Vector3 CenterOffset;
		protected Vector3 ModelOffset;

		protected float ModelRotationDeg;
		protected Color ModelColor;
		protected Vector3 ModelScale;

		// Rotate around Y axis at set sped
		public bool IsRotating = false;
		public float RotationSpeed = 30;

		private IFishEngineRunner _eng;
		public IFishEngineRunner Eng
		{
			get => _eng;
			set
			{
				_eng = value;
				Logging = value.DI.GetRequiredService<IFishLogging>();
			}
		}
		protected IFishLogging Logging;
		EntityManager EntMgr;
		GameSimulation _simulation;

		/// <summary>
		/// Gets the entity type name used for network spawning.
		/// Defaults to the class name (e.g., "VEntNPC", "VEntPickup").
		/// </summary>
		public virtual string EntityTypeName => GetType().Name;

		public virtual void OnInit()
		{
		}

		/// <summary>
		/// Writes spawn-time properties needed to reconstruct this entity on a client.
		/// Base implementation writes Size and model name. Subclasses override
		/// <see cref="WriteSpawnPropertiesExtra"/> for type-specific properties.
		/// </summary>
		public void WriteSpawnProperties(BinaryWriter writer)
		{
			// Size (12 bytes)
			writer.Write(Size.X);
			writer.Write(Size.Y);
			writer.Write(Size.Z);

			// Model name (string, length-prefixed)
			writer.Write(EntModelName ?? string.Empty);

			WriteSpawnPropertiesExtra(writer);
		}

		/// <summary>
		/// Reads spawn-time properties and applies them to this entity.
		/// Base implementation reads Size and loads the model. Subclasses override
		/// <see cref="ReadSpawnPropertiesExtra"/> for type-specific properties.
		/// </summary>
		public void ReadSpawnProperties(BinaryReader reader)
		{
			// Size
			Size = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

			// Model name
			string modelName = reader.ReadString();
			if (!string.IsNullOrEmpty(modelName))
				SetModel(modelName);

			ReadSpawnPropertiesExtra(reader);
		}

		/// <summary>
		/// Override to write subclass-specific spawn properties.
		/// </summary>
		protected virtual void WriteSpawnPropertiesExtra(BinaryWriter writer) { }

		/// <summary>
		/// Override to read subclass-specific spawn properties.
		/// </summary>
		protected virtual void ReadSpawnPropertiesExtra(BinaryReader reader) { }

		/// <summary>
		/// Stores the model name without loading the GPU resource.
		/// Use this on the headless server where Raylib is not available.
		/// </summary>
		public void SetModelName(string mdlName)
		{
			EntModelName = mdlName;
		}

		public virtual void SetModel(string MdlName)
		{
			HasModel = false;
			ModelOffset = Vector3.Zero;
			ModelRotationDeg = 0;
			ModelColor = Color.White;
			ModelScale = Vector3.One;

			if (Size != Vector3.Zero)
			{
				CenterOffset = new Vector3(Size.X / 2, ModelOffset.Y, Size.Y / 2);
			}

			EntModelName = MdlName;
			EntModel = ResMgr.GetModel(MdlName);
			HasModel = EntModel.MeshCount > 0;
		}

		public virtual Vector3 GetPosition()
		{
			return Position;
		}

		public virtual void SetPosition(Vector3 Pos)
		{
			Position = Pos;
		}

		public virtual Vector3 GetSize()
		{
			return Size;
		}

		public virtual void SetSize(Vector3 Size)
		{
			this.Size = Size;
		}

		public virtual Vector3 GetVelocity()
		{
			return Velocity;
		}

		public virtual void SetVelocity(Vector3 Velocity)
		{
			this.Velocity = Velocity;
		}

		public virtual EntityManager GetEntityManager()
		{
			return EntMgr;
		}

		public virtual void SetEntityManager(EntityManager EntMgr)
		{
			this.EntMgr = EntMgr;
		}

		public virtual GameSimulation GetSimulation()
		{
			return _simulation;
		}

		public virtual void SetSimulation(GameSimulation simulation)
		{
			_simulation = simulation;
		}

		public virtual void OnPlayerTouch(Player Ply)
		{
			Logging.WriteLine("Player touched me!");
		}

		public virtual void UpdateLockstep(float TotalTime, float Dt, InputMgr InMgr)
		{
			if (IsRotating)
				ModelRotationDeg = (ModelRotationDeg + RotationSpeed * Dt) % 360;

		}

		/// <summary>
		/// Updates cosmetic visuals (rotation) without running AI or physics.
		/// Used on multiplayer clients where the server is authoritative.
		/// </summary>
		public virtual void UpdateVisuals(float Dt)
		{
			if (IsRotating)
				ModelRotationDeg = (ModelRotationDeg + RotationSpeed * Dt) % 360;
		}

		// Tracks which players are currently overlapping this entity (by player ID).
		// Used by EntityManager to trigger OnPlayerTouch only once per entry per player.
		public readonly HashSet<int> _TouchingPlayerIds = new();

		public virtual void OnUpdatePhysics(float Dt)
		{
		}

		/// <summary>
		/// Gets the world position where light is emitted from (center of entity).
		/// </summary>
		public Vector3 GetLightSourcePosition()
		{
			return Position + Size / 2f;
		}

		/// <summary>
		/// Returns true if this entity emits light.
		/// </summary>
		public bool EmitsLight() => LightEmission > 0;

		protected Vector3 GetDrawPosition()
		{
			return Position + ModelOffset + CenterOffset;
		}

		protected virtual void EntityDrawModel(float TimeAlpha, ref GameFrameInfo LastFrame)
		{
			if (HasModel)
			{
				Raylib.DrawModelEx(EntModel, GetDrawPosition(), Vector3.UnitY, ModelRotationDeg, ModelScale, ModelColor);
			}
		}

		public virtual void Draw3D(float TimeAlpha, ref GameFrameInfo LastFrame)
		{
			EntityDrawModel(TimeAlpha, ref LastFrame);
			DrawCollisionBox();
		}

		// Draws the collision box at Position with Size
		/// <summary>
		/// Writes a compact network snapshot of this entity's state.
		/// Subclasses should override <see cref="WriteSnapshotExtra"/> to include type-specific state.
		/// </summary>
		public void WriteSnapshot(BinaryWriter writer)
		{
			// Position (12 bytes)
			writer.Write(Position.X);
			writer.Write(Position.Y);
			writer.Write(Position.Z);

			// Velocity (12 bytes)
			writer.Write(Velocity.X);
			writer.Write(Velocity.Y);
			writer.Write(Velocity.Z);

			// Rotation state (5 bytes)
			writer.Write(IsRotating);
			writer.Write(ModelRotationDeg);

			WriteSnapshotExtra(writer);
		}

		/// <summary>
		/// Reads a network snapshot and applies it to this entity's state.
		/// Subclasses should override <see cref="ReadSnapshotExtra"/> to read type-specific state.
		/// </summary>
		public void ReadSnapshot(BinaryReader reader)
		{
			// Position
			Position = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

			// Velocity
			Velocity = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

			// Rotation state
			IsRotating = reader.ReadBoolean();
			ModelRotationDeg = reader.ReadSingle();

			ReadSnapshotExtra(reader);
		}

		/// <summary>
		/// Override to write subclass-specific state in the network snapshot.
		/// </summary>
		protected virtual void WriteSnapshotExtra(BinaryWriter writer) { }

		/// <summary>
		/// Override to read subclass-specific state from the network snapshot.
		/// </summary>
		protected virtual void ReadSnapshotExtra(BinaryReader reader) { }

		protected virtual void DrawCollisionBox()
		{
			if (!Eng.DebugMode)
				return;

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
	}
}
