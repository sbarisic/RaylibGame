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
	}
}
