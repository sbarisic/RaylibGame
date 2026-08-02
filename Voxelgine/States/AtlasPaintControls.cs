#if WINDOWS
using System.Numerics;
using FishUI;
using FishUI.Controls;

namespace Voxelgine.States;

internal sealed class AtlasLayerThumbnail : Control
{
	private readonly Func<AtlasPaintTarget?> getTarget;
	private readonly Func<AtlasPaintTarget, int, int, AtlasPixel> getPixel;
	private readonly Action<AtlasPaintLayer> select;

	internal AtlasLayerThumbnail(AtlasPaintLayer layer, Func<AtlasPaintTarget?> getTarget,
		Func<AtlasPaintTarget, int, int, AtlasPixel> getPixel,
		Action<AtlasPaintLayer> select)
	{
		Layer = layer;
		this.getTarget = getTarget;
		this.getPixel = getPixel;
		this.select = select;
	}

	internal AtlasPaintLayer Layer { get; }
	internal bool Selected { get; set; }

	public override void HandleMouseClick(global::FishUI.FishUI ui, FishInputState input,
		FishMouseButton button, Vector2 position)
	{
		base.HandleMouseClick(ui, input, button, position);
		if (button == FishMouseButton.Left)
			select(Layer);
	}

	public override void DrawControl(global::FishUI.FishUI ui, float deltaTime, float time)
	{
		Vector2 position = GetAbsolutePosition();
		Vector2 size = GetAbsoluteSize();
		FishColor border = Selected ? new FishColor(0x47, 0xc8, 0xe8) : new FishColor(0x35, 0x3d, 0x4a);
		ui.Graphics.DrawRectangle(position, size, new FishColor(0x19, 0x1d, 0x25));
		AtlasPaintTarget? nullable = getTarget();
		if (nullable.HasValue)
		{
			AtlasPaintTarget target = nullable.Value;
			float top = 18;
			float pixelWidth = size.X / target.Width;
			float pixelHeight = Math.Max(0, size.Y - top) / target.Height;
			for (int y = 0; y < target.Height; y++)
			for (int x = 0; x < target.Width; x++)
			{
				AtlasPixel pixel = getPixel(target, x, y);
				if (Layer == AtlasPaintLayer.BaseColor && pixel.A < byte.MaxValue)
				{
					FishColor checker = ((x + y) & 1) == 0
						? new FishColor(0x5c, 0x65, 0x72)
						: new FishColor(0x2c, 0x32, 0x3c);
					ui.Graphics.DrawRectangle(
						position + new Vector2(x * pixelWidth, top + y * pixelHeight),
						new Vector2(MathF.Ceiling(pixelWidth), MathF.Ceiling(pixelHeight)), checker);
				}
				byte red = pixel.R;
				FishColor color = Layer is AtlasPaintLayer.Specular or AtlasPaintLayer.Roughness
					? new FishColor(red, red, red, byte.MaxValue)
					: new FishColor(pixel.R, pixel.G, pixel.B, pixel.A);
				ui.Graphics.DrawRectangle(
					position + new Vector2(x * pixelWidth, top + y * pixelHeight),
					new Vector2(MathF.Ceiling(pixelWidth), MathF.Ceiling(pixelHeight)), color);
			}
		}
		ui.Graphics.DrawRectangleOutline(position, size, border);
		if (Selected)
			ui.Graphics.DrawRectangleOutline(position + Vector2.One, size - new Vector2(2), border);
		ui.Graphics.DrawTextColor(ui.Settings.FontDefault, Layer.ToString(), position + new Vector2(4, 1),
			Selected ? new FishColor(0xee, 0xf4, 0xfa) : new FishColor(0x9a, 0xa8, 0xb8));
	}
}

internal sealed class AtlasSwatchGrid : Control
{
	private readonly Func<IReadOnlyList<AtlasPixel>> colors;
	private readonly Action<AtlasPixel> select;

	internal AtlasSwatchGrid(Func<IReadOnlyList<AtlasPixel>> colors, Action<AtlasPixel> select)
	{
		this.colors = colors;
		this.select = select;
	}

	public override void HandleMouseClick(global::FishUI.FishUI ui, FishInputState input,
		FishMouseButton button, Vector2 position)
	{
		base.HandleMouseClick(ui, input, button, position);
		if (button != FishMouseButton.Left)
			return;
		Vector2 local = position - GetAbsolutePosition();
		int column = Math.Clamp((int)(local.X / (GetAbsoluteSize().X / 8)), 0, 7);
		int row = Math.Clamp((int)(local.Y / (GetAbsoluteSize().Y / 4)), 0, 3);
		int index = row * 8 + column;
		IReadOnlyList<AtlasPixel> values = colors();
		if (index < values.Count)
			select(values[index]);
	}

	public override void DrawControl(global::FishUI.FishUI ui, float deltaTime, float time)
	{
		Vector2 position = GetAbsolutePosition();
		Vector2 size = GetAbsoluteSize();
		float width = size.X / 8;
		float height = size.Y / 4;
		IReadOnlyList<AtlasPixel> values = colors();
		for (int index = 0; index < 32; index++)
		{
			int x = index % 8;
			int y = index / 8;
			Vector2 cell = position + new Vector2(x * width, y * height);
			FishColor background = new(0x19, 0x1d, 0x25);
			ui.Graphics.DrawRectangle(cell, new Vector2(width - 2, height - 2), background);
			if (index < values.Count)
			{
				AtlasPixel pixel = values[index];
				ui.Graphics.DrawRectangle(cell + new Vector2(2), new Vector2(width - 6, height - 6),
					new FishColor(pixel.R, pixel.G, pixel.B, pixel.A));
			}
		}
	}
}
#endif
