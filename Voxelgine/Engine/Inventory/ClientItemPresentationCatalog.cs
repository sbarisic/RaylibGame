using System.Numerics;
using FishUI;
using Voxelgine.GUI;

namespace Voxelgine.Engine;

public enum DroppedItemPresentationKind
{
	BlockCube,
	CustomBlockModel,
	IconQuad,
}

public sealed record ClientItemPresentation(
	ItemId Id,
	string IconAsset,
	ViewModelAssetKind ViewModel,
	DroppedItemPresentationKind WorldPresentation);

public static class ClientItemPresentationCatalog
{
	public static ClientItemPresentation Get(ItemId item)
	{
		if (item == ItemIds.Gun)
			return new(item, "data/textures/items/gun.png", ViewModelAssetKind.Gun, DroppedItemPresentationKind.IconQuad);
		if (item == ItemIds.Hammer)
			return new(item, "data/textures/items/hammer.png", ViewModelAssetKind.Hammer, DroppedItemPresentationKind.IconQuad);

		ItemDefinition definition = ItemCatalog.Get(item);
		BlockType block = definition.PlacesBlock ?? BlockType.None;
		DroppedItemPresentationKind world = BlockInfo.CustomModel(block)
			? DroppedItemPresentationKind.CustomBlockModel
			: DroppedItemPresentationKind.BlockCube;
		return new(item, null, ViewModelAssetKind.None, world);
	}

	public static void ApplyIcon(global::FishUI.FishUI ui, FishUIItemBox box, ItemStack stack)
	{
		box.ClearIcon();
		if (stack.IsEmpty)
			return;

		ClientItemPresentation presentation = Get(stack.Item);
		if (!string.IsNullOrEmpty(presentation.IconAsset))
		{
			box.SetIcon(ui, presentation.IconAsset, stack.Item == ItemIds.Hammer ? 3.8f : 1.8f);
			return;
		}

		BlockType block = ItemCatalog.Get(stack.Item).PlacesBlock ?? BlockType.None;
		BlockPresentationInfo.GetBlockTexCoords(block, Vector3.UnitY, out Vector2 uvSize, out Vector2 uvPosition);
		ImageRef atlas = ui.Graphics.LoadImage("data/textures/atlas.png");
		int x = (int)MathF.Round(uvPosition.X * atlas.Width);
		int y = (int)MathF.Round(uvPosition.Y * atlas.Height);
		int width = (int)MathF.Round(uvSize.X * atlas.Width);
		int height = (int)MathF.Round(uvSize.Y * atlas.Height);
		box.SetIcon(ui, ui.Graphics.LoadImage(atlas, x, y, width, height), 1);
	}
}
