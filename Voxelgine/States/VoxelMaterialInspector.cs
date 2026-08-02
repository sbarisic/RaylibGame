#if WINDOWS
using System.Numerics;
using FishUI;
using FishUI.Controls;
using Voxelgine.Engine;
using Voxelgine.FishGfxClient.Voxels;

namespace Voxelgine.States;

internal sealed class VoxelMaterialInspector
{
	private static readonly FishColor Background = new(0x19, 0x1d, 0x25);
	private static readonly FishColor Card = new(0x25, 0x2b, 0x36);
	private static readonly FishColor Accent = new(0x47, 0xc8, 0xe8);
	private static readonly FishColor Primary = new(0xee, 0xf4, 0xfa);
	private static readonly FishColor Muted = new(0x9a, 0xa8, 0xb8);

	private readonly VoxelMaterialInspectorModel model;
	private readonly Panel root;
	private readonly Label header;
	private readonly Textbox search;
	private readonly ScrollablePane central;
	private readonly StackLayout centralStack;
	private readonly ListBox blockList;
	private readonly Label materialInfo;
	private readonly Label reloadStatus;
	private readonly Label footerHint;
	private readonly Button backButton;
	internal readonly Slider LightAzimuthSlider;
	internal readonly CheckBox AutomaticLightCheckBox;

	internal VoxelMaterialInspector(
		VoxelMaterialInspectorModel model,
		Action<BlockType> select,
		Action<float> setAzimuth,
		Action<float> setElevation,
		Action<float> setDirect,
		Action<float> setAmbient,
		Action<bool> setAutomatic,
		Action reload,
		Action back)
	{
		this.model = model;
		root = new Panel { ID = "voxel_material_inspector", Color = Background };
		header = Label("voxel_material_header", string.Empty, Primary);
		search = new Textbox
		{
			ID = "voxel_material_search",
			Placeholder = "Search block name or numeric ID",
			PlaceholderColor = Muted,
			TextColorOverride = Primary,
			Color = Card,
		};
		search.OnTextChanged += (_, query) => RebuildList(query);

		central = new ScrollablePane
		{
			ID = "voxel_material_central_scroll",
			AutoContentSize = true,
			Color = Background,
			BorderColor = Card,
		};
		centralStack = new StackLayout
		{
			Orientation = StackOrientation.Vertical,
			Spacing = VoxelMaterialInspectorLayout.Spacing,
			IsTransparent = true,
		};
		blockList = new ListBox
		{
			ID = "voxel_material_block_list",
			Size = new Vector2(300, VoxelMaterialInspectorLayout.MinimumBlockListHeight),
			CustomItemHeight = 26,
			Color = Card,
			CustomItemRenderer = DrawBlockRow,
		};
		blockList.OnItemSelected += (_, _, item) =>
		{
			if (item?.UserData is not BlockType type) return;
			select(type);
		};
		centralStack.AddChild(blockList);

		Panel infoCard = CardPanel(142);
		materialInfo = Label("voxel_material_info", string.Empty, Primary);
		materialInfo.Position = new Vector2(10, 8);
		infoCard.AddChild(materialInfo);
		centralStack.AddChild(infoCard);

		Panel lightingCard = CardPanel(276);
		lightingCard.AddChild(PositionedLabel("Lighting", 10, 8, Accent));
		LightAzimuthSlider = AddSlider(lightingCard, "voxel_material_light_azimuth", "Azimuth", 38,
			0, 360, 45, 1, setAzimuth);
		AddSlider(lightingCard, "voxel_material_light_elevation", "Elevation", 88,
			5, 85, 35, 1, setElevation);
		AddSlider(lightingCard, "voxel_material_direct", "Direct intensity", 138,
			0, 2, 1, 0.05f, setDirect);
		AddSlider(lightingCard, "voxel_material_ambient", "Ambient intensity", 188,
			0, 0.5f, 0.15f, 0.01f, setAmbient);
		AutomaticLightCheckBox = new CheckBox
		{
			ID = "voxel_material_auto_light",
			Position = new Vector2(10, 238),
			Size = new Vector2(20, 20),
			Color = Accent,
		};
		AutomaticLightCheckBox.OnCheckedChanged += (_, value) => setAutomatic(value);
		lightingCard.AddChild(AutomaticLightCheckBox);
		lightingCard.AddChild(PositionedLabel("Automatic rotation", 38, 238, Primary));
		centralStack.AddChild(lightingCard);

		Panel atlasCard = CardPanel(116);
		atlasCard.AddChild(PositionedLabel("Atlas", 10, 8, Accent));
		var reloadButton = new Button
		{
			ID = "voxel_material_reload_atlases",
			Text = "Reload atlas textures",
			Position = new Vector2(10, 34),
			Size = new Vector2(300, 34),
			Color = Accent,
		};
		reloadButton.SetColorOverride("Text", Background);
		reloadButton.OnButtonPressed += (_, _, _) => reload();
		atlasCard.AddChild(reloadButton);
		reloadStatus = PositionedLabel("Watching deployed textures (200 ms debounce)", 10, 76, Muted);
		atlasCard.AddChild(reloadStatus);
		centralStack.AddChild(atlasCard);

		central.AddChild(centralStack);
		root.AddChild(header);
		root.AddChild(search);
		root.AddChild(central);
		footerHint = Label("voxel_material_footer_hint",
			"Drag outside to orbit  •  Wheel to zoom\nCtrl+Shift+F12 creates a debug snapshot", Muted);
		backButton = new Button
		{
			ID = "voxel_material_back",
			Text = "Back to Main Menu",
			Color = Card,
		};
		backButton.SetColorOverride("Text", Primary);
		backButton.OnButtonPressed += (_, _, _) => back();
		root.AddChild(footerHint);
		root.AddChild(backButton);

		RebuildList(string.Empty);
		UpdateHeader();
	}

	internal Control Root => root;
	internal VoxelMaterialInspectorLayout Layout { get; private set; }
	internal bool Contains(Vector2 point) => Layout.Contains(point);

	internal void UpdateLayout(float windowWidth, float windowHeight)
	{
		Layout = VoxelMaterialInspectorLayout.Calculate(windowWidth, windowHeight);
		root.Position = Layout.Position;
		root.Size = Layout.Size;
		float contentWidth = Math.Max(0, Layout.Size.X - VoxelMaterialInspectorLayout.Spacing * 2);
		header.Position = new Vector2(12, 10);
		header.Size = new Vector2(contentWidth, 48);
		search.Position = new Vector2(12, 68);
		search.Size = new Vector2(contentWidth, 34);
		central.Position = Layout.CentralPosition;
		central.Size = Layout.CentralSize;
		centralStack.Position = Vector2.Zero;
		float stackHeight = VoxelMaterialInspectorLayout.CalculateStackContentHeight(
			centralStack.Children.Select(static child => child.Size.Y));
		centralStack.Size = new Vector2(Math.Max(0, contentWidth - 20), stackHeight);
		blockList.Size = new Vector2(centralStack.Size.X, VoxelMaterialInspectorLayout.MinimumBlockListHeight);
		foreach (Control card in centralStack.Children.Where(child => child is Panel))
			card.Size = new Vector2(centralStack.Size.X, card.Size.Y);
		foreach (Slider slider in centralStack.Children.SelectMany(child => child.Children).OfType<Slider>())
			slider.Size = new Vector2(Math.Max(0, centralStack.Size.X - 20), slider.Size.Y);
		AutomaticLightCheckBox.Size = new Vector2(20, 20);
		materialInfo.Size = new Vector2(Math.Max(0, centralStack.Size.X - 20), 126);
		Control reload = centralStack.Children.SelectMany(child => child.Children)
			.First(child => child.ID == "voxel_material_reload_atlases");
		reload.Size = new Vector2(Math.Max(0, centralStack.Size.X - 20), 34);

		float footerY = Math.Max(0, Layout.Size.Y - 76);
		footerHint.Position = new Vector2(12, footerY);
		footerHint.Size = new Vector2(contentWidth, 32);
		backButton.Position = new Vector2(12, footerY + 36);
		backButton.Size = new Vector2(contentWidth, 34);
	}

	internal void UpdateMaterialInfo(VoxelMaterialPreviewInfo info)
	{
		var tiles = info.AtlasTiles;
		materialInfo.Text =
			$"Material identity  {(int)info.BlockType}: {info.Name}\n" +
			$"Geometry  {(info.IsCustomModel ? "Custom model" : "Cube")}\n" +
			$"Render mode  {info.RenderMode}\n" +
			$"Surface maps  {(info.SurfaceMapsEnabled ? "Enabled" : "Disabled")}\n" +
			$"Faces +X/-X/+Y/-Y/+Z/-Z\n" +
			$"{tiles.PositiveX} / {tiles.NegativeX} / {tiles.PositiveY} / {tiles.NegativeY} / {tiles.PositiveZ} / {tiles.NegativeZ}";
	}

	internal void SetReloadStatus(string value) => reloadStatus.Text = value;

	internal void Select(BlockType type)
	{
		model.Select(type);
		RebuildList(search.Text);
		UpdateHeader();
	}

	private void RebuildList(string query)
	{
		blockList.Items.Clear();
		foreach (VoxelMaterialChoice choice in model.Filter(query))
			blockList.AddItem(new ListBoxItem($"{(int)choice.Type}: {choice.Type}", choice.Type));
		blockList.SerializedSelectedIndex = blockList.Items.FindIndex(item =>
			item.UserData is BlockType type && type == model.Selected);
	}

	private void UpdateHeader()
	{
		header.Text = $"Material Lab\n{model.Selected}  •  ID {(int)model.Selected}  •  {model.Choices.Count} blocks";
	}

	private static Panel CardPanel(float height) => new()
	{
		Size = new Vector2(300, height),
		Color = Card,
		BorderStyle = BorderStyle.Solid,
		BorderColor = new FishColor(0x35, 0x3d, 0x4a),
	};

	private static Label Label(string id, string text, FishColor color)
	{
		var label = new Label { ID = id, Text = text };
		label.SetColorOverride("Text", color);
		return label;
	}

	private static Label PositionedLabel(string text, float x, float y, FishColor color)
	{
		Label label = Label(null, text, color);
		label.Position = new Vector2(x, y);
		label.Size = new Vector2(290, 24);
		return label;
	}

	private static Slider AddSlider(Panel parent, string id, string title, float y,
		float minimum, float maximum, float value, float step, Action<float> changed)
	{
		parent.AddChild(PositionedLabel(title, 10, y, Muted));
		var slider = new Slider
		{
			ID = id,
			Position = new Vector2(10, y + 20),
			Size = new Vector2(280, 26),
			MinValue = minimum,
			MaxValue = maximum,
			Value = value,
			Step = step,
			ShowValueLabel = true,
			ValueLabelFormat = "0.00",
			TrackColor = Background,
			FillColor = Accent,
			ThumbColor = Primary,
			LabelColor = Primary,
			UseThemeColors = false,
		};
		slider.OnValueChanged += (_, newValue) => changed(newValue);
		parent.AddChild(slider);
		return slider;
	}

	private static void DrawBlockRow(global::FishUI.FishUI ui, ListBoxItem item, int index,
		Vector2 position, Vector2 size, bool selected, bool hovered)
	{
		FishColor background = selected ? Accent : hovered ? new FishColor(0x31, 0x3a, 0x48) : Card;
		FishColor foreground = selected ? Background : Primary;
		ui.Graphics.DrawRectangle(position, size, background);
		ui.Graphics.DrawTextColor(ui.Settings.FontDefault, item.Text, position + new Vector2(8, 4), foreground);
	}
}
#endif
