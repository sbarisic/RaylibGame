#if WINDOWS
using FishUI;
using System.Numerics;
using Voxelgine.Engine;
using Voxelgine.Engine.DI;
using Voxelgine.Graphics;
using Voxelgine.GUI;

namespace Voxelgine.FishGfxClient.Viewmodel;

internal sealed class FishGfxGunItem : Weapon, IViewModelAssetProvider
{
	private readonly global::FishUI.FishUI ui;

	public FishGfxGunItem(
		IFishEngineRunner engine,
		ClientPlayer player,
		global::FishUI.FishUI ui
	) : base(engine, player, "Gun", IconType.Gun)
	{
		this.ui = ui ?? throw new ArgumentNullException(nameof(ui));
		SetViewModelInfo(ViewModelRotationMode.Gun);
	}

	public ViewModelAssetKind ViewModelAsset => ViewModelAssetKind.Gun;

	public bool IsAiming { get; private set; }

	public override bool SupportsAutoFire => true;

	public override float AutoFireRate => 10;

	public override void SetupFishUIItemBox(FishUIItemBox itemBox)
	{
		itemBox.SetIcon(ui, "data/textures/items/gun.png", 1.8f);
	}

	public override void Tick(ViewModel viewModel, InputMgr input)
	{
		IsAiming = input.IsInputDown(InputKey.Click_Right);
		ViewModelRotationMode = IsAiming
			? ViewModelRotationMode.GunIronsight
			: ViewModelRotationMode.Gun;
		viewModel.SetRotationMode(ViewModelRotationMode);
	}

	public override void OnLeftClick(InventoryClickEventArgs args)
	{
		if (!IsAiming)
		{
			return;
		}

		ParentPlayer.ViewMdl.ApplyKickback();
		ParentPlayer.PlaySound("shoot1", ParentPlayer.Position);
		if (Eng.AsClient().MultiplayerGameState is { IsActive: true } multiplayer)
		{
			multiplayer.SendWeaponFire(args.Start, args.Dir);
			multiplayer.SpawnPredictedFireEffects(args.Start, args.Dir, args.MaxLen);
		}
	}
}

internal sealed class FishGfxHammerItem : Weapon, IViewModelAssetProvider
{
	private readonly global::FishUI.FishUI ui;

	public FishGfxHammerItem(
		IFishEngineRunner engine,
		ClientPlayer player,
		global::FishUI.FishUI ui
	) : base(engine, player, "Hammer", IconType.Hammer)
	{
		this.ui = ui ?? throw new ArgumentNullException(nameof(ui));
		SetViewModelInfo(ViewModelRotationMode.Tool);
	}

	public ViewModelAssetKind ViewModelAsset => ViewModelAssetKind.Hammer;

	public override void SetupFishUIItemBox(FishUIItemBox itemBox)
	{
		itemBox.SetIcon(ui, "data/textures/items/hammer.png", 3.8f);
	}

	public override void OnLeftClick(InventoryClickEventArgs args)
	{
		ParentPlayer.ViewMdl.ApplySwing();
	}
}

internal sealed class FishGfxBlockItem : Weapon, IViewModelAssetProvider
{
	private readonly global::FishUI.FishUI ui;

	public FishGfxBlockItem(
		IFishEngineRunner engine,
		ClientPlayer player,
		BlockType block,
		global::FishUI.FishUI ui
	) : base(engine, player, block)
	{
		this.ui = ui ?? throw new ArgumentNullException(nameof(ui));
	}

	public ViewModelAssetKind ViewModelAsset => ViewModelAssetKind.None;

	public override void SetupFishUIItemBox(FishUIItemBox itemBox)
	{
		BlockPresentationInfo.GetBlockTexCoords(
			BlockIcon,
			Vector3.UnitY,
			out Vector2 uvSize,
			out Vector2 uvPosition
		);
		ImageRef atlas = ui.Graphics.LoadImage("data/textures/atlas.png");
		int x = (int)MathF.Round(uvPosition.X * atlas.Width);
		int y = (int)MathF.Round(uvPosition.Y * atlas.Height);
		int width = (int)MathF.Round(uvSize.X * atlas.Width);
		int height = (int)MathF.Round(uvSize.Y * atlas.Height);
		ImageRef region = ui.Graphics.LoadImage(atlas, x, y, width, height);
		itemBox.SetIcon(ui, region, 1);
	}
}
#endif
