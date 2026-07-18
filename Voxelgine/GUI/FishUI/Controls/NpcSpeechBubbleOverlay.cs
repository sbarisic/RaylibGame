using System.Numerics;
using FishUI;
using FishUI.Controls;

namespace Voxelgine.GUI;

internal readonly record struct NpcSpeechBubbleItem(
	int NetworkId,
	string Text,
	Vector2 Anchor,
	float Distance
);

internal sealed class NpcSpeechBubbleOverlay : Control
{
	private const float MaximumWidth = 260;
	private const float BubblePadding = 8;
	private const float EdgePadding = 8;
	private const float PointerHeight = 10;
	private readonly List<NpcSpeechBubbleItem> items = new();

	public NpcSpeechBubbleOverlay()
	{
		Focusable = false;
	}

	public void SetItems(IEnumerable<NpcSpeechBubbleItem> visibleItems)
	{
		items.Clear();
		items.AddRange(visibleItems.OrderBy(static item => item.Distance));
	}

	public override void DrawControl(global::FishUI.FishUI UI, float Dt, float Time)
	{
		if (items.Count == 0)
			return;

		List<(Vector2 Position, Vector2 Size)> occupied = new();
		foreach (NpcSpeechBubbleItem item in items)
		{
			List<string> lines = Wrap(UI, item.Text, MaximumWidth - BubblePadding * 2);
			float lineHeight = MathF.Max(1, UI.Graphics.MeasureText(UI.Settings.FontDefault, "Ag").Y);
			float textWidth = lines.Count == 0
				? 1
				: lines.Max(line => UI.Graphics.MeasureText(UI.Settings.FontDefault, line).X);
			Vector2 panelSize = new(
				MathF.Min(MaximumWidth, textWidth + BubblePadding * 2),
				lines.Count * lineHeight + BubblePadding * 2
			);
			Vector2 panelPosition = new(
				item.Anchor.X - panelSize.X * 0.5f,
				item.Anchor.Y - panelSize.Y - PointerHeight
			);

			panelPosition.X = Math.Clamp(panelPosition.X, EdgePadding, MathF.Max(EdgePadding, Size.X - panelSize.X - EdgePadding));
			panelPosition.Y = Math.Clamp(panelPosition.Y, EdgePadding, MathF.Max(EdgePadding, Size.Y - panelSize.Y - PointerHeight - EdgePadding));
			while (occupied.Any(rect => Overlaps(panelPosition, panelSize, rect.Position, rect.Size)))
			{
				panelPosition.Y = MathF.Max(EdgePadding, panelPosition.Y - panelSize.Y - 4);
				if (panelPosition.Y <= EdgePadding)
					break;
			}

			occupied.Add((panelPosition, panelSize));
			DrawPanel(UI, panelPosition, panelSize, item.Anchor, lines, lineHeight);
		}
	}

	private static void DrawPanel(
		global::FishUI.FishUI UI,
		Vector2 position,
		Vector2 size,
		Vector2 anchor,
		IReadOnlyList<string> lines,
		float lineHeight
	)
	{
		FishColor background = new(20, 22, 28, 225);
		FishColor border = new(220, 220, 225, 255);
		UI.Graphics.DrawRectangle(position, size, background);
		UI.Graphics.DrawRectangle(position, new Vector2(size.X, 1), border);
		UI.Graphics.DrawRectangle(position + new Vector2(0, size.Y - 1), new Vector2(size.X, 1), border);
		UI.Graphics.DrawRectangle(position, new Vector2(1, size.Y), border);
		UI.Graphics.DrawRectangle(position + new Vector2(size.X - 1, 0), new Vector2(1, size.Y), border);

		float pointerX = Math.Clamp(anchor.X, position.X + 12, position.X + size.X - 12);
		Vector2 pointerTop = new(pointerX, position.Y + size.Y);
		Vector2 pointerTip = new(anchor.X, MathF.Min(anchor.Y, pointerTop.Y + PointerHeight));
		UI.Graphics.DrawLine(pointerTop - new Vector2(7, 0), pointerTip, 2, border);
		UI.Graphics.DrawLine(pointerTop + new Vector2(7, 0), pointerTip, 2, border);

		for (int i = 0; i < lines.Count; i++)
		{
			UI.Graphics.DrawTextColor(
				UI.Settings.FontDefault,
				lines[i],
				position + new Vector2(BubblePadding, BubblePadding + i * lineHeight),
				FishColor.White
			);
		}
	}

	private static List<string> Wrap(global::FishUI.FishUI UI, string text, float width)
	{
		List<string> lines = new();
		foreach (string paragraph in (text ?? string.Empty).Replace("\r", string.Empty).Split('\n'))
		{
			string current = string.Empty;
			foreach (string word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
			{
				string candidate = current.Length == 0 ? word : $"{current} {word}";
				if (current.Length > 0 && UI.Graphics.MeasureText(UI.Settings.FontDefault, candidate).X > width)
				{
					lines.Add(current);
					current = word;
				}
				else
				{
					current = candidate;
				}
			}
			lines.Add(current);
		}
		return lines;
	}

	private static bool Overlaps(Vector2 aPosition, Vector2 aSize, Vector2 bPosition, Vector2 bSize)
	{
		return aPosition.X < bPosition.X + bSize.X
			&& aPosition.X + aSize.X > bPosition.X
			&& aPosition.Y < bPosition.Y + bSize.Y
			&& aPosition.Y + aSize.Y > bPosition.Y;
	}
}
