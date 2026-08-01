using System.Numerics;

namespace Voxelgine.Engine.Server;

public partial class ServerLoop
{
	private const int MaximumPendingItemUses = 32;
	private const float WeaponFireInterval = 0.1f;

	private void EnqueueItemUse(NetConnection connection, Packet packet)
	{
		if (!_sessions.TryGetValue(connection.PlayerId, out ServerClientSession session))
			return;

		(uint actionId, int commandTick, ItemUseChannel channel) = packet switch
		{
			BlockPlaceRequestPacket request => (request.ItemUseActionId, request.CommandTick, request.Channel),
			BlockRemoveRequestPacket request => (request.ItemUseActionId, request.CommandTick, request.Channel),
			WeaponFirePacket request => (request.ItemUseActionId, request.CommandTick, request.Channel),
			_ => default,
		};

		if (session.ItemUseActionHistory.TryGet(actionId, out ProcessedActionOutcome previous))
		{
			SendItemUseResult(session, actionId, commandTick, previous.Accepted, (ItemUseRejectionReason)previous.Reason);
			return;
		}

		if (actionId == 0 ||
			commandTick <= 0 ||
			commandTick > session.CommandQueue.LastSimulatedCommandTick + ServerCommandQueue.MaximumAhead ||
			channel is not (ItemUseChannel.Primary or ItemUseChannel.Secondary))
		{
			CompleteRejectedItemUse(session, actionId, commandTick, ItemUseRejectionReason.CommandTooFarAhead);
			return;
		}

		if (session.PendingItemUses.Exists(item => item.ActionId == actionId))
			return;
		if (session.PendingItemUses.Count >= MaximumPendingItemUses)
		{
			CompleteRejectedItemUse(session, actionId, commandTick, ItemUseRejectionReason.QueueFull);
			return;
		}

		session.PendingItemUses.Add(new PendingItemUseRequest(actionId, commandTick, channel, packet));
		session.PendingItemUses.Sort(static (left, right) => left.ActionId.CompareTo(right.ActionId));
	}

	private void CompleteRejectedItemUse(
		ServerClientSession session,
		uint actionId,
		int commandTick,
		ItemUseRejectionReason reason)
	{
		if (actionId != 0)
		{
			session.ItemUseActionHistory.Record(new ProcessedActionOutcome(
				actionId,
				false,
				(byte)reason));
			AdvanceExpectedItemUseActionId(session);
		}
		SendItemUseResult(session, actionId, commandTick, false, reason);
	}

	private void ProcessPendingItemUses()
	{
		foreach (ServerClientSession session in _sessions.Values)
		{
			int oldestRetainedTick = session.CommandHistory.OldestTick;
			if (oldestRetainedTick > 0)
			{
				session.ConsumedItemUseChannels.RemoveWhere(key =>
					(int)(key >> 1) < oldestRetainedTick);
			}
			while (session.PendingItemUses.Count > 0)
			{
				PendingItemUseRequest pending = session.PendingItemUses[0];
				if (pending.ActionId != session.NextExpectedItemUseActionId)
					break;

				if (!session.CommandHistory.TryGet(pending.CommandTick, out SimulatedCommandRecord command))
				{
					if (pending.CommandTick > session.CommandHistory.LatestTick)
						break;
					CompleteItemUse(session, pending, false, ItemUseRejectionReason.CommandExpired);
					continue;
				}

				bool inputMatches = pending.Channel == ItemUseChannel.Primary
					? command.PrimaryUse
					: command.SecondaryUse;
				if (!inputMatches)
				{
					CompleteItemUse(session, pending, false, ItemUseRejectionReason.InvalidTarget);
					continue;
				}

				long channelKey = ((long)(uint)pending.CommandTick << 1) | (uint)pending.Channel;
				if (!session.ConsumedItemUseChannels.Add(channelKey))
				{
					CompleteItemUse(session, pending, false, ItemUseRejectionReason.ChannelAlreadyConsumed);
					continue;
				}

				(bool accepted, ItemUseRejectionReason reason) = ExecuteItemUse(session, command, pending.Packet);
				CompleteItemUse(session, pending, accepted, reason);
			}
		}
	}

	private (bool Accepted, ItemUseRejectionReason Reason) ExecuteItemUse(
		ServerClientSession session,
		in SimulatedCommandRecord command,
		Packet packet)
	{
		return packet switch
		{
			BlockPlaceRequestPacket place => ExecuteBlockPlacement(session, command, place),
			BlockRemoveRequestPacket remove => ExecuteBlockRemoval(session, command, remove),
			WeaponFirePacket fire => ExecuteWeaponUse(session, command, fire),
			_ => (false, ItemUseRejectionReason.InvalidTarget),
		};
	}

	private (bool, ItemUseRejectionReason) ExecuteBlockPlacement(
		ServerClientSession session,
		in SimulatedCommandRecord command,
		BlockPlaceRequestPacket packet)
	{
		if (command.SelectedHotbarSlot >= PlayerInventory.HotbarSlotCount)
			return (false, ItemUseRejectionReason.InvalidSelection);

		ItemStack selected = session.Inventory.GetSlot(command.SelectedHotbarSlot);
		if (selected.IsEmpty ||
			!ItemCatalog.TryGet(selected.Item, out ItemDefinition definition) ||
			definition.PlacesBlock is not BlockType placedBlock ||
			(ushort)placedBlock != packet.BlockType)
		{
			return (false, ItemUseRejectionReason.ItemMismatch);
		}

		if (!_simulation.Map.TryRaycast(command.InteractionOrigin, command.InteractionDirection, 20, out VoxelRaycastHit hit))
			return (false, ItemUseRejectionReason.InvalidTarget);

		int targetX = hit.X + (int)hit.Normal.X;
		int targetY = hit.Y + (int)hit.Normal.Y;
		int targetZ = hit.Z + (int)hit.Normal.Z;
		if (targetX != packet.X || targetY != packet.Y || targetZ != packet.Z)
			return (false, ItemUseRejectionReason.InvalidTarget);
		if (_simulation.Map.GetBlock(targetX, targetY, targetZ) != BlockType.None)
			return (false, ItemUseRejectionReason.WorldConflict);
		AABB targetBounds = new(
			new Vector3(targetX, targetY, targetZ),
			Vector3.One);
		if (_sessions.Values.Any(candidate =>
				candidate.IsGameplayActive &&
				candidate.Player.BBox.Overlaps(targetBounds)) ||
			_simulation.Entities.GetAllEntities().Any(entity =>
				entity.PhysicsProperties.BlocksPlayers &&
				entity.WorldBounds.Overlaps(targetBounds)))
		{
			return (false, ItemUseRejectionReason.CollisionBlocked);
		}

		PreparedConsumption consumption = session.Inventory.TryPrepareConsumption(
			command.SelectedHotbarSlot,
			selected.Item,
			1);
		if (!consumption.IsValid)
			return (false, ItemUseRejectionReason.ItemMismatch);

		_simulation.Map.SetBlock(targetX, targetY, targetZ, placedBlock);
		if (!session.Inventory.ApplyPreparedConsumption(consumption))
			throw new InvalidOperationException("Prepared inventory consumption changed during serialized item use.");

		session.AttackAnimationEndTime = CurrentTime + AttackAnimDuration;
		SendInventoryState(session, 0, true);
		_server.Broadcast(new SoundEventPacket
		{
			EventType = (byte)SoundEventType.BlockPlace,
			Position = new Vector3(targetX + 0.5f, targetY + 0.5f, targetZ + 0.5f),
			SourcePlayerId = session.Player.PlayerId,
		}, false, CurrentTime);
		return (true, ItemUseRejectionReason.None);
	}

	private (bool, ItemUseRejectionReason) ExecuteBlockRemoval(
		ServerClientSession session,
		in SimulatedCommandRecord command,
		BlockRemoveRequestPacket packet)
	{
		if (!_simulation.Map.TryRaycast(command.InteractionOrigin, command.InteractionDirection, 20, out VoxelRaycastHit hit) ||
			hit.X != packet.X || hit.Y != packet.Y || hit.Z != packet.Z)
		{
			return (false, ItemUseRejectionReason.InvalidTarget);
		}

		BlockType block = _simulation.Map.GetBlock(hit.X, hit.Y, hit.Z);
		if (block == BlockType.None)
			return (false, ItemUseRejectionReason.WorldConflict);
		BlockGameplayDefinition policy = ItemCatalog.GetBlock(block);
		ItemStack selected = session.Inventory.GetSlot(command.SelectedHotbarSlot);
		ToolCapabilities capabilities = selected.IsEmpty
			? ToolCapabilities.None
			: ItemCatalog.Get(selected.Item).Capabilities;
		if (!policy.BreakableByHand && !capabilities.HasFlag(ToolCapabilities.BreakBlocks))
			return (false, ItemUseRejectionReason.NotBreakable);

		if (_simulation.Map.GetBlock(hit.X, hit.Y, hit.Z) != block)
			return (false, ItemUseRejectionReason.WorldConflict);
		_simulation.Map.SetBlock(hit.X, hit.Y, hit.Z, BlockType.None);
		session.AttackAnimationEndTime = CurrentTime + AttackAnimDuration;
		OnBlockRemovedForDrop(session, block, new Vector3(hit.X + 0.5f, hit.Y + 0.5f, hit.Z + 0.5f));
		_server.Broadcast(new SoundEventPacket
		{
			EventType = (byte)SoundEventType.BlockBreak,
			Position = new Vector3(hit.X + 0.5f, hit.Y + 0.5f, hit.Z + 0.5f),
			SourcePlayerId = session.Player.PlayerId,
		}, false, CurrentTime);
		return (true, ItemUseRejectionReason.None);
	}

	private (bool, ItemUseRejectionReason) ExecuteWeaponUse(
		ServerClientSession session,
		in SimulatedCommandRecord command,
		WeaponFirePacket packet)
	{
		ItemStack selected = session.Inventory.GetSlot(command.SelectedHotbarSlot);
		if (selected.Item != ItemIds.Gun)
			return (false, ItemUseRejectionReason.ItemMismatch);
		if (CurrentTime - session.LastWeaponFireTime < WeaponFireInterval)
			return (false, ItemUseRejectionReason.NoEffect);
		session.LastWeaponFireTime = CurrentTime;
		ExecuteWeaponFire(session, command, packet);
		return (true, ItemUseRejectionReason.None);
	}

	private void CompleteItemUse(
		ServerClientSession session,
		PendingItemUseRequest pending,
		bool accepted,
		ItemUseRejectionReason reason)
	{
		session.PendingItemUses.RemoveAt(0);
		session.ItemUseActionHistory.Record(new ProcessedActionOutcome(
			pending.ActionId,
			accepted,
			(byte)reason));
		AdvanceExpectedItemUseActionId(session);
		SendItemUseResult(session, pending.ActionId, pending.CommandTick, accepted, reason);
	}

	private static void AdvanceExpectedItemUseActionId(ServerClientSession session)
	{
		while (session.ItemUseActionHistory.TryGet(
			session.NextExpectedItemUseActionId,
			out _))
		{
			session.NextExpectedItemUseActionId++;
		}
	}

	private void SendItemUseResult(
		ServerClientSession session,
		uint actionId,
		int commandTick,
		bool accepted,
		ItemUseRejectionReason reason)
	{
		_server.SendTo(session.Player.PlayerId, new ItemUseResultPacket
		{
			ItemUseActionId = actionId,
			CommandTick = commandTick,
			Accepted = accepted,
			RejectionReason = reason,
			InventoryRevision = session.Inventory.Revision,
		}, true, CurrentTime);
	}
}
