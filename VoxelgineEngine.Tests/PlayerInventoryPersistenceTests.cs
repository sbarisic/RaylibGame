using System.Numerics;
using Voxelgine.Engine;
using Voxelgine.Engine.Server;

namespace VoxelgineEngine.Tests;

public sealed class PlayerInventoryPersistenceTests
{
	[Fact]
	public void SlotsCursorOriginAndSelectionRoundTripWithoutLiveRevision()
	{
		string directory = Path.Combine(Path.GetTempPath(), $"voxelgine-inventory-{Guid.NewGuid():N}");
		try
		{
			PlayerInventory source = new();
			source.Grant(ItemStack.Create(ItemIds.FromBlock(BlockType.Plank), 12));
			source.ApplyClick(InventoryActionKind.RightClickSlot, 0);
			Assert.True(source.Revision > 1);

			PlayerDataStore store = new(directory);
			store.Save("builder", new Vector3(1, 2, 3), 75, new Vector3(4, 5, 6), source, 8);
			PlayerInventory restored = new();

			Assert.True(store.TryLoad("builder", out Vector3 position, out float health, out Vector3 velocity, restored, out byte selection));
			Assert.Equal(new Vector3(1, 2, 3), position);
			Assert.Equal(75, health);
			Assert.Equal(new Vector3(4, 5, 6), velocity);
			Assert.Equal((byte)8, selection);
			Assert.Equal(1, restored.Revision);
			Assert.Equal(source.Cursor, restored.Cursor);
			Assert.Equal(source.CursorOriginSlot, restored.CursorOriginSlot);
			Assert.Equal(source.GetSlots().ToArray(), restored.GetSlots().ToArray());
		}
		finally
		{
			if (Directory.Exists(directory))
				Directory.Delete(directory, true);
		}
	}
}
