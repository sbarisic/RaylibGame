#if WINDOWS
using System.Numerics;
using Voxelgine.Engine;
using Voxelgine.Graphics;

namespace Voxelgine.States;

internal readonly record struct VoxelMaterialChoice(BlockType Type, BlockValue Value);

internal sealed class VoxelMaterialInspectorModel
{
	private readonly VoxelMaterialChoice[] choices = Enum.GetValues<BlockType>()
		.Where(static type => type != BlockType.None)
		.OrderBy(static type => (int)type)
		.Select(static type => new VoxelMaterialChoice(type, new BlockValue(type, 0)))
		.ToArray();

	internal VoxelMaterialInspectorModel(BlockType selected)
	{
		if (!choices.Any(choice => choice.Type == selected))
			throw new ArgumentOutOfRangeException(nameof(selected));
		Selected = selected;
	}

	internal IReadOnlyList<VoxelMaterialChoice> Choices => choices;
	internal BlockType Selected { get; private set; }

	internal IReadOnlyList<VoxelMaterialChoice> Filter(string query)
	{
		string normalized = query?.Trim() ?? string.Empty;
		if (normalized.Length == 0)
			return choices;
		return choices.Where(choice =>
			choice.Type.ToString().Contains(normalized, StringComparison.OrdinalIgnoreCase)
			|| ((int)choice.Type).ToString(System.Globalization.CultureInfo.InvariantCulture)
				.Contains(normalized, StringComparison.OrdinalIgnoreCase)).ToArray();
	}

	internal void Select(BlockType type)
	{
		if (!choices.Any(choice => choice.Type == type))
			throw new ArgumentOutOfRangeException(nameof(type));
		Selected = type;
	}
}

internal readonly record struct VoxelMaterialInspectorLayout(
	Vector2 Position,
	Vector2 Size,
	Vector2 CentralPosition,
	Vector2 CentralSize,
	float BlockListMinimumHeight)
{
	internal const float Margin = 16;
	internal const float Spacing = 12;
	internal const float MinimumBlockListHeight = 180;

	internal static VoxelMaterialInspectorLayout Calculate(float windowWidth, float windowHeight)
	{
		float width = Math.Min(
			Math.Clamp(windowWidth * 0.24f, 380, 440),
			Math.Max(0, windowWidth - Margin * 2));
		float height = Math.Max(0, windowHeight - Margin * 2);
		const float centralTop = 116;
		const float footerHeight = 82;
		float centralHeight = Math.Max(0, height - centralTop - footerHeight - Spacing);
		return new VoxelMaterialInspectorLayout(
			new Vector2(Margin, Margin),
			new Vector2(width, height),
			new Vector2(Spacing, centralTop),
			new Vector2(Math.Max(0, width - Spacing * 2), centralHeight),
			MinimumBlockListHeight);
	}

	internal bool Contains(Vector2 point) =>
		point.X >= Position.X && point.Y >= Position.Y
		&& point.X <= Position.X + Size.X && point.Y <= Position.Y + Size.Y;
}
#endif
