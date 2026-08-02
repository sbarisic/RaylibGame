using System.Numerics;
using Voxelgine.Graphics;
using Voxelgine.Engine.World.Structures;

namespace Voxelgine.Engine.Server;

public partial class ServerLoop
{
	private const int DropMergeIntervalTicks = 13;
	private const float DropPickupRadius = 1.25f;
	private const float DropMergeRadius = 1f;
	private const int DeathDropProtectionTicks = 667;

	private void OnBlockRemovedForDrop(
		ServerClientSession session,
		BlockType removedBlock,
		Vector3 position)
	{
		BlockGameplayDefinition policy = ItemCatalog.GetBlock(removedBlock);
		if (!policy.DropsItem || policy.Drop.IsEmpty)
			return;

		var drop = new VEntItemDrop();
		drop.SetStack(policy.Drop);
		drop.SetPosition(position);
		drop.SetVelocity(new Vector3(0, 2, 0));
		drop.PickupDelayTicks = VEntItemDrop.DefaultPickupDelayTicks;
		drop.ExpiryServerTick = _server.ServerTick + VEntItemDrop.DefaultLifetimeTicks;
		SpawnEntityAndBroadcast(drop);
	}

	private void OnPlantLostSupport(WorldPlantRecord plant)
	{
		if(plant.Key.Kind==PersistentWorldObjectKeyKind.Generated)_simulation.Tombstones.Add(GeneratedObjectKind.WorldObject,plant.Key.GeneratedMarkerId);
		SpawnItemDrop(new ItemStack(ItemIds.WheatSeeds, 1), new Vector3(plant.Position.X + 0.5f, plant.Position.Y + 0.25f, plant.Position.Z + 0.5f));
	}

	private void SpawnItemDrop(ItemStack stack, Vector3 position)
	{
		if (stack.IsEmpty) return;
		var drop = new VEntItemDrop();
		drop.SetStack(stack);
		drop.SetPosition(position);
		drop.SetVelocity(new Vector3(0, 2, 0));
		drop.PickupDelayTicks = VEntItemDrop.DefaultPickupDelayTicks;
		drop.ExpiryServerTick = _server.ServerTick + VEntItemDrop.DefaultLifetimeTicks;
		SpawnEntityAndBroadcast(drop);
	}

	private void ResolveCursorForDeath(ServerClientSession session)
	{
		ItemStack remainder = session.Inventory.ResolveCursorForDeath();
		if (remainder.IsEmpty)
		{
			SendInventoryState(session, 0, true);
			return;
		}

		long protectionEnd = _server.ServerTick + DeathDropProtectionTicks;
		var drop = new VEntItemDrop();
		drop.SetStack(remainder);
		drop.SetPosition(session.Player.Position + Vector3.UnitY * 0.5f);
		drop.SetVelocity(new Vector3(0, 2, 0));
		drop.PickupDelayTicks = VEntItemDrop.DefaultPickupDelayTicks;
		drop.IsProtected = true;
		drop.ProtectedOwnerSessionValue = session.SessionId.Value;
		drop.ProtectionUntilServerTick = protectionEnd;
		drop.ExpiryServerTick = _server.ServerTick + VEntItemDrop.DefaultLifetimeTicks;
		SpawnEntityAndBroadcast(drop);
		SendInventoryState(session, 0, true);
	}

	private void ProcessItemDrops()
	{
		long tick = _server.ServerTick;
		List<VEntItemDrop> drops = _simulation.Entities.GetAllEntities()
			.OfType<VEntItemDrop>()
			.OrderBy(static drop => drop.NetworkId)
			.ToList();

		foreach (VEntItemDrop drop in drops)
		{
			if (drop.PickupDelayTicks > 0)
				drop.PickupDelayTicks--;
			if (drop.IsProtected && tick >= drop.ProtectionUntilServerTick)
			{
				drop.IsProtected = false;
				drop.ProtectedOwnerSessionValue = 0;
				_lastEntitySnapshots.Remove(drop.NetworkId);
			}

			if (WorldBoundsPolicy.IsBelowVoid(drop.Position) || tick >= drop.ExpiryServerTick)
			{
				RemoveDrop(drop);
				continue;
			}

			if (drop.PickupDelayTicks > 0)
				continue;

			ServerClientSession target = FindDropPickupPlayer(drop);
			if (target == null)
				continue;

			InventoryInsertionResult insertion = target.Inventory.TryInsert(drop.Stack);
			if (!insertion.Changed)
				continue;

			SendInventoryState(target, 0, true);
			_server.Broadcast(new SoundEventPacket
			{
				EventType = (byte)SoundEventType.ItemPickup,
				Position = drop.Position + Vector3.UnitY * (drop.Size.Y * 0.5f),
				SourcePlayerId = target.Player.PlayerId,
			}, false, CurrentTime);
			if (insertion.Remainder.IsEmpty)
				RemoveDrop(drop);
			else
			{
				drop.SetStack(insertion.Remainder);
				_lastEntitySnapshots.Remove(drop.NetworkId);
			}
		}

		if (tick % DropMergeIntervalTicks == 0)
			MergeItemDrops();
	}

	private ServerClientSession FindDropPickupPlayer(VEntItemDrop drop)
	{
		return _sessions.Values
			.Where(session => session.IsGameplayActive &&
				(!drop.IsProtected || drop.ProtectedOwnerSessionValue == session.SessionId.Value))
			.Select(session => new
			{
				Session = session,
				DistanceSquared = GetDropPickupDistanceSquared(session.Player, drop),
			})
			.Where(candidate => candidate.DistanceSquared <= DropPickupRadius * DropPickupRadius)
			.OrderBy(candidate => candidate.DistanceSquared)
			.ThenBy(candidate => candidate.Session.Player.PlayerId)
			.Select(candidate => candidate.Session)
			.FirstOrDefault();
	}

	internal static float GetDropPickupDistanceSquared(Player player, VEntItemDrop drop)
	{
		ArgumentNullException.ThrowIfNull(player);
		ArgumentNullException.ThrowIfNull(drop);
		return Vector3.DistanceSquared(player.FeetPosition, drop.Position);
	}

	private void MergeItemDrops()
	{
		List<VEntItemDrop> drops = _simulation.Entities.GetAllEntities()
			.OfType<VEntItemDrop>()
			.OrderBy(static drop => drop.NetworkId)
			.ToList();
		float radiusSquared = DropMergeRadius * DropMergeRadius;

		for (int i = 0; i < drops.Count; i++)
		{
			VEntItemDrop target = drops[i];
			if (_simulation.Entities.GetEntityByNetworkId(target.NetworkId) == null)
				continue;
			ushort maximum = ItemCatalog.Get(target.Stack.Item).MaximumStack;
			for (int j = i + 1; j < drops.Count && target.Stack.Count < maximum; j++)
			{
				VEntItemDrop source = drops[j];
				if (_simulation.Entities.GetEntityByNetworkId(source.NetworkId) == null ||
					target.Stack.Item != source.Stack.Item ||
					target.IsProtected != source.IsProtected ||
					target.ProtectedOwnerSessionValue != source.ProtectedOwnerSessionValue ||
					target.ProtectionUntilServerTick != source.ProtectionUntilServerTick ||
					Vector3.DistanceSquared(target.Position, source.Position) > radiusSquared)
				{
					continue;
				}

				ushort moved = (ushort)Math.Min(maximum - target.Stack.Count, source.Stack.Count);
				target.SetStack(new ItemStack(target.Stack.Item, (ushort)(target.Stack.Count + moved)));
				ushort remaining = (ushort)(source.Stack.Count - moved);
				_lastEntitySnapshots.Remove(target.NetworkId);
				if (remaining == 0)
					RemoveDrop(source);
				else
				{
					source.SetStack(new ItemStack(source.Stack.Item, remaining));
					_lastEntitySnapshots.Remove(source.NetworkId);
				}
			}
		}
	}

	private void RemoveDrop(VEntItemDrop drop)
	{
		if (!_simulation.Entities.Remove(drop))
			return;
		_lastEntitySnapshots.Remove(drop.NetworkId);
		_server.Broadcast(new EntityRemovePacket { NetworkId = drop.NetworkId }, true, CurrentTime);
	}
}
