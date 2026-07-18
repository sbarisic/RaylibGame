using System.IO;
using System.Numerics;

namespace Voxelgine.Engine
{
	/// <summary>
	/// Authoritative sliding-door state. A client adapter owns its model and texture.
	/// </summary>
	public class VEntSlidingDoor : VoxEntity
	{
		public enum DoorState
		{
			Closed,
			Opening,
			Open,
			Closing,
		}

		private float _openProgress;
		private float _openTimer;
		private Vector3 _closedPosition;
		private bool _collisionEnabled = true;

		public DoorState State { get; private set; } = DoorState.Closed;

		public float TriggerRadius = 3f;
		public float OpenAngleDeg = 90f;
		public float OpenSpeed = 2f;
		public float OpenDelay = 0.5f;

		/// <summary>Direction used by the client to orient the door and its hinge.</summary>
		public Vector3 FacingDirection = Vector3.UnitZ;

		/// <summary>Normalized open amount consumed by renderer-neutral adapters.</summary>
		public float OpenAmount => _openProgress;

		public override EntityPhysicsProperties PhysicsProperties => new(
			SimulateMotion: false,
			AffectedByGravity: false,
			CollidesWithVoxels: false,
			BlocksPlayers: _collisionEnabled,
			BlocksEntities: _collisionEnabled,
			GeneratesTouchEvents: false
		);

		public VEntSlidingDoor()
		{
			IsRotating = false;
		}

		public void Initialize(Vector3 position, Vector3 size, float openAngleDeg = 90f)
		{
			Position = position;
			_closedPosition = position;
			Size = size;
			OpenAngleDeg = openAngleDeg;
		}

		public override void UpdateLockstep(float totalTime, float deltaTime, InputMgr inputManager)
		{
			base.UpdateLockstep(totalTime, deltaTime, inputManager);

			GameSimulation simulation = GetSimulation();
			if (simulation == null)
				return;

			Vector3 doorCenter = Position + new Vector3(0f, Size.Y * 0.5f, 0f);
			bool playerInRange = false;
			foreach (Player player in simulation.Players.GetAllPlayers())
			{
				if (!player.IsDead && Vector3.DistanceSquared(doorCenter, player.Position) <= TriggerRadius * TriggerRadius)
				{
					playerInRange = true;
					break;
				}
			}

			switch (State)
			{
				case DoorState.Closed:
					if (playerInRange)
					{
						State = DoorState.Opening;
						_collisionEnabled = false;
					}
					break;

				case DoorState.Opening:
					_openProgress += OpenSpeed * deltaTime;
					if (_openProgress >= 1f)
					{
						_openProgress = 1f;
						State = DoorState.Open;
					}
					break;

				case DoorState.Open:
					if (!playerInRange)
					{
						_openTimer += deltaTime;
						if (_openTimer >= OpenDelay)
						{
							State = DoorState.Closing;
							_openTimer = 0f;
						}
					}
					else
					{
						_openTimer = 0f;
					}
					break;

				case DoorState.Closing:
					if (playerInRange)
					{
						State = DoorState.Opening;
						break;
					}

					_openProgress -= OpenSpeed * deltaTime;
					if (_openProgress <= 0f)
					{
						_openProgress = 0f;
						State = DoorState.Closed;
						_collisionEnabled = true;
					}
					break;
			}
		}

		public bool IsCollisionEnabled() => _collisionEnabled;

		public AABB GetCollisionAABB()
		{
			return _collisionEnabled
				? WorldBounds
				: AABB.Empty;
		}

		protected override void WriteSnapshotExtra(BinaryWriter writer)
		{
			writer.Write((byte)State);
			writer.Write(_openProgress);
		}

		protected override void ReadSnapshotExtra(BinaryReader reader)
		{
			State = (DoorState)reader.ReadByte();
			_openProgress = reader.ReadSingle();
			_collisionEnabled = State == DoorState.Closed;
		}

		protected override void WriteSpawnPropertiesExtra(BinaryWriter writer)
		{
			writer.Write(OpenAngleDeg);
			writer.Write(TriggerRadius);
			writer.Write(_closedPosition.X);
			writer.Write(_closedPosition.Y);
			writer.Write(_closedPosition.Z);
			writer.Write(FacingDirection.X);
			writer.Write(FacingDirection.Y);
			writer.Write(FacingDirection.Z);
		}

		protected override void ReadSpawnPropertiesExtra(BinaryReader reader)
		{
			OpenAngleDeg = reader.ReadSingle();
			TriggerRadius = reader.ReadSingle();
			_closedPosition = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
			FacingDirection = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
		}
	}
}
