using Voxelgine.Engine;
using Voxelgine.Engine.DI;
using Voxelgine.Graphics;
using Voxelgine.Engine.World.Structures;

namespace Voxelgine.States;

/// <summary>
/// Converts local selected-item intent into bounded, connection-local requests.
/// World mutation and inventory consumption remain server-authoritative.
/// </summary>
internal sealed class GameplayItemUseController : IDisposable
{
	public const int MaximumPendingActions = 32;

	private readonly Func<bool> canSend;
	private readonly Func<int> nextCommandTick;
	private readonly Func<ClientPlayer> player;
	private readonly Func<ChunkMap> map;
	private readonly Func<EntityManager> entities;
	private readonly Action<Packet> send;
	private readonly IFishLogging logging;
	private readonly HashSet<uint> pendingActions = new();
	private uint nextActionId = 1;
	private bool disposed;

	internal GameplayItemUseController(
		Func<bool> canSend,
		Func<int> nextCommandTick,
		Func<ClientPlayer> player,
		Func<ChunkMap> map,
		Func<EntityManager> entities,
		Action<Packet> send,
		IFishLogging logging)
	{
		this.canSend = canSend ?? throw new ArgumentNullException(nameof(canSend));
		this.nextCommandTick = nextCommandTick ?? throw new ArgumentNullException(nameof(nextCommandTick));
		this.player = player ?? throw new ArgumentNullException(nameof(player));
		this.map = map ?? throw new ArgumentNullException(nameof(map));
		this.entities = entities ?? throw new ArgumentNullException(nameof(entities));
		this.send = send ?? throw new ArgumentNullException(nameof(send));
		this.logging = logging ?? throw new ArgumentNullException(nameof(logging));
	}

	internal int PendingActionCount => pendingActions.Count;

	internal void RequestUse(ItemUseChannel channel)
	{
		if (disposed || !canSend() || pendingActions.Count >= MaximumPendingActions)
			return;

		ClientPlayer activePlayer = player();
		ChunkMap activeMap = map();
		if (activePlayer == null || activeMap == null)
			return;

		ItemStack selected = activePlayer.GetSelectedStack();
		int commandTick = nextCommandTick();
		if(channel==ItemUseChannel.Primary&&selected.Item==ItemIds.Hammer)
		{
			RaycastHit furnitureHit=entities()?.Raycast(activePlayer.Position,activePlayer.GetForward(),20)??RaycastHit.None;
			BlockCoordinate? anchor=furnitureHit.Entity switch{VEntItemBasket basket=>basket.CaptureRecord().Anchor,VEntBed bed=>bed.Anchor,_=>null};
			if(anchor is BlockCoordinate target){send(new WorldInteractRequestPacket{X=target.X,Y=target.Y,Z=target.Z,Interaction=WorldInteractionKind.RemoveFurniture});return;}
		}
		if (!selected.IsEmpty
			&& selected.Item == ItemIds.Gun
			&& channel == ItemUseChannel.Primary)
		{
			send(new WeaponFirePacket
			{
				ItemUseActionId = BeginAction(),
				CommandTick = commandTick,
				Channel = channel,
				WeaponType = 0,
			});
			return;
		}

		if (!activeMap.TryRaycast(activePlayer.Position, activePlayer.GetForward(), 20, out VoxelRaycastHit hit))
			return;

		if (channel == ItemUseChannel.Primary)
		{
			send(new BlockRemoveRequestPacket
			{
				ItemUseActionId = BeginAction(),
				CommandTick = commandTick,
				Channel = channel,
				X = hit.X,
				Y = hit.Y,
				Z = hit.Z,
			});
			return;
		}

		if (selected.Item == ItemIds.WheatSeeds)
		{
			send(new WorldObjectPlaceRequestPacket
			{
				ActionId = BeginAction(),
				X = hit.X,
				Y = hit.Y,
				Z = hit.Z,
				PlantType = (byte)WorldPlantType.Wheat,
			});
			return;
		}

		if (selected.Item == ItemIds.ItemBasket)
		{
			send(new WorldObjectPlaceRequestPacket
			{
				ActionId = BeginAction(),
				X = hit.X + (int)hit.Normal.X,
				Y = hit.Y + (int)hit.Normal.Y,
				Z = hit.Z + (int)hit.Normal.Z,
				PlantType = (byte)WorldObjectPlacementKind.ItemBasket,
			});
			return;
		}

		if (selected.Item == ItemIds.Bed)
		{
			send(new WorldObjectPlaceRequestPacket{ActionId=BeginAction(),X=hit.X+(int)hit.Normal.X,Y=hit.Y+(int)hit.Normal.Y,Z=hit.Z+(int)hit.Normal.Z,PlantType=(byte)WorldObjectPlacementKind.Bed});return;
		}

		if (selected.IsEmpty || ItemCatalog.Get(selected.Item).PlacesBlock is not BlockType block)
			return;

		send(new BlockPlaceRequestPacket
		{
			ItemUseActionId = BeginAction(),
			CommandTick = commandTick,
			Channel = channel,
			X = hit.X + (int)hit.Normal.X,
			Y = hit.Y + (int)hit.Normal.Y,
			Z = hit.Z + (int)hit.Normal.Z,
			BlockType = (ushort)block,
			BlockState = BlockShapeCatalog.IsStair(block)
				? BlockShapeCatalog.GetNormalStairState(activePlayer.GetForward())
				: (byte)0,
		});
	}

	internal void HandleResult(ItemUseResultPacket packet)
	{
		if (packet == null)
			throw new ArgumentNullException(nameof(packet));

		pendingActions.Remove(packet.ItemUseActionId);
		if (!packet.Accepted)
		{
			logging.Log(
				GameLogLevel.Debug,
				"Inventory",
				$"item-use rejected actionId={packet.ItemUseActionId} commandTick={packet.CommandTick} reason={packet.RejectionReason}");
		}
	}

	public void Dispose()
	{
		if (disposed)
			return;
		disposed = true;
		pendingActions.Clear();
	}

	private uint BeginAction()
	{
		uint actionId = nextActionId++;
		pendingActions.Add(actionId);
		return actionId;
	}
}
