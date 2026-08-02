using System;
using System.Collections.Generic;

namespace Voxelgine.Engine.Server;

public readonly record struct PlayerSessionId(ulong Value);

public sealed class ServerClientSession
{
	public ServerClientSession(
		PlayerSessionId sessionId,
		NetConnection connection,
		Player player,
		PlayerInventory inventory)
	{
		SessionId = sessionId;
		Connection = connection ?? throw new ArgumentNullException(nameof(connection));
		Player = player ?? throw new ArgumentNullException(nameof(player));
		Inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
		InputSource = new NetworkInputSource();
		InputManager = new InputMgr(InputSource);
		CommandQueue = new ServerCommandQueue();
		CommandHistory = new SimulatedCommandHistory();
	}

	public PlayerSessionId SessionId { get; }
	public NetConnection Connection { get; }
	public Player Player { get; }
	public PlayerInventory Inventory { get; }
	public NetworkInputSource InputSource { get; }
	public InputMgr InputManager { get; }
	public ServerCommandQueue CommandQueue { get; }
	public SimulatedCommandHistory CommandHistory { get; }
	public string PlayerName => Connection.PlayerName;
	public bool IsGameplayActive { get; set; }
	public byte SelectedHotbarSlot { get; set; }
	public int SelectionCommandTick { get; set; }
	public float? RespawnStartedAt { get; set; }
	public float AttackAnimationEndTime { get; set; }
	public uint NextExpectedInventoryActionId { get; set; } = 1;
	public uint NextExpectedItemUseActionId { get; set; } = 1;
	public ProcessedActionHistory InventoryActionHistory { get; } = new(128);
	public ProcessedActionHistory ItemUseActionHistory { get; } = new(128);
	public HashSet<long> ConsumedItemUseChannels { get; } = new();
	public List<PendingItemUseRequest> PendingItemUses { get; } = new(32);
	public float LastWeaponFireTime { get; set; } = float.NegativeInfinity;
	public ContainerViewerSession ContainerSession { get; set; }

	public void ClearTransientState()
	{
		CommandQueue.Clear();
		CommandHistory.Clear();
		InventoryActionHistory.Clear();
		ItemUseActionHistory.Clear();
		ConsumedItemUseChannels.Clear();
		PendingItemUses.Clear();
	}
}

public sealed record ContainerViewerSession(ulong SessionId, PersistentFurnitureKey ContainerKey, long Generation);

public readonly record struct PendingItemUseRequest(
	uint ActionId,
	int CommandTick,
	ItemUseChannel Channel,
	Packet Packet);

public readonly record struct ProcessedActionOutcome(uint ActionId, bool Accepted, byte Reason);

public sealed class ProcessedActionHistory
{
	private readonly int _capacity;
	private readonly Dictionary<uint, ProcessedActionOutcome> _items = new();
	private readonly Queue<uint> _order = new();

	public ProcessedActionHistory(int capacity)
	{
		_capacity = capacity > 0 ? capacity : throw new ArgumentOutOfRangeException(nameof(capacity));
	}

	public bool TryGet(uint actionId, out ProcessedActionOutcome outcome) => _items.TryGetValue(actionId, out outcome);

	public void Record(ProcessedActionOutcome outcome)
	{
		if (_items.ContainsKey(outcome.ActionId))
			return;
		_items.Add(outcome.ActionId, outcome);
		_order.Enqueue(outcome.ActionId);
		while (_order.Count > _capacity)
			_items.Remove(_order.Dequeue());
	}

	public void Clear()
	{
		_items.Clear();
		_order.Clear();
	}
}
