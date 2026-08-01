using System.Numerics;
using Voxelgine.Graphics;

namespace Voxelgine.Engine.Server
{
	public partial class ServerLoop
	{
		private void OnPacketReceived(NetConnection connection, Packet packet)
		{
			if (packet is WorldColumnResyncRequestPacket resync)
			{
				_worldStream.HandleResyncRequest(connection.PlayerId, resync);
				return;
			}

			if (packet is WorldColumnAppliedPacket applied)
			{
				_worldStream.HandleApplied(connection.PlayerId, applied);
				return;
			}

			if (packet is ChunkInterestPacket interest)
			{
				_worldStream.HandleInterest(connection.PlayerId, interest);
				return;
			}

			if (packet is ClientWorldReadyPacket ready)
			{
				_worldStream.HandleReady(connection.PlayerId, ready);
				return;
			}

			if (!connection.IsGameplayActive)
				return;

			switch (packet)
			{
				case InputStatePacket inputPacket:
					HandleInputState(connection, inputPacket);
					break;

				case InventoryActionRequestPacket inventoryAction:
					HandleInventoryAction(connection, inventoryAction);
					break;

				case BlockPlaceRequestPacket placeReq:
					EnqueueItemUse(connection, placeReq);
					break;

				case BlockRemoveRequestPacket removeReq:
					EnqueueItemUse(connection, removeReq);
					break;

				case WeaponFirePacket weaponFire:
					EnqueueItemUse(connection, weaponFire);
					break;

						case ChatMessagePacket chatMsg:
							HandleChatMessage(connection, chatMsg);
							break;

						case DebugSpawnEntityRequestPacket debugSpawn:
							HandleDebugSpawnEntityRequest(connection, debugSpawn);
							break;

						case DebugPlaceBlockRequestPacket debugPlace:
							HandleDebugPlaceBlockRequest(connection, debugPlace);
							break;
					}
				}

		/// <summary>
		/// Handles an <see cref="InputStatePacket"/> from a client.
		/// Unpacks the key bitmask into an <see cref="InputState"/>, sets the camera angle,
		/// and feeds the state into the player's <see cref="NetworkInputSource"/>.
		/// </summary>
		private void HandleInputState(NetConnection connection, InputStatePacket inputPacket)
		{
			int playerId = connection.PlayerId;
			if (_sessions.TryGetValue(playerId, out ServerClientSession session))
				session.CommandQueue.Enqueue(inputPacket);
		}

		/// <summary>
		/// Handles a <see cref="ChatMessagePacket"/> from a client.
		/// Sets the sender's player ID, logs the message, and broadcasts to all clients.
		/// </summary>
		private void HandleChatMessage(NetConnection connection, ChatMessagePacket packet)
		{
			string playerName = connection.PlayerName;
			string message = packet.Message;

			if (string.IsNullOrWhiteSpace(message))
				return;

			// Intercept player commands (messages starting with /)
			if (message.StartsWith('/'))
			{
				HandlePlayerCommand(connection, message.Substring(1));
				return;
			}

			_logging.ServerWriteLine($"[Chat] [{connection.PlayerId}] \"{playerName}\": {message}");

			// Notify all NPCs of the player chat message
			foreach (var ent in _simulation.Entities.GetAllEntities())
			{
				if (ent is VEntNPC npc)
					npc.OnPlayerChat(message);
			}

			// Rebroadcast with correct player ID
			var broadcastPacket = new ChatMessagePacket
			{
				PlayerId = connection.PlayerId,
				Message = message
			};
			_server.Broadcast(broadcastPacket, true, CurrentTime);
		}

		/// <summary>
		/// Handles a <see cref="DebugSpawnEntityRequestPacket"/> from a client.
		/// Creates the requested entity on the server and broadcasts it to all clients.
		/// </summary>
		private void HandleDebugSpawnEntityRequest(NetConnection connection, DebugSpawnEntityRequestPacket packet)
		{
			_logging.ServerWriteLine($"DebugSpawnEntity [{connection.PlayerId}]: type={packet.EntityType} pos={packet.Position}");

			VoxEntity entity = packet.EntityType switch
			{
				"VEntSlidingDoor" => CreateDebugDoor(packet.Position, packet.FacingDirection),
				_ => null,
			};

			if (entity == null)
			{
				_logging.ServerWriteLine($"DebugSpawnEntity REJECTED [{connection.PlayerId}]: unknown entity type '{packet.EntityType}'");
				return;
			}

			SpawnEntityAndBroadcast(entity);
		}

		private VoxEntity CreateDebugDoor(Vector3 position, Vector3 facingDirection)
		{
			var door = new VEntSlidingDoor();
			door.SetModelName("door/door.json");
			door.Initialize(position, new Vector3(1.0f, 2.0f, 0.125f));
			door.FacingDirection = facingDirection;
			return door;
		}

		/// <summary>
		/// Handles a <see cref="DebugPlaceBlockRequestPacket"/> from a client.
		/// Places the block without inventory validation and broadcasts the change.
		/// </summary>
		private void HandleDebugPlaceBlockRequest(NetConnection connection, DebugPlaceBlockRequestPacket packet)
		{
			_logging.ServerWriteLine($"DebugPlaceBlock [{connection.PlayerId}]: ({packet.X}, {packet.Y}, {packet.Z}) type={packet.BlockType}");

			BlockType blockType = (BlockType)packet.BlockType;
			_simulation.Map.SetBlock(packet.X, packet.Y, packet.Z, blockType);
		}
	}
}
