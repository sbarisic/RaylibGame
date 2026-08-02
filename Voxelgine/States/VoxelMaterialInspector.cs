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
	private readonly AtlasEditingSession editingSession;
	private readonly AtlasHeightStore heightStore;
	private readonly Action<AtlasPixel> setPaintColor;
	private readonly Action<float> setNormalStrength;
	private readonly Panel root;
	private readonly Label header;
	private readonly Textbox search;
	private readonly ScrollablePane central;
	private readonly StackLayout centralStack;
	private readonly ListBox blockList;
	private readonly Label materialInfo;
	private readonly Label reloadStatus;
	private readonly Label viewportStatus;
	private readonly Label paintStatus;
	private readonly Label hoverStatus;
	private readonly ListBox sharedUsageList;
	private readonly Dictionary<AtlasPaintLayer, AtlasPaintTarget?> layerTargets = new();
	private readonly Dictionary<AtlasPaintLayer, AtlasLayerThumbnail> thumbnails = new();
	private readonly AtlasSwatchGrid swatches;
	private readonly Slider pickerOne;
	private readonly Slider pickerTwo;
	private readonly Slider pickerThree;
	private readonly Slider pickerAlpha;
	private readonly Label pickerOneLabel;
	private readonly Label pickerTwoLabel;
	private readonly Label pickerThreeLabel;
	private readonly Label pickerAlphaLabel;
	private readonly Textbox hexColor;
	private readonly Textbox[] rgbaFields;
	private readonly Button saveButton;
	private readonly Button discardButton;
	private readonly Button undoButton;
	private readonly Button redoButton;
	private readonly Label footerHint;
	private readonly Button backButton;
	private readonly Button vectorModeButton;
	private readonly Button heightModeButton;
	private readonly Dictionary<int, Button> brushSizeButtons = new();
	private readonly Panel lightingCard;
	private AtlasPaintLayer selectedLayer;
	private NormalPaintMode normalPaintMode;
	private AtlasPixel selectedColor = new(0x47, 0xc8, 0xe8, 0xff);
	private BlockType selectedBlock;
	private bool updatingPicker;
	private bool paintModeEnabled = true;
	private (BlockType Block, int Tile)? lastUsageKey;
	internal readonly Slider LightAzimuthSlider;
	internal readonly CheckBox AutomaticLightCheckBox;

	internal VoxelMaterialInspector(
		VoxelMaterialInspectorModel model,
		AtlasEditingSession editingSession,
		AtlasHeightStore heightStore,
		Action<BlockType> select,
		Action<float> setAzimuth,
		Action<float> setElevation,
		Action<float> setDirect,
		Action<float> setAmbient,
		Action<bool> setAutomatic,
		Action<AtlasPaintLayer> selectLayer,
		Action<NormalPaintMode> selectNormalMode,
		Action<AtlasPixel> setPaintColor,
		Action<int> selectBrushSize,
		Action<float> setNormalStrength,
		Action beginNormalStrength,
		Action endNormalStrength,
		Action<bool> setPaintMode,
		Action save,
		Action discard,
		Action undo,
		Action redo,
		Action reload,
		Action back)
	{
		this.model = model;
		this.editingSession = editingSession;
		this.heightStore = heightStore;
		this.setPaintColor = setPaintColor;
		this.setNormalStrength = setNormalStrength;
		selectedBlock = model.Selected;
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

		Panel layerCard = CardPanel(166);
		layerCard.AddChild(PositionedLabel("Paint layers", 10, 8, Accent));
		foreach (AtlasPaintLayer layer in Enum.GetValues<AtlasPaintLayer>())
		{
			layerTargets[layer] = null;
			AtlasPaintLayer captured = layer;
			var thumbnail = new AtlasLayerThumbnail(layer, () => layerTargets[captured],
				(target, x, y) => GetThumbnailPixel(captured, target, x, y), selected =>
			{
				SetSelectedLayer(selected);
				selectLayer(selected);
			});
			thumbnail.Position = new Vector2(10 + (int)layer * 78, 34);
			thumbnail.Size = new Vector2(70, 88);
			layerCard.AddChild(thumbnail);
			thumbnails.Add(layer, thumbnail);
		}
		vectorModeButton = FooterButton("voxel_normal_vector_mode", "Vector", Accent, Background,
			() => { SetNormalPaintMode(NormalPaintMode.Vector); selectNormalMode(NormalPaintMode.Vector); });
		vectorModeButton.Position = new Vector2(10, 128);
		vectorModeButton.Size = new Vector2(140, 28);
		layerCard.AddChild(vectorModeButton);
		heightModeButton = FooterButton("voxel_normal_height_mode", "Height", Background, Primary,
			() => { SetNormalPaintMode(NormalPaintMode.Height); selectNormalMode(NormalPaintMode.Height); });
		heightModeButton.Position = new Vector2(158, 128);
		heightModeButton.Size = new Vector2(140, 28);
		layerCard.AddChild(heightModeButton);
		centralStack.AddChild(layerCard);

		Panel paintCard = CardPanel(326);
		paintCard.AddChild(PositionedLabel("Color tools", 10, 8, Accent));
		pickerOneLabel = PositionedLabel("Hue", 10, 34, Muted);
		pickerTwoLabel = PositionedLabel("Saturation", 10, 80, Muted);
		pickerThreeLabel = PositionedLabel("Value", 10, 126, Muted);
		pickerAlphaLabel = PositionedLabel("Alpha", 10, 172, Muted);
		paintCard.AddChild(pickerOneLabel);
		paintCard.AddChild(pickerTwoLabel);
		paintCard.AddChild(pickerThreeLabel);
		paintCard.AddChild(pickerAlphaLabel);
		pickerOne = AddPickerSlider(paintCard, "voxel_material_picker_1", 52);
		pickerTwo = AddPickerSlider(paintCard, "voxel_material_picker_2", 98,
			beginNormalStrength, endNormalStrength);
		pickerThree = AddPickerSlider(paintCard, "voxel_material_picker_3", 144);
		pickerAlpha = AddPickerSlider(paintCard, "voxel_material_picker_alpha", 190);
		foreach (Slider slider in new[] { pickerOne, pickerTwo, pickerThree, pickerAlpha })
			slider.OnValueChanged += (_, _) => PickerChanged();
		rgbaFields = new Textbox[4];
		for (int component = 0; component < rgbaFields.Length; component++)
		{
			int captured = component;
			var field = new Textbox
			{
				ID = $"voxel_material_rgba_{component}",
				Position = new Vector2(10 + component * 58, 224),
				Size = new Vector2(52, 30),
				Placeholder = "RGBA"[component].ToString(),
				PlaceholderColor = Muted,
				TextColorOverride = Primary,
				Color = Background,
			};
			field.OnTextChanged += (_, value) => ParseComponent(captured, value);
			paintCard.AddChild(field);
			rgbaFields[component] = field;
		}
		hexColor = new Textbox
		{
			ID = "voxel_material_hex_color",
			Position = new Vector2(10, 268),
			Size = new Vector2(120, 32),
			Text = selectedColor.Hex,
			TextColorOverride = Primary,
			Color = Background,
		};
		hexColor.OnTextChanged += (_, value) => ParseHex(value);
		paintCard.AddChild(hexColor);
		var paintMode = new CheckBox
		{
			ID = "voxel_material_paint_mode",
			Position = new Vector2(146, 274),
			Size = new Vector2(20),
			IsChecked = true,
			Color = Accent,
		};
		paintMode.OnCheckedChanged += (_, value) =>
		{
			SetPaintModeVisual(value);
			setPaintMode(value);
		};
		paintCard.AddChild(paintMode);
		paintCard.AddChild(PositionedLabel("Paint mode", 174, 274, Primary));
		centralStack.AddChild(paintCard);

		Panel swatchCard = CardPanel(154);
		swatchCard.AddChild(PositionedLabel("Common + sampled colors", 10, 8, Accent));
		swatches = new AtlasSwatchGrid(BuildSwatches, SetSelectedColor);
		swatches.Position = new Vector2(10, 34);
		swatches.Size = new Vector2(300, 108);
		swatchCard.AddChild(swatches);
		centralStack.AddChild(swatchCard);

		lightingCard = CardPanel(276);
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

		Panel interactionCard = CardPanel(350);
		interactionCard.AddChild(PositionedLabel("3D brush", 10, 8, Accent));
		hoverStatus = PositionedLabel("Hover the block to select a texel", 10, 32, Primary);
		viewportStatus = PositionedLabel(string.Empty, 10, 70, Muted);
		paintStatus = PositionedLabel(editingSession.IsReadOnly
			? "Read-only: use --asset-source-root <Voxelgine/data>"
			: "Ready", 10, 94, editingSession.IsReadOnly ? new FishColor(0xff, 0xab, 0x40) : Muted);
		interactionCard.AddChild(hoverStatus);
		interactionCard.AddChild(viewportStatus);
		interactionCard.AddChild(paintStatus);
		interactionCard.AddChild(PositionedLabel("Brush size", 10, 122, Accent));
		for (int size = VoxelBrushFootprint.MinimumSize; size <= VoxelBrushFootprint.MaximumSize; size++)
		{
			int captured = size;
			Button button = FooterButton($"voxel_material_brush_{size}", $"{size} x {size}",
				size == 1 ? Accent : Background, size == 1 ? Background : Primary,
				() => { SetBrushSize(captured); selectBrushSize(captured); });
			button.Position = new Vector2(10 + (size - 1) * 98, 146);
			button.Size = new Vector2(90, 28);
			interactionCard.AddChild(button);
			brushSizeButtons.Add(size, button);
		}
		interactionCard.AddChild(PositionedLabel("Shared tile usage", 10, 184, Accent));
		sharedUsageList = new ListBox
		{
			ID = "voxel_material_shared_usage",
			Position = new Vector2(10, 208),
			Size = new Vector2(300, 132),
			CustomItemHeight = 22,
			Color = Background,
			CustomItemRenderer = DrawBlockRow,
		};
		interactionCard.AddChild(sharedUsageList);
		centralStack.AddChild(interactionCard);

		central.AddChild(centralStack);
		root.AddChild(header);
		root.AddChild(search);
		root.AddChild(central);
		footerHint = Label("voxel_material_footer_hint",
			"Paint L-drag  •  Sample R-click  •  Orbit Alt+L/M\nWheel zoom  •  Ctrl+Shift+F12 snapshot", Muted);
		saveButton = FooterButton("voxel_material_save", "Save", Accent, Background, save);
		discardButton = FooterButton("voxel_material_discard", "Discard", Card, Primary, discard);
		undoButton = FooterButton("voxel_material_undo", "Undo", Card, Primary, undo);
		redoButton = FooterButton("voxel_material_redo", "Redo", Card, Primary, redo);
		backButton = new Button
		{
			ID = "voxel_material_back",
			Text = "Back to Main Menu",
			Color = Card,
		};
		backButton.SetColorOverride("Text", Primary);
		backButton.OnButtonPressed += (_, _, _) => back();
		root.AddChild(footerHint);
		root.AddChild(saveButton);
		root.AddChild(discardButton);
		root.AddChild(undoButton);
		root.AddChild(redoButton);
		root.AddChild(backButton);

		RebuildList(string.Empty);
		UpdateHeader();
		SetSelectedLayer(AtlasPaintLayer.BaseColor);
		SetNormalPaintMode(NormalPaintMode.Vector);
		SetBrushSize(1);
		SetPaintModeVisual(true);
		SetSelectedColor(selectedColor);
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
		float thumbnailWidth = Math.Max(24, (centralStack.Size.X - 20 - 24) / 4);
		foreach ((AtlasPaintLayer layer, AtlasLayerThumbnail thumbnail) in thumbnails)
		{
			thumbnail.Position = new Vector2(10 + (int)layer * (thumbnailWidth + 8), 34);
			thumbnail.Size = new Vector2(thumbnailWidth, 88);
		}
		swatches.Size = new Vector2(Math.Max(0, centralStack.Size.X - 20), 108);
		sharedUsageList.Size = new Vector2(Math.Max(0, centralStack.Size.X - 20), 132);
		float brushButtonWidth = Math.Max(0, (centralStack.Size.X - 32) / 3);
		foreach ((int size, Button button) in brushSizeButtons)
		{
			button.Position = new Vector2(10 + (size - 1) * (brushButtonWidth + 6), 146);
			button.Size = new Vector2(brushButtonWidth, 28);
		}
		AutomaticLightCheckBox.Size = new Vector2(20, 20);
		materialInfo.Size = new Vector2(Math.Max(0, centralStack.Size.X - 20), 126);
		Control reload = centralStack.Children.SelectMany(child => child.Children)
			.First(child => child.ID == "voxel_material_reload_atlases");
		reload.Size = new Vector2(Math.Max(0, centralStack.Size.X - 20), 34);

		float footerY = Math.Max(0, Layout.Size.Y - 126);
		footerHint.Position = new Vector2(12, footerY);
		footerHint.Size = new Vector2(contentWidth, 38);
		float actionY = footerY + 42;
		float buttonWidth = Math.Max(0, (contentWidth - 18) / 4);
		Button[] actions = { saveButton, discardButton, undoButton, redoButton };
		for (int index = 0; index < actions.Length; index++)
		{
			actions[index].Position = new Vector2(12 + index * (buttonWidth + 6), actionY);
			actions[index].Size = new Vector2(buttonWidth, 32);
		}
		backButton.Position = new Vector2(12, actionY + 38);
		backButton.Size = new Vector2(contentWidth, 32);
	}

	internal void UpdateMaterialInfo(VoxelMaterialPreviewInfo info)
	{
		selectedBlock = info.BlockType;
		lastUsageKey = null;
		var tiles = info.AtlasTiles;
		materialInfo.Text =
			$"Material identity  {(int)info.BlockType}: {info.Name}\n" +
			$"Geometry  {(info.IsCustomModel ? "Custom model" : "Cube")}\n" +
			$"Render mode  {info.RenderMode}\n" +
			$"Surface maps  {(info.SurfaceMapsEnabled ? "Enabled" : "Disabled")}\n" +
			$"Faces +X/-X/+Y/-Y/+Z/-Z\n" +
			$"{tiles.PositiveX} / {tiles.NegativeX} / {tiles.PositiveY} / {tiles.NegativeY} / {tiles.PositiveZ} / {tiles.NegativeZ}";
		int defaultTile = info.IsCustomModel ? -1 : tiles.PositiveY;
		foreach (AtlasPaintLayer layer in Enum.GetValues<AtlasPaintLayer>())
			layerTargets[layer] = editingSession.GetTarget(info.BlockType, layer, defaultTile);
	}

	internal void SetReloadStatus(string value) => reloadStatus.Text = value;

	internal void SetEditStatus(string value) => paintStatus.Text = value;

	internal void RefreshPaintData(
		VoxelPaintHit hit,
		AtlasPaintLayer layer,
		IReadOnlyList<string> sharedUsage)
	{
		foreach (AtlasPaintLayer paintLayer in Enum.GetValues<AtlasPaintLayer>())
			layerTargets[paintLayer] = editingSession.GetTarget(selectedBlock, paintLayer, hit.TextureLayer);
		string target = hit.Target.CustomDefinition == null
			? $"tile {hit.TextureLayer}"
			: hit.Target.CustomDefinition.RelativePath;
		hoverStatus.Text =
			$"{hit.Face}  •  {target}  •  {layer}  •  pixel {hit.LocalX},{hit.LocalY}\n" +
			(hit.Editable ? $"Affects {sharedUsage.Count} mapped face/state uses" : "Read-only surface-map region");
		if (lastUsageKey != (selectedBlock, hit.TextureLayer))
		{
			lastUsageKey = (selectedBlock, hit.TextureLayer);
			sharedUsageList.Items.Clear();
			foreach (string usage in sharedUsage)
				sharedUsageList.AddItem(new ListBoxItem(usage));
		}
	}

	internal void SetSelectedLayer(AtlasPaintLayer layer)
	{
		selectedLayer = layer;
		foreach ((AtlasPaintLayer key, AtlasLayerThumbnail thumbnail) in thumbnails)
			thumbnail.Selected = key == layer;
		ConfigurePickerForLayer();
		UpdateNormalModeVisibility();
		SetPaintModeVisual(paintModeEnabled);
	}

	internal void SetNormalPaintMode(NormalPaintMode mode)
	{
		normalPaintMode = mode;
		vectorModeButton.Color = mode == NormalPaintMode.Vector ? Accent : Background;
		heightModeButton.Color = mode == NormalPaintMode.Height ? Accent : Background;
		vectorModeButton.SetColorOverride("Text", mode == NormalPaintMode.Vector ? Background : Primary);
		heightModeButton.SetColorOverride("Text", mode == NormalPaintMode.Height ? Background : Primary);
		ConfigurePickerForLayer();
		SetPaintModeVisual(paintModeEnabled);
	}

	internal void SetSelectedColor(AtlasPixel color)
	{
		selectedColor = color;
		setPaintColor(color);
		updatingPicker = true;
		try
		{
			hexColor.Text = color.Hex;
			rgbaFields[0].Text = color.R.ToString(System.Globalization.CultureInfo.InvariantCulture);
			rgbaFields[1].Text = color.G.ToString(System.Globalization.CultureInfo.InvariantCulture);
			rgbaFields[2].Text = color.B.ToString(System.Globalization.CultureInfo.InvariantCulture);
			rgbaFields[3].Text = color.A.ToString(System.Globalization.CultureInfo.InvariantCulture);
			if (selectedLayer == AtlasPaintLayer.BaseColor)
			{
				(float hue, float saturation, float value) = RgbToHsv(color);
				pickerOne.Value = hue;
				pickerTwo.Value = saturation;
				pickerThree.Value = value;
				pickerAlpha.Value = color.A;
			}
			else if (selectedLayer == AtlasPaintLayer.Normal && normalPaintMode == NormalPaintMode.Vector)
			{
				pickerOne.Value = color.R / 255f * 2 - 1;
				pickerTwo.Value = color.G / 255f * 2 - 1;
			}
			else if (selectedLayer == AtlasPaintLayer.Normal)
			{
				pickerOne.Value = color.R;
				AtlasPaintTarget? target = layerTargets[AtlasPaintLayer.Normal];
				pickerTwo.Value = target.HasValue
					? heightStore.GetOrCreate(target.Value).Strength
					: AtlasHeightStore.DefaultStrength;
			}
			else
			{
				pickerOne.Value = color.R;
			}
		}
		finally
		{
			updatingPicker = false;
		}
	}

	internal void SetBrushSize(int size)
	{
		VoxelBrushFootprint.ValidateSize(size);
		foreach ((int value, Button button) in brushSizeButtons)
		{
			bool selected = value == size;
			button.Color = selected ? Accent : Background;
			button.SetColorOverride("Text", selected ? Background : Primary);
		}
	}

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

	private static Slider AddPickerSlider(Panel parent, string id, float y,
		Action gestureStarted = null, Action gestureCompleted = null)
	{
		Slider slider = gestureStarted == null && gestureCompleted == null
			? new Slider()
			: new GestureSlider(gestureStarted, gestureCompleted);
		slider.ID = id;
		slider.Position = new Vector2(10, y);
		slider.Size = new Vector2(280, 24);
		slider.MinValue = 0;
		slider.MaxValue = 1;
		slider.Step = 0.01f;
		slider.TrackColor = Background;
		slider.FillColor = Accent;
		slider.ThumbColor = Primary;
		slider.LabelColor = Primary;
		slider.UseThemeColors = false;
		slider.ShowValueLabel = true;
		slider.ValueLabelFormat = "0.00";
		parent.AddChild(slider);
		return slider;
	}

	private static Button FooterButton(
		string id,
		string text,
		FishColor color,
		FishColor textColor,
		Action action)
	{
		var button = new Button { ID = id, Text = text, Color = color };
		button.SetColorOverride("Text", textColor);
		button.OnButtonPressed += (_, _, _) => action();
		return button;
	}

	private void ConfigurePickerForLayer()
	{
		if (pickerOne == null)
			return;
		updatingPicker = true;
		try
		{
			bool baseColor = selectedLayer == AtlasPaintLayer.BaseColor;
			bool normal = selectedLayer == AtlasPaintLayer.Normal;
			bool height = normal && normalPaintMode == NormalPaintMode.Height;
			pickerOneLabel.Text = baseColor ? "Hue" : height ? "Height" : normal ? "Normal X" : "Value";
			pickerTwoLabel.Text = baseColor ? "Saturation" : height ? "Normal Strength" : "Normal Y";
			pickerThreeLabel.Text = "Value";
			pickerAlphaLabel.Text = "Alpha";
			pickerTwo.Visible = pickerTwoLabel.Visible = baseColor || normal;
			pickerThree.Visible = pickerThreeLabel.Visible = baseColor;
			pickerAlpha.Visible = pickerAlphaLabel.Visible = baseColor;
			pickerOne.MinValue = normal && !height ? -1 : 0;
			pickerOne.MaxValue = baseColor ? 360 : normal && !height ? 1 : 255;
			pickerOne.Step = baseColor ? 1 : normal && !height ? 0.01f : 1;
			pickerTwo.MinValue = height ? AtlasHeightStore.MinimumStrength : normal ? -1 : 0;
			pickerTwo.MaxValue = height ? AtlasHeightStore.MaximumStrength : 1;
			pickerTwo.Step = height ? 0.25f : 0.01f;
			pickerThree.MinValue = 0;
			pickerThree.MaxValue = 1;
			pickerThree.Step = 0.01f;
			pickerAlpha.MinValue = 0;
			pickerAlpha.MaxValue = 255;
			pickerAlpha.Step = 1;
		}
		finally
		{
			updatingPicker = false;
		}
		SetSelectedColor(selectedColor);
	}

	private void PickerChanged()
	{
		if (updatingPicker)
			return;
		bool height = selectedLayer == AtlasPaintLayer.Normal
			&& normalPaintMode == NormalPaintMode.Height;
		float requestedStrength = pickerTwo.Value;
		AtlasPixel color = selectedLayer switch
		{
			AtlasPaintLayer.BaseColor => HsvToRgb(
				pickerOne.Value, pickerTwo.Value, pickerThree.Value, (byte)Math.Clamp((int)pickerAlpha.Value, 0, 255)),
			AtlasPaintLayer.Normal when height => AtlasPixel.Scalar(
				(byte)Math.Clamp((int)pickerOne.Value, 0, 255)),
			AtlasPaintLayer.Normal => AtlasPixel.Normal(pickerOne.Value, pickerTwo.Value),
			AtlasPaintLayer.Specular or AtlasPaintLayer.Roughness =>
				AtlasPixel.Scalar((byte)Math.Clamp((int)pickerOne.Value, 0, 255)),
			_ => throw new ArgumentOutOfRangeException(),
		};
		SetSelectedColor(color);
		if (height)
			setNormalStrength(requestedStrength);
	}

	private AtlasPixel GetThumbnailPixel(AtlasPaintLayer layer, AtlasPaintTarget target, int x, int y)
	{
		if (layer == AtlasPaintLayer.Normal && normalPaintMode == NormalPaintMode.Height)
			return AtlasPixel.Scalar(heightStore.GetOrCreate(target).Get(x, y));
		return target.Get(x, y);
	}

	private void UpdateNormalModeVisibility()
	{
		bool visible = selectedLayer == AtlasPaintLayer.Normal;
		vectorModeButton.Visible = visible;
		heightModeButton.Visible = visible;
	}

	private void SetPaintModeVisual(bool enabled)
	{
		paintModeEnabled = enabled;
		lightingCard.Disabled = enabled;
		viewportStatus.Text = enabled
			? $"Viewport: {(selectedLayer == AtlasPaintLayer.Normal ? $"Normal {normalPaintMode}" : selectedLayer)} (unlit)"
			: "Viewport: Material";
	}

	private sealed class GestureSlider : Slider
	{
		private readonly Action started;
		private readonly Action completed;

		internal GestureSlider(Action started, Action completed)
		{
			this.started = started;
			this.completed = completed;
		}

		public override void HandleMousePress(global::FishUI.FishUI ui, FishInputState input,
			FishMouseButton button, Vector2 position)
		{
			if (button == FishMouseButton.Left && !Disabled)
				started?.Invoke();
			base.HandleMousePress(ui, input, button, position);
		}

		public override void HandleMouseRelease(global::FishUI.FishUI ui, FishInputState input,
			FishMouseButton button, Vector2 position)
		{
			base.HandleMouseRelease(ui, input, button, position);
			if (button == FishMouseButton.Left)
				completed?.Invoke();
		}
	}

	private void ParseHex(string value)
	{
		if (updatingPicker || value?.Length != 9 || value[0] != '#'
			|| !uint.TryParse(value.AsSpan(1), System.Globalization.NumberStyles.HexNumber,
				System.Globalization.CultureInfo.InvariantCulture, out uint parsed))
			return;
		SetSelectedColor(new AtlasPixel(
			(byte)(parsed >> 24), (byte)(parsed >> 16), (byte)(parsed >> 8), (byte)parsed));
	}

	private void ParseComponent(int component, string value)
	{
		if (updatingPicker || !byte.TryParse(value, out byte parsed))
			return;
		AtlasPixel color = component switch
		{
			0 => selectedColor with { R = parsed },
			1 => selectedColor with { G = parsed },
			2 => selectedColor with { B = parsed },
			3 => selectedColor with { A = parsed },
			_ => throw new ArgumentOutOfRangeException(nameof(component)),
		};
		SetSelectedColor(color);
	}

	private IReadOnlyList<AtlasPixel> BuildSwatches()
	{
		AtlasPixel[] fixedColors =
		{
			new(0x00,0x00,0x00,0x00), new(0x11,0x13,0x18,0xff), new(0x24,0x28,0x32,0xff), new(0x3f,0x46,0x52,0xff),
			new(0x69,0x73,0x82,0xff), new(0x9a,0xa8,0xb8,0xff), new(0xd7,0xe0,0xe8,0xff), new(0xff,0xff,0xff,0xff),
			new(0x3a,0x24,0x18,0xff), new(0x6b,0x42,0x26,0xff), new(0x9b,0x69,0x38,0xff), new(0xd1,0xa3,0x65,0xff),
			new(0x17,0x35,0x1f,0xff), new(0x2f,0x64,0x34,0xff), new(0x57,0x93,0x44,0xff), new(0x47,0xc8,0xe8,0xff),
		};
		AtlasPaintTarget? target = layerTargets.GetValueOrDefault(selectedLayer);
		if (!target.HasValue)
			return fixedColors;
		if (selectedLayer == AtlasPaintLayer.Normal && normalPaintMode == NormalPaintMode.Height)
		{
			AtlasHeightField field = heightStore.GetOrCreate(target.Value);
			AtlasPixel[] grayscale = Enumerable.Range(0, 16)
				.Select(index => AtlasPixel.Scalar((byte)(index * 17)))
				.ToArray();
			IEnumerable<AtlasPixel> frequent = field.Pixels.ToArray()
				.GroupBy(static value => value)
				.OrderByDescending(static group => group.Count())
				.ThenBy(static group => group.Key)
				.Take(16)
				.Select(group => AtlasPixel.Scalar(group.Key));
			return grayscale.Concat(frequent).ToArray();
		}
		return fixedColors.Concat(target.Value.Document.GetFrequentColors(
			target.Value.X, target.Value.Y, target.Value.Width, target.Value.Height, 16)).ToArray();
	}

	private static (float Hue, float Saturation, float Value) RgbToHsv(AtlasPixel color)
	{
		float r = color.R / 255f;
		float g = color.G / 255f;
		float b = color.B / 255f;
		float maximum = Math.Max(r, Math.Max(g, b));
		float minimum = Math.Min(r, Math.Min(g, b));
		float delta = maximum - minimum;
		float hue = delta == 0 ? 0
			: maximum == r ? 60 * (((g - b) / delta) % 6)
			: maximum == g ? 60 * ((b - r) / delta + 2)
			: 60 * ((r - g) / delta + 4);
		if (hue < 0) hue += 360;
		return (hue, maximum == 0 ? 0 : delta / maximum, maximum);
	}

	private static AtlasPixel HsvToRgb(float hue, float saturation, float value, byte alpha)
	{
		float chroma = value * saturation;
		float x = chroma * (1 - MathF.Abs((hue / 60) % 2 - 1));
		float m = value - chroma;
		(float r, float g, float b) = hue switch
		{
			< 60 => (chroma, x, 0f),
			< 120 => (x, chroma, 0f),
			< 180 => (0f, chroma, x),
			< 240 => (0f, x, chroma),
			< 300 => (x, 0f, chroma),
			_ => (chroma, 0f, x),
		};
		return new AtlasPixel(
			(byte)Math.Clamp((int)MathF.Round((r + m) * 255), 0, 255),
			(byte)Math.Clamp((int)MathF.Round((g + m) * 255), 0, 255),
			(byte)Math.Clamp((int)MathF.Round((b + m) * 255), 0, 255), alpha);
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
