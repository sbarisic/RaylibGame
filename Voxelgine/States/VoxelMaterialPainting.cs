#if WINDOWS
using System.Numerics;
using FishGfx.Graphics;
using FishGfx.Voxels;
using Voxelgine.Engine;
using Voxelgine.FishGfxClient.Voxels;
using Voxelgine.Graphics;

namespace Voxelgine.States;

internal readonly record struct VoxelPaintHit(
	float Distance,
	Vector3 Position,
	Vector3 Normal,
	VoxelFace Face,
	int TriangleIndex,
	Vector2 Uv,
	int TextureLayer,
	AtlasPaintTarget Target,
	int LocalX,
	int LocalY)
{
	internal int DocumentX => Target.X + LocalX;
	internal int DocumentY => Target.Y + LocalY;
	internal bool Editable => Target.Editable;
}

internal static class VoxelMaterialPicker
{
	internal static bool TryPick(
		Camera camera,
		Vector2 logicalMouse,
		Vector2 logicalSize,
		Vector2 framebufferSize,
		VoxelMaterialPaintGeometry geometry,
		AtlasEditingSession session,
		AtlasPaintLayer layer,
		out VoxelPaintHit hit)
	{
		ArgumentNullException.ThrowIfNull(camera);
		Vector2 framebufferMouse = LogicalToFramebuffer(logicalMouse, logicalSize, framebufferSize);
		PickingRay ray = camera.CreatePickingRay(framebufferMouse);
		IReadOnlyList<VoxelVertex> vertices = geometry.Model.Vertices;
		List<Candidate> candidates = new(vertices.Count / 3);
		for (int index = 0; index < vertices.Count; index += 3)
		{
			VoxelVertex a = vertices[index];
			VoxelVertex b = vertices[index + 1];
			VoxelVertex c = vertices[index + 2];
			if (TryIntersect(ray, a.Position, b.Position, c.Position,
				out float distance, out float weightA, out float weightB, out float weightC))
			{
				Vector2 uv = a.TextureCoordinates * weightA
					+ b.TextureCoordinates * weightB
					+ c.TextureCoordinates * weightC;
				Vector3 normal = Vector3.Normalize(a.Normal * weightA + b.Normal * weightB + c.Normal * weightC);
				candidates.Add(new Candidate(distance, ray.GetPoint(distance), normal, uv, a.TextureLayer, index / 3));
			}
		}

		foreach (Candidate candidate in candidates.OrderBy(static value => value.Distance))
		{
			AtlasPaintTarget target = session.GetTarget(geometry.Value.Type, layer, candidate.TextureLayer);
			(int localX, int localY) = ToTargetPixel(candidate.Uv, candidate.TextureLayer, target);
			if (geometry.Material.RenderMode == VoxelRenderMode.Cutout)
			{
				AtlasPaintTarget alphaTarget = session.GetTarget(
					geometry.Value.Type, AtlasPaintLayer.BaseColor, candidate.TextureLayer);
				(int alphaX, int alphaY) = ToTargetPixel(candidate.Uv, candidate.TextureLayer, alphaTarget);
				if (alphaTarget.Get(alphaX, alphaY).A < geometry.Material.ShadowAlphaCutoff * byte.MaxValue)
					continue;
			}

			hit = new VoxelPaintHit(
				candidate.Distance,
				candidate.Position,
				candidate.Normal,
				FaceFromNormal(candidate.Normal),
				candidate.TriangleIndex,
				candidate.Uv,
				candidate.TextureLayer,
				target,
				localX,
				localY);
			return true;
		}

		hit = default;
		return false;
	}

	internal static Vector2 LogicalToFramebuffer(Vector2 logical, Vector2 logicalSize, Vector2 framebufferSize)
	{
		if (logicalSize.X <= 0 || logicalSize.Y <= 0)
			throw new ArgumentOutOfRangeException(nameof(logicalSize));
		return new Vector2(
			logical.X * framebufferSize.X / logicalSize.X,
			logical.Y * framebufferSize.Y / logicalSize.Y);
	}

	internal static (int X, int Y) UvToTopLeftPixel(Vector2 uv, int width, int height) =>
		(
			Math.Clamp((int)MathF.Floor(uv.X * width), 0, width - 1),
			Math.Clamp((int)MathF.Floor((1 - uv.Y) * height), 0, height - 1)
		);

	private static (int X, int Y) ToTargetPixel(Vector2 uv, int textureLayer, AtlasPaintTarget target)
	{
		if (target.CustomDefinition != null)
		{
			(int atlasX, int atlasY) = UvToTopLeftPixel(uv, AtlasEditingSession.AtlasSize, AtlasEditingSession.AtlasSize);
			return (
				Math.Clamp(atlasX - target.CustomDefinition.X, 0, target.Width - 1),
				Math.Clamp(atlasY - target.CustomDefinition.Y, 0, target.Height - 1));
		}
		return UvToTopLeftPixel(uv, target.Width, target.Height);
	}

	private static VoxelFace FaceFromNormal(Vector3 normal)
	{
		Vector3 absolute = Vector3.Abs(normal);
		if (absolute.X >= absolute.Y && absolute.X >= absolute.Z)
			return normal.X >= 0 ? VoxelFace.PositiveX : VoxelFace.NegativeX;
		if (absolute.Y >= absolute.Z)
			return normal.Y >= 0 ? VoxelFace.PositiveY : VoxelFace.NegativeY;
		return normal.Z >= 0 ? VoxelFace.PositiveZ : VoxelFace.NegativeZ;
	}

	private static bool TryIntersect(
		PickingRay ray,
		Vector3 a,
		Vector3 b,
		Vector3 c,
		out float distance,
		out float weightA,
		out float weightB,
		out float weightC)
	{
		const float epsilon = 1e-6f;
		Vector3 edge1 = b - a;
		Vector3 edge2 = c - a;
		Vector3 p = Vector3.Cross(ray.Direction, edge2);
		float determinant = Vector3.Dot(edge1, p);
		if (MathF.Abs(determinant) <= epsilon)
		{
			distance = weightA = weightB = weightC = 0;
			return false;
		}
		float inverse = 1 / determinant;
		Vector3 t = ray.Origin - a;
		weightB = Vector3.Dot(t, p) * inverse;
		if (weightB < 0 || weightB > 1)
		{
			distance = weightA = weightC = 0;
			return false;
		}
		Vector3 q = Vector3.Cross(t, edge1);
		weightC = Vector3.Dot(ray.Direction, q) * inverse;
		if (weightC < 0 || weightB + weightC > 1)
		{
			distance = weightA = 0;
			return false;
		}
		distance = Vector3.Dot(edge2, q) * inverse;
		weightA = 1 - weightB - weightC;
		return distance >= 0;
	}

	private readonly record struct Candidate(
		float Distance,
		Vector3 Position,
		Vector3 Normal,
		Vector2 Uv,
		int TextureLayer,
		int TriangleIndex);
}

internal sealed class VoxelPaintStroke
{
	private readonly AtlasEditingSession session;
	private readonly AtlasHeightStore heights;
	private readonly bool invalidateHeight;
	private readonly Dictionary<(string Document, int X, int Y), AtlasPixelDelta> deltas = new();
	private VoxelPaintHit previous;
	private bool hasPrevious;

	internal VoxelPaintStroke(AtlasEditingSession session, AtlasHeightStore heights = null,
		bool invalidateHeight = false)
	{
		this.session = session ?? throw new ArgumentNullException(nameof(session));
		this.heights = heights;
		this.invalidateHeight = invalidateHeight;
	}

	internal bool Paint(VoxelPaintHit hit, AtlasPixel color)
	{
		if (!hit.Editable)
			return false;
		bool changed = false;
		if (hasPrevious && CanInterpolate(previous, hit))
		{
			foreach ((int x, int y) in TraverseLine(previous.DocumentX, previous.DocumentY, hit.DocumentX, hit.DocumentY))
				changed |= PaintPixel(hit.Target.Document, x, y, color);
		}
		else
		{
			changed = PaintPixel(hit.Target.Document, hit.DocumentX, hit.DocumentY, color);
		}
		previous = hit;
		hasPrevious = true;
		return changed;
	}

	internal bool Commit()
	{
		if (deltas.Count == 0)
			return false;
		AtlasHeightCacheSnapshot before = invalidateHeight && hasPrevious
			? heights?.Remove(previous.Target) : null;
		IEnumerable<AtlasHeightCacheChange> cacheChanges = before == null
			? null
			: new[] { new AtlasHeightCacheChange(before.Key, before, null) };
		session.History.Commit(deltas.Values, cacheChanges);
		return true;
	}

	private bool PaintPixel(AtlasImageDocument document, int x, int y, AtlasPixel color)
	{
		AtlasPixel previousColor = document.GetPixel(x, y);
		if (!document.SetPixel(x, y, color))
			return false;
		var key = (document.Key, x, y);
		if (deltas.TryGetValue(key, out AtlasPixelDelta existing))
			deltas[key] = existing with { Current = color };
		else
			deltas.Add(key, new AtlasPixelDelta(document.Key, x, y, previousColor, color));
		return true;
	}

	private static bool CanInterpolate(VoxelPaintHit left, VoxelPaintHit right) =>
		ReferenceEquals(left.Target.Document, right.Target.Document)
		&& left.Target.X == right.Target.X && left.Target.Y == right.Target.Y
		&& left.Target.Width == right.Target.Width && left.Target.Height == right.Target.Height
		&& left.Face == right.Face && left.TextureLayer == right.TextureLayer
		&& left.TriangleIndex == right.TriangleIndex;

	private static IEnumerable<(int X, int Y)> TraverseLine(int x0, int y0, int x1, int y1)
	{
		int dx = Math.Abs(x1 - x0);
		int sx = x0 < x1 ? 1 : -1;
		int dy = -Math.Abs(y1 - y0);
		int sy = y0 < y1 ? 1 : -1;
		int error = dx + dy;
		while (true)
		{
			yield return (x0, y0);
			if (x0 == x1 && y0 == y1)
				yield break;
			int doubled = error * 2;
			if (doubled >= dy) { error += dy; x0 += sx; }
			if (doubled <= dx) { error += dx; y0 += sy; }
		}
	}
}

internal sealed class VoxelHeightPaintStroke
{
	private readonly AtlasEditingSession session;
	private readonly AtlasHeightStore heights;
	private readonly Dictionary<(string Document, int X, int Y), AtlasPixelDelta> normalDeltas = new();
	private readonly Dictionary<(int X, int Y), (byte Previous, byte Current)> heightDeltas = new();
	private AtlasPaintTarget target;
	private AtlasHeightField field;
	private AtlasHeightCacheSnapshot before;
	private VoxelPaintHit previous;
	private bool hasPrevious;

	internal VoxelHeightPaintStroke(AtlasEditingSession session, AtlasHeightStore heights)
	{
		this.session = session ?? throw new ArgumentNullException(nameof(session));
		this.heights = heights ?? throw new ArgumentNullException(nameof(heights));
	}

	internal bool Paint(VoxelPaintHit hit, byte value)
	{
		if (!hit.Editable)
			return false;
		if (field == null)
		{
			target = hit.Target;
			field = heights.GetOrCreate(target);
			before = field.Snapshot();
		}
		if (AtlasHeightStore.Key(hit.Target) != field.Key)
			return false;

		bool changed = false;
		if (hasPrevious && CanInterpolate(previous, hit))
		{
			foreach ((int x, int y) in TraverseLine(previous.LocalX, previous.LocalY, hit.LocalX, hit.LocalY))
				changed |= PaintPixel(x, y, value);
		}
		else
		{
			changed = PaintPixel(hit.LocalX, hit.LocalY, value);
		}
		previous = hit;
		hasPrevious = true;
		return changed;
	}

	internal bool Commit()
	{
		if (field == null || heightDeltas.Count == 0)
			return false;
		session.History.Commit(normalDeltas.Values, new[]
		{
			new AtlasHeightCacheChange(field.Key, before, field.Snapshot()),
		});
		return true;
	}

	private bool PaintPixel(int x, int y, byte value)
	{
		byte previousValue = field.Get(x, y);
		if (previousValue == value)
			return false;
		field.Set(x, y, value);
		if (heightDeltas.TryGetValue((x, y), out var existing))
			heightDeltas[(x, y)] = (existing.Previous, value);
		else
			heightDeltas[(x, y)] = (previousValue, value);

		IEnumerable<(int X, int Y)> affected =
			from offsetY in Enumerable.Range(-1, 3)
			from offsetX in Enumerable.Range(-1, 3)
			select (x + offsetX, y + offsetY);
		foreach (AtlasPixelDelta delta in heights.RegenerateNormals(target, field, affected))
		{
			var key = (delta.DocumentKey, delta.X, delta.Y);
			if (normalDeltas.TryGetValue(key, out AtlasPixelDelta current))
				normalDeltas[key] = current with { Current = delta.Current };
			else
				normalDeltas[key] = delta;
		}
		return true;
	}

	private static bool CanInterpolate(VoxelPaintHit left, VoxelPaintHit right) =>
		AtlasHeightStore.Key(left.Target) == AtlasHeightStore.Key(right.Target)
		&& left.Face == right.Face && left.TextureLayer == right.TextureLayer
		&& left.TriangleIndex == right.TriangleIndex;

	private static IEnumerable<(int X, int Y)> TraverseLine(int x0, int y0, int x1, int y1)
	{
		int dx = Math.Abs(x1 - x0);
		int sx = x0 < x1 ? 1 : -1;
		int dy = -Math.Abs(y1 - y0);
		int sy = y0 < y1 ? 1 : -1;
		int error = dx + dy;
		while (true)
		{
			yield return (x0, y0);
			if (x0 == x1 && y0 == y1)
				yield break;
			int doubled = error * 2;
			if (doubled >= dy) { error += dy; x0 += sx; }
			if (doubled <= dx) { error += dx; y0 += sy; }
		}
	}
}

internal sealed class AtlasReverseUsageCatalog
{
	private readonly Dictionary<int, IReadOnlyList<string>> usages;

	internal AtlasReverseUsageCatalog(Func<BlockType, VoxelMaterialPreviewInfo> describe)
	{
		ArgumentNullException.ThrowIfNull(describe);
		Dictionary<int, List<string>> mutable = new();
		foreach (BlockType block in Enum.GetValues<BlockType>().Where(static value => value != BlockType.None))
		{
			VoxelMaterialPreviewInfo info = describe(block);
			if (info.IsCustomModel)
				continue;
			foreach (VoxelFace face in Enum.GetValues<VoxelFace>())
			{
				int tile = info.AtlasTiles[face];
				if (!mutable.TryGetValue(tile, out List<string> values))
					mutable.Add(tile, values = new List<string>());
				if (block is BlockType.StoneStairs or BlockType.WoodStairs or BlockType.ConcreteStairs)
				{
					for (int state = 0; state < 8; state++)
						values.Add($"{block} state {state} {face}");
				}
				else
				{
					values.Add($"{block} {face}");
				}
			}
		}
		usages = mutable.ToDictionary(
			static pair => pair.Key,
			static pair => (IReadOnlyList<string>)pair.Value.Distinct().Order().ToArray());
	}

	internal IReadOnlyList<string> Get(int tile) =>
		usages.TryGetValue(tile, out IReadOnlyList<string> values) ? values : Array.Empty<string>();
}
#endif
