using Voxelgine.Graphics;

namespace Voxelgine.Engine.Server
{
	/// <summary>
	/// Server-side inventory state for a single player.
	/// Tracks item counts per slot using the fixed 10-slot loadout
	/// that matches the client-side <see cref="Player"/> inventory setup.
	/// </summary>
	public class ServerInventory
	{
		/// <summary>Number of inventory slots.</summary>
		public const int SlotCount = 10;

		/// <summary>
		/// The block type associated with each inventory slot.
		/// <see cref="BlockType.None"/> means the slot is a tool (gun, hammer) with no block type.
		/// </summary>
		public static readonly BlockType[] SlotBlockTypes = new BlockType[]
		{
			BlockType.None,       // Slot 0: Gun
			BlockType.None,       // Slot 1: Hammer
			BlockType.Dirt,       // Slot 2
			BlockType.Stone,      // Slot 3
			BlockType.Plank,      // Slot 4
			BlockType.Bricks,     // Slot 5
			BlockType.StoneBrick, // Slot 6
			BlockType.Glowstone,  // Slot 7
			BlockType.Glass,      // Slot 8
			BlockType.Water,      // Slot 9
		};

		/// <summary>Default counts for new players. -1 means infinite.</summary>
		private static readonly int[] DefaultCounts = new int[]
		{
			-1, // Gun
			-1, // Hammer
			64, 64, 64, 64, 64, 64, 64, 64
		};

		private readonly int[] _counts = new int[SlotCount];

		/// <summary>
		/// Creates a new inventory with the default loadout.
		/// </summary>
		public ServerInventory()
		{
			ResetToDefaults();
		}

		/// <summary>
		/// Resets all slot counts to the default loadout values.
		/// </summary>
		public void ResetToDefaults()
		{
			Array.Copy(DefaultCounts, _counts, SlotCount);
		}

		/// <summary>Gets the item count for a slot. -1 means infinite.</summary>
		public int GetCount(int slot)
		{
			if (slot < 0 || slot >= SlotCount)
				return 0;
			return _counts[slot];
		}

		/// <summary>Sets the item count for a slot.</summary>
		public void SetCount(int slot, int count)
		{
			if (slot >= 0 && slot < SlotCount)
				_counts[slot] = count;
		}

		/// <summary>
		/// Finds the inventory slot index that holds the given block type.
		/// Returns -1 if not found.
		/// </summary>
		public static int FindSlotByBlockType(BlockType blockType)
		{
			if (blockType == BlockType.None)
				return -1;

			for (int i = 0; i < SlotCount; i++)
			{
				if (SlotBlockTypes[i] == blockType)
					return i;
			}
			return -1;
		}

		/// <summary>
		/// Attempts to decrement the count for a slot. Returns true if the slot had items
		/// available (count > 0 or infinite). Does not decrement infinite (-1) slots.
		/// </summary>
		public bool TryDecrement(int slot)
		{
			if (slot < 0 || slot >= SlotCount)
				return false;

			int count = _counts[slot];
			if (count == -1)
				return true; // Infinite — always available, no decrement

			if (count <= 0)
				return false; // No items left

			_counts[slot] = count - 1;
			return true;
		}

		/// <summary>
		/// Creates a full <see cref="InventoryUpdatePacket"/> containing all slot counts.
		/// Used for initial sync when a player connects.
		/// </summary>
		public InventoryUpdatePacket CreateFullUpdatePacket()
		{
			var slots = new InventoryUpdatePacket.InventorySlotEntry[SlotCount];
			for (int i = 0; i < SlotCount; i++)
			{
				slots[i] = new InventoryUpdatePacket.InventorySlotEntry
				{
					SlotIndex = (byte)i,
					Count = _counts[i],
				};
			}
			return new InventoryUpdatePacket { Slots = slots };
		}

		/// <summary>
		/// Creates an <see cref="InventoryUpdatePacket"/> for a single slot change.
		/// </summary>
		public InventoryUpdatePacket CreateSlotUpdatePacket(int slot)
		{
			return new InventoryUpdatePacket
			{
				Slots = new[]
				{
					new InventoryUpdatePacket.InventorySlotEntry
					{
						SlotIndex = (byte)slot,
						Count = _counts[slot],
					}
				}
			};
		}

		/// <summary>Writes all slot counts to a <see cref="BinaryWriter"/> for persistence.</summary>
		public void Write(BinaryWriter writer)
		{
			for (int i = 0; i < SlotCount; i++)
				writer.Write(_counts[i]);
		}

		/// <summary>Reads all slot counts from a <see cref="BinaryReader"/>.</summary>
		public void Read(BinaryReader reader)
		{
			for (int i = 0; i < SlotCount; i++)
				_counts[i] = reader.ReadInt32();
		}
	}
}
