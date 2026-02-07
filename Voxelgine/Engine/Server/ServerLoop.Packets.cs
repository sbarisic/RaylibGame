using System.Numerics;
using Voxelgine.Graphics;

namespace Voxelgine.Engine.Server
{
	public partial class ServerLoop
	{
		private void OnPacketReceived(NetConnection connection, Packet packet)
		{
			switch (packet)
			{
				case InputStatePacket inputPacket:
					HandleInputState(connection, inputPacket);
					break;

				case BlockPlaceRequestPacket placeReq:
					HandleBlockPlaceRequest(connection, placeReq);
					break;

				case BlockRemoveRequestPacket removeReq:
					HandleBlockRemoveRequest(connection, removeReq);
					break;

				case WeaponFirePacket weaponFire:
					HandleWeaponFire(connection, weaponFire);
					break;
			}
		}

		/// <summary>
		/// Handles an <see cref="InputStatePacket"/> from a client.
		/// Unpacks the key bitmask into an <see cref="InputState"/>, sets the camera angle,
		/// and feeds the state into the player's <see cref="NetworkInputSource"/>.
		/// </summary>
		private unsafe void HandleInputState(NetConnection connection, InputStatePacket inputPacket)
		{
			int playerId = connection.PlayerId;

			if (!_playerInputSources.TryGetValue(playerId, out var inputSource))
				return;

			Player player = _simulation.Players.GetPlayer(playerId);
			if (player == null)
				return;

			// Unpack key bitmask into InputState
			InputState state = new InputState();
			inputPacket.UnpackKeys(ref state);
			state.MouseWheel = inputPacket.MouseWheel;

			// Feed the input into the player's NetworkInputSource
			inputSource.SetState(state);

			// Set the camera angle from the packet (Vector2 yaw/pitch → Vector3 with Z=0)
			player.SetCamAngle(new Vector3(inputPacket.CameraAngle.X, inputPacket.CameraAngle.Y, 0));
		}

		/// <summary>
		/// Handles a <see cref="BlockPlaceRequestPacket"/> from a client.
		/// Validates that the player is within reach, then applies the block change to the ChunkMap.
		/// The change is automatically logged by <see cref="ChunkMap.SetPlacedBlock"/> and
		/// will be broadcast to all clients in <see cref="BroadcastBlockChanges"/>.
		/// </summary>
		private void HandleBlockPlaceRequest(NetConnection connection, BlockPlaceRequestPacket packet)
		{
			int playerId = connection.PlayerId;
			Player player = _simulation.Players.GetPlayer(playerId);
			if (player == null)
				return;

			Vector3 blockCenter = new Vector3(packet.X + 0.5f, packet.Y + 0.5f, packet.Z + 0.5f);
			float distance = Vector3.Distance(player.Position, blockCenter);
			if (distance > MaxBlockReach)
				return;

			_simulation.Map.SetBlock(packet.X, packet.Y, packet.Z, (BlockType)packet.BlockType);
		}

		/// <summary>
		/// Handles a <see cref="BlockRemoveRequestPacket"/> from a client.
		/// Validates that the player is within reach, then removes the block from the ChunkMap.
		/// </summary>
		private void HandleBlockRemoveRequest(NetConnection connection, BlockRemoveRequestPacket packet)
		{
			int playerId = connection.PlayerId;
			Player player = _simulation.Players.GetPlayer(playerId);
			if (player == null)
				return;

			Vector3 blockCenter = new Vector3(packet.X + 0.5f, packet.Y + 0.5f, packet.Z + 0.5f);
			float distance = Vector3.Distance(player.Position, blockCenter);
			if (distance > MaxBlockReach)
				return;

			_simulation.Map.SetBlock(packet.X, packet.Y, packet.Z, BlockType.None);
		}
	}
}
