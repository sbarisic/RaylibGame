#if WINDOWS
using System.Drawing;
using System.Drawing.Imaging;
using System.Numerics;
using System.Security.Cryptography;
using Voxelgine.Engine;
using Voxelgine.Graphics;

namespace Voxelgine.States;

internal enum AtlasPaintLayer
{
	BaseColor,
	Normal,
	Specular,
	Roughness,
}

internal readonly record struct AtlasPixel(byte R, byte G, byte B, byte A)
{
	internal static AtlasPixel Transparent => new(0, 0, 0, 0);
	internal uint Packed => (uint)(R | G << 8 | B << 16 | A << 24);
	internal string Hex => $"#{R:X2}{G:X2}{B:X2}{A:X2}";

	internal static AtlasPixel FromPacked(uint value) => new(
		(byte)value,
		(byte)(value >> 8),
		(byte)(value >> 16),
		(byte)(value >> 24));

	internal static AtlasPixel Scalar(byte value) => new(value, value, value, byte.MaxValue);

	internal static AtlasPixel Normal(float x, float y)
	{
		Vector2 xy = new(x, y);
		if (xy.LengthSquared() > 1)
			xy = Vector2.Normalize(xy);
		float z = MathF.Sqrt(MathF.Max(0, 1 - xy.LengthSquared()));
		return new AtlasPixel(EncodeNormal(xy.X), EncodeNormal(xy.Y), EncodeNormal(z), byte.MaxValue);
	}

	private static byte EncodeNormal(float value) =>
		(byte)Math.Clamp((int)MathF.Round((value * 0.5f + 0.5f) * byte.MaxValue), 0, byte.MaxValue);
}

internal sealed record AtlasAssetPaths(string SourceRoot, string RuntimeRoot)
{
	private static readonly string[] AtlasNames =
	{
		"atlas.png", "atlas_normal.png", "atlas_specular.png", "atlas_roughness.png",
	};

	internal bool CanWriteSource => SourceRoot != null;

	internal static AtlasAssetPaths Resolve(
		string explicitSourceRoot,
		string runtimeRoot,
		string workingDirectory,
		string applicationDirectory)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(runtimeRoot);
		if (!string.IsNullOrWhiteSpace(explicitSourceRoot))
		{
			string explicitRoot = Path.GetFullPath(explicitSourceRoot);
			return new AtlasAssetPaths(
				ValidateDataRoot(explicitRoot)
					? explicitRoot
					: throw new DirectoryNotFoundException(
						$"The explicit atlas source root '{explicitRoot}' does not contain all four atlases."),
				Path.GetFullPath(runtimeRoot));
		}

		foreach (string start in new[] { workingDirectory, applicationDirectory }
			.Where(static value => !string.IsNullOrWhiteSpace(value))
			.Select(Path.GetFullPath)
			.Distinct(StringComparer.OrdinalIgnoreCase))
		{
			for (DirectoryInfo current = new(start); current != null; current = current.Parent)
			{
				foreach (string projectRoot in new[] { current.FullName, Path.Combine(current.FullName, "Voxelgine") })
				{
					string project = Path.Combine(projectRoot, "Voxelgine.csproj");
					string data = Path.Combine(projectRoot, "data");
					if (File.Exists(project) && ValidateDataRoot(data))
						return new AtlasAssetPaths(Path.GetFullPath(data), Path.GetFullPath(runtimeRoot));
				}
			}
		}

		return new AtlasAssetPaths(null, Path.GetFullPath(runtimeRoot));
	}

	internal static bool ValidateDataRoot(string root) =>
		Directory.Exists(root) && AtlasNames.All(name => File.Exists(Path.Combine(root, "textures", name)));
}

internal sealed class AtlasImageDocument
{
	private byte[] pixels;
	private byte[] savedPixels;

	private AtlasImageDocument(string key, string relativePath, string loadedPath, bool editable,
		int width, int height, byte[] pixels, byte[] loadedHash)
	{
		Key = key;
		RelativePath = relativePath;
		LoadedPath = loadedPath;
		Editable = editable;
		Width = width;
		Height = height;
		this.pixels = pixels;
		savedPixels = (byte[])pixels.Clone();
		LoadedHash = loadedHash;
	}

	internal string Key { get; }
	internal string RelativePath { get; }
	internal string LoadedPath { get; }
	internal bool Editable { get; }
	internal int Width { get; }
	internal int Height { get; }
	internal byte[] LoadedHash { get; private set; }
	internal bool IsDirty => !pixels.AsSpan().SequenceEqual(savedPixels);
	internal ReadOnlySpan<byte> Pixels => pixels;

	internal static AtlasImageDocument Load(
		string key,
		string relativePath,
		string sourceRoot,
		string runtimeRoot,
		bool sourceRequiredForEditing = true)
	{
		string sourcePath = sourceRoot == null ? null : Path.Combine(sourceRoot, relativePath);
		bool editable = !sourceRequiredForEditing || sourcePath != null && File.Exists(sourcePath);
		string loadedPath = editable ? sourcePath : Path.Combine(runtimeRoot, relativePath);
		if (!File.Exists(loadedPath))
			throw new FileNotFoundException($"Atlas document '{relativePath}' was not found.", loadedPath);
		byte[] encoded = File.ReadAllBytes(loadedPath);
		using MemoryStream stream = new(encoded, writable: false);
		using Bitmap bitmap = new(stream);
		return new AtlasImageDocument(
			key,
			relativePath,
			loadedPath,
			editable,
			bitmap.Width,
			bitmap.Height,
			AtlasBitmapCodec.Read(bitmap),
			SHA256.HashData(encoded));
	}

	internal AtlasPixel GetPixel(int x, int y)
	{
		ValidateCoordinate(x, y);
		int offset = (y * Width + x) * 4;
		return new AtlasPixel(pixels[offset], pixels[offset + 1], pixels[offset + 2], pixels[offset + 3]);
	}

	internal bool SetPixel(int x, int y, AtlasPixel color)
	{
		if (!Editable)
			return false;
		ValidateCoordinate(x, y);
		int offset = (y * Width + x) * 4;
		AtlasPixel previous = GetPixel(x, y);
		if (previous == color)
			return false;
		pixels[offset] = color.R;
		pixels[offset + 1] = color.G;
		pixels[offset + 2] = color.B;
		pixels[offset + 3] = color.A;
		return true;
	}

	internal Bitmap CreateBitmap() => AtlasBitmapCodec.Create(Width, Height, pixels);

	internal void RestorePixel(int x, int y, AtlasPixel color)
	{
		int offset = (y * Width + x) * 4;
		pixels[offset] = color.R;
		pixels[offset + 1] = color.G;
		pixels[offset + 2] = color.B;
		pixels[offset + 3] = color.A;
	}

	internal void Discard() => pixels = (byte[])savedPixels.Clone();

	internal void Reload()
	{
		byte[] encoded = File.ReadAllBytes(LoadedPath);
		using MemoryStream stream = new(encoded, writable: false);
		using Bitmap bitmap = new(stream);
		if (bitmap.Width != Width || bitmap.Height != Height)
			throw new InvalidDataException($"Reloaded document '{LoadedPath}' changed dimensions.");
		pixels = AtlasBitmapCodec.Read(bitmap);
		savedPixels = (byte[])pixels.Clone();
		LoadedHash = SHA256.HashData(encoded);
	}

	internal void MarkSaved(string path)
	{
		savedPixels = (byte[])pixels.Clone();
		LoadedHash = SHA256.HashData(File.ReadAllBytes(path));
	}

	internal void AcceptSavedBitmap(Bitmap bitmap, string path)
	{
		pixels = AtlasBitmapCodec.Read(bitmap);
		savedPixels = (byte[])pixels.Clone();
		LoadedHash = SHA256.HashData(File.ReadAllBytes(path));
	}

	internal IReadOnlyList<AtlasPixel> GetFrequentColors(int x, int y, int width, int height, int maximum)
	{
		Dictionary<uint, int> counts = new();
		for (int py = Math.Max(0, y); py < Math.Min(Height, y + height); py++)
		for (int px = Math.Max(0, x); px < Math.Min(Width, x + width); px++)
		{
			uint packed = GetPixel(px, py).Packed;
			counts[packed] = counts.GetValueOrDefault(packed) + 1;
		}
		return counts.OrderByDescending(static pair => pair.Value)
			.ThenBy(static pair => pair.Key)
			.Take(maximum)
			.Select(static pair => AtlasPixel.FromPacked(pair.Key))
			.ToArray();
	}

	private void ValidateCoordinate(int x, int y)
	{
		if ((uint)x >= Width || (uint)y >= Height)
			throw new ArgumentOutOfRangeException($"Pixel ({x}, {y}) is outside {Width}x{Height} document '{Key}'.");
	}
}

internal readonly record struct AtlasPixelDelta(
	string DocumentKey,
	int X,
	int Y,
	AtlasPixel Previous,
	AtlasPixel Current);

internal sealed class AtlasStrokeHistory
{
	private const int Capacity = 100;
	private readonly List<IReadOnlyList<AtlasPixelDelta>> undo = new();
	private readonly List<IReadOnlyList<AtlasPixelDelta>> redo = new();

	internal bool CanUndo => undo.Count > 0;
	internal bool CanRedo => redo.Count > 0;

	internal void Commit(IEnumerable<AtlasPixelDelta> deltas)
	{
		IReadOnlyList<AtlasPixelDelta> stroke = deltas
			.GroupBy(static delta => (delta.DocumentKey, delta.X, delta.Y))
			.Select(static group => new AtlasPixelDelta(
				group.Key.DocumentKey,
				group.Key.X,
				group.Key.Y,
				group.First().Previous,
				group.Last().Current))
			.Where(static delta => delta.Previous != delta.Current)
			.ToArray();
		if (stroke.Count == 0)
			return;
		undo.Add(stroke);
		if (undo.Count > Capacity)
			undo.RemoveAt(0);
		redo.Clear();
	}

	internal bool Undo(IReadOnlyDictionary<string, AtlasImageDocument> documents) =>
		Transfer(undo, redo, documents, usePrevious: true);

	internal bool Redo(IReadOnlyDictionary<string, AtlasImageDocument> documents) =>
		Transfer(redo, undo, documents, usePrevious: false);

	internal void Clear()
	{
		undo.Clear();
		redo.Clear();
	}

	private static bool Transfer(
		List<IReadOnlyList<AtlasPixelDelta>> source,
		List<IReadOnlyList<AtlasPixelDelta>> destination,
		IReadOnlyDictionary<string, AtlasImageDocument> documents,
		bool usePrevious)
	{
		if (source.Count == 0)
			return false;
		IReadOnlyList<AtlasPixelDelta> stroke = source[^1];
		source.RemoveAt(source.Count - 1);
		foreach (AtlasPixelDelta delta in stroke)
			documents[delta.DocumentKey].RestorePixel(delta.X, delta.Y, usePrevious ? delta.Previous : delta.Current);
		destination.Add(stroke);
		return true;
	}
}

internal sealed class AtlasEditingSession
{
	internal const int AtlasSize = 512;
	internal const int TileSize = 32;
	private static readonly IReadOnlyDictionary<AtlasPaintLayer, string> LayerPaths =
		new Dictionary<AtlasPaintLayer, string>
		{
			[AtlasPaintLayer.BaseColor] = "textures/atlas.png",
			[AtlasPaintLayer.Normal] = "textures/atlas_normal.png",
			[AtlasPaintLayer.Specular] = "textures/atlas_specular.png",
			[AtlasPaintLayer.Roughness] = "textures/atlas_roughness.png",
		};
	private static readonly IReadOnlyDictionary<BlockType, CustomDocumentDefinition> CustomDocuments =
		new Dictionary<BlockType, CustomDocumentDefinition>
		{
			[BlockType.Barrel] = new("models/barrel/barrel_tex.png", 8, 72, 64, 64),
			[BlockType.Campfire] = new("models/campfire/campfire_tex.png", 88, 72, 64, 64),
			[BlockType.Torch] = new("models/torch/torch_tex.png", 168, 72, 16, 16),
			[BlockType.Foliage] = new("models/grass/grass1_tex.png", 200, 72, 16, 16),
		};
	private readonly Dictionary<string, AtlasImageDocument> documents = new(StringComparer.Ordinal);
	private readonly Dictionary<AtlasPaintLayer, AtlasImageDocument> atlases = new();
	private readonly Dictionary<BlockType, AtlasImageDocument> customBase = new();

	internal AtlasEditingSession(AtlasAssetPaths paths)
	{
		Paths = paths ?? throw new ArgumentNullException(nameof(paths));
		foreach ((AtlasPaintLayer layer, string relativePath) in LayerPaths)
		{
			AtlasImageDocument document = AtlasImageDocument.Load(
				$"atlas:{layer}", relativePath, paths.SourceRoot, paths.RuntimeRoot);
			ValidateAtlas(document);
			atlases.Add(layer, document);
			documents.Add(document.Key, document);
		}
		foreach ((BlockType block, CustomDocumentDefinition definition) in CustomDocuments)
		{
			AtlasImageDocument document = AtlasImageDocument.Load(
				$"model:{block}", definition.RelativePath, paths.SourceRoot, paths.RuntimeRoot);
			if (document.Width != definition.Width || document.Height != definition.Height)
				throw new InvalidDataException($"Custom model texture '{definition.RelativePath}' has invalid dimensions.");
			customBase.Add(block, document);
			documents.Add(document.Key, document);
		}
	}

	internal AtlasAssetPaths Paths { get; }
	internal AtlasStrokeHistory History { get; } = new();
	internal IReadOnlyDictionary<string, AtlasImageDocument> Documents => documents;
	internal bool IsDirty => documents.Values.Any(static document => document.IsDirty);
	internal bool IsReadOnly => !Paths.CanWriteSource;

	internal AtlasPaintTarget GetTarget(BlockType block, AtlasPaintLayer layer, int textureLayer)
	{
		if (CustomDocuments.TryGetValue(block, out CustomDocumentDefinition custom))
		{
			if (layer == AtlasPaintLayer.BaseColor)
				return new AtlasPaintTarget(customBase[block], 0, 0, custom.Width, custom.Height, true, custom);
			return new AtlasPaintTarget(atlases[layer], custom.X, custom.Y, custom.Width, custom.Height, false, custom);
		}

		int tile = Math.Max(0, textureLayer);
		return new AtlasPaintTarget(
			atlases[layer],
			(tile % 16) * TileSize,
			(tile / 16) * TileSize,
			TileSize,
			TileSize,
			atlases[layer].Editable,
			null);
	}

	internal AtlasTextureSnapshot CreateTextureSnapshot()
	{
		Bitmap baseColor = atlases[AtlasPaintLayer.BaseColor].CreateBitmap();
		foreach ((BlockType block, CustomDocumentDefinition definition) in CustomDocuments)
		{
			using Bitmap source = customBase[block].CreateBitmap();
			AtlasBitmapCodec.DrawWithBorder(baseColor, source, definition.X, definition.Y);
		}
		return new AtlasTextureSnapshot(
			baseColor,
			atlases[AtlasPaintLayer.Normal].CreateBitmap(),
			atlases[AtlasPaintLayer.Specular].CreateBitmap(),
			atlases[AtlasPaintLayer.Roughness].CreateBitmap());
	}

	internal IReadOnlyList<AtlasSaveDocument> BuildSaveDocuments()
	{
		if (IsReadOnly)
			return Array.Empty<AtlasSaveDocument>();
		List<AtlasSaveDocument> result = new();
		using AtlasTextureSnapshot snapshot = CreateTextureSnapshot();
		foreach ((AtlasPaintLayer layer, AtlasImageDocument document) in atlases)
		{
			if (!document.IsDirty && layer != AtlasPaintLayer.BaseColor && !customBase.Values.Any(static value => value.IsDirty))
				continue;
			Bitmap bitmap = layer switch
			{
				AtlasPaintLayer.BaseColor => (Bitmap)snapshot.BaseColor.Clone(),
				AtlasPaintLayer.Normal => (Bitmap)snapshot.Normal.Clone(),
				AtlasPaintLayer.Specular => (Bitmap)snapshot.Specular.Clone(),
				AtlasPaintLayer.Roughness => (Bitmap)snapshot.Roughness.Clone(),
				_ => throw new ArgumentOutOfRangeException(),
			};
			result.Add(new AtlasSaveDocument(document, bitmap));
		}
		foreach (AtlasImageDocument document in customBase.Values.Where(static value => value.IsDirty))
			result.Add(new AtlasSaveDocument(document, document.CreateBitmap()));
		return result;
	}

	internal void Discard()
	{
		foreach (AtlasImageDocument document in documents.Values)
			document.Reload();
		History.Clear();
	}

	private static void ValidateAtlas(AtlasImageDocument document)
	{
		if (document.Width != AtlasSize || document.Height != AtlasSize)
			throw new InvalidDataException($"Atlas '{document.RelativePath}' must be {AtlasSize}x{AtlasSize}.");
	}

	internal sealed record CustomDocumentDefinition(string RelativePath, int X, int Y, int Width, int Height);
}

internal readonly record struct AtlasPaintTarget(
	AtlasImageDocument Document,
	int X,
	int Y,
	int Width,
	int Height,
	bool Editable,
	AtlasEditingSession.CustomDocumentDefinition CustomDefinition)
{
	internal AtlasPixel Get(int x, int y) => Document.GetPixel(X + x, Y + y);
	internal bool Set(int x, int y, AtlasPixel color) => Document.SetPixel(X + x, Y + y, color);
}

internal sealed record AtlasSaveDocument(AtlasImageDocument Document, Bitmap Bitmap) : IDisposable
{
	public void Dispose() => Bitmap.Dispose();
}

internal sealed record AtlasTextureSnapshot(Bitmap BaseColor, Bitmap Normal, Bitmap Specular, Bitmap Roughness) : IDisposable
{
	public void Dispose()
	{
		Roughness.Dispose();
		Specular.Dispose();
		Normal.Dispose();
		BaseColor.Dispose();
	}
}

internal static class AtlasBitmapCodec
{
	internal static byte[] Read(Bitmap source)
	{
		using Bitmap bitmap = EnsureArgb(source);
		Rectangle rectangle = new(0, 0, bitmap.Width, bitmap.Height);
		BitmapData data = bitmap.LockBits(rectangle, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
		try
		{
			byte[] bgra = new byte[Math.Abs(data.Stride) * bitmap.Height];
			System.Runtime.InteropServices.Marshal.Copy(data.Scan0, bgra, 0, bgra.Length);
			byte[] rgba = new byte[bitmap.Width * bitmap.Height * 4];
			for (int y = 0; y < bitmap.Height; y++)
			for (int x = 0; x < bitmap.Width; x++)
			{
				int sourceOffset = y * data.Stride + x * 4;
				int destinationOffset = (y * bitmap.Width + x) * 4;
				rgba[destinationOffset] = bgra[sourceOffset + 2];
				rgba[destinationOffset + 1] = bgra[sourceOffset + 1];
				rgba[destinationOffset + 2] = bgra[sourceOffset];
				rgba[destinationOffset + 3] = bgra[sourceOffset + 3];
			}
			return rgba;
		}
		finally
		{
			bitmap.UnlockBits(data);
		}
	}

	internal static Bitmap Create(int width, int height, ReadOnlySpan<byte> rgba)
	{
		if (rgba.Length != width * height * 4)
			throw new ArgumentException("RGBA buffer length does not match bitmap dimensions.", nameof(rgba));
		Bitmap bitmap = new(width, height, PixelFormat.Format32bppArgb);
		Rectangle rectangle = new(0, 0, width, height);
		BitmapData data = bitmap.LockBits(rectangle, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
		try
		{
			byte[] bgra = new byte[Math.Abs(data.Stride) * height];
			for (int y = 0; y < height; y++)
			for (int x = 0; x < width; x++)
			{
				int sourceOffset = (y * width + x) * 4;
				int destinationOffset = y * data.Stride + x * 4;
				bgra[destinationOffset] = rgba[sourceOffset + 2];
				bgra[destinationOffset + 1] = rgba[sourceOffset + 1];
				bgra[destinationOffset + 2] = rgba[sourceOffset];
				bgra[destinationOffset + 3] = rgba[sourceOffset + 3];
			}
			System.Runtime.InteropServices.Marshal.Copy(bgra, 0, data.Scan0, bgra.Length);
		}
		finally
		{
			bitmap.UnlockBits(data);
		}
		return bitmap;
	}

	internal static void DrawWithBorder(Bitmap destination, Bitmap source, int x, int y)
	{
		using System.Drawing.Graphics graphics = System.Drawing.Graphics.FromImage(destination);
		graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
		graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
		graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
		graphics.DrawImageUnscaled(source, x, y);
		for (int py = 0; py < source.Height; py++)
		{
			destination.SetPixel(x - 1, y + py, source.GetPixel(0, py));
			destination.SetPixel(x + source.Width, y + py, source.GetPixel(source.Width - 1, py));
		}
		for (int px = 0; px < source.Width; px++)
		{
			destination.SetPixel(x + px, y - 1, source.GetPixel(px, 0));
			destination.SetPixel(x + px, y + source.Height, source.GetPixel(px, source.Height - 1));
		}
	}

	private static Bitmap EnsureArgb(Bitmap source)
	{
		return source.Clone(
			new Rectangle(0, 0, source.Width, source.Height),
			PixelFormat.Format32bppArgb);
	}
}
#endif
