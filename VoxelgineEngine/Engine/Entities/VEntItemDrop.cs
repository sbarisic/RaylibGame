using System.IO;
using System.Numerics;

namespace Voxelgine.Engine;

public sealed class VEntItemDrop : VoxEntity
{
	public const int DefaultPickupDelayTicks = 20;
	public const int DefaultLifetimeTicks = 20 * 60 * 5;

	public ItemStack Stack { get; private set; }
	public int PickupDelayTicks { get; set; } = DefaultPickupDelayTicks;
	public bool IsProtected { get; set; }
	public ulong ProtectedOwnerSessionValue { get; set; }
	public long ProtectionUntilServerTick { get; set; }
	public long ExpiryServerTick { get; set; }

	public override EntityPhysicsProperties PhysicsProperties => EntityPhysicsProperties.DynamicTrigger;

	public VEntItemDrop()
	{
		Size = new Vector3(0.35f);
	}

	public void SetStack(ItemStack stack)
	{
		if (stack.IsEmpty || !ItemCatalog.IsCanonical(stack))
			throw new ArgumentException("A drop requires a canonical non-empty stack.", nameof(stack));
		Stack = stack;
	}

	protected override void WriteSpawnPropertiesExtra(BinaryWriter writer)
	{
		WriteDropState(writer);
	}

	protected override void ReadSpawnPropertiesExtra(BinaryReader reader)
	{
		ReadDropState(reader);
	}

	protected override void WriteSnapshotExtra(BinaryWriter writer)
	{
		WriteDropState(writer);
	}

	protected override void ReadSnapshotExtra(BinaryReader reader)
	{
		ReadDropState(reader);
	}

	private void WriteDropState(BinaryWriter writer)
	{
		writer.Write(Stack.Item.Value);
		writer.Write(Stack.Count);
		writer.Write(PickupDelayTicks);
		writer.Write(IsProtected);
		writer.Write(ProtectionUntilServerTick);
		writer.Write(ExpiryServerTick);
	}

	private void ReadDropState(BinaryReader reader)
	{
		SetStack(new ItemStack(new ItemId(reader.ReadUInt16()), reader.ReadUInt16()));
		PickupDelayTicks = reader.ReadInt32();
		IsProtected = reader.ReadBoolean();
		ProtectionUntilServerTick = reader.ReadInt64();
		ExpiryServerTick = reader.ReadInt64();
	}
}
