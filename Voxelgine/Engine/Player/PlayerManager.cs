using System.Collections.Generic;
using System.Linq;

namespace Voxelgine.Engine
{
	/// <summary>
	/// Manages all players in the game. In single-player, holds a single player with ID 0.
	/// In multiplayer, holds up to 10 players keyed by their assigned player ID.
	/// </summary>
	public class PlayerManager
	{
		private readonly Dictionary<int, Player> _players = new();
		private readonly Dictionary<int, RemotePlayer> _remotePlayers = new();
		private int _localPlayerId;

		/// <summary>
		/// Convenience property to get the local player instance.
		/// </summary>
		public Player LocalPlayer => GetPlayer(_localPlayerId);

		/// <summary>
		/// Adds a player to the manager.
		/// </summary>
		public void AddPlayer(int id, Player player)
		{
			_players[id] = player;
		}

		/// <summary>
		/// Adds a player and marks them as the local player.
		/// </summary>
		public void AddLocalPlayer(int id, Player player)
		{
			_localPlayerId = id;
			_players[id] = player;
		}

		/// <summary>
		/// Removes a player by ID.
		/// </summary>
		/// <returns>True if the player was found and removed.</returns>
		public bool RemovePlayer(int id)
		{
			_remotePlayers.Remove(id);
			return _players.Remove(id);
		}

		/// <summary>
		/// Gets a player by ID, or null if not found.
		/// </summary>
		public Player GetPlayer(int id)
		{
			_players.TryGetValue(id, out Player player);
			return player;
		}

		/// <summary>
		/// Gets the local player instance (same as <see cref="LocalPlayer"/>).
		/// </summary>
		public Player GetLocalPlayer()
		{
			return LocalPlayer;
		}

		/// <summary>
		/// Returns all connected players.
		/// </summary>
		public IEnumerable<Player> GetAllPlayers()
		{
			return _players.Values;
		}

		/// <summary>
		/// Gets the number of connected players.
		/// </summary>
		public int Count => _players.Count;

		/// <summary>
		/// Gets the local player's ID.
		/// </summary>
		public int LocalPlayerId => _localPlayerId;

		/// <summary>
		/// Adds a remote player for client-side rendering.
		/// </summary>
		public void AddRemotePlayer(RemotePlayer remotePlayer)
		{
			_remotePlayers[remotePlayer.PlayerId] = remotePlayer;
		}

		/// <summary>
		/// Removes a remote player by ID.
		/// </summary>
		/// <returns>True if the remote player was found and removed.</returns>
		public bool RemoveRemotePlayer(int id)
		{
			return _remotePlayers.Remove(id);
		}

		/// <summary>
		/// Gets a remote player by ID, or null if not found.
		/// </summary>
		public RemotePlayer GetRemotePlayer(int id)
		{
			_remotePlayers.TryGetValue(id, out RemotePlayer player);
			return player;
		}

		/// <summary>
		/// Returns all remote players (for rendering).
		/// </summary>
		public IEnumerable<RemotePlayer> GetAllRemotePlayers()
		{
			return _remotePlayers.Values;
		}

		/// <summary>
		/// Gets the number of remote players.
		/// </summary>
		public int RemotePlayerCount => _remotePlayers.Count;

		/// <summary>
		/// Removes all remote players (e.g., on disconnect).
		/// </summary>
		public void ClearRemotePlayers()
		{
			_remotePlayers.Clear();
		}
	}
}
