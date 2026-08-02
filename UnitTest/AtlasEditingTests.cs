using System.Drawing;
using System.Drawing.Imaging;
using FishGfx.Graphics;
using FishGfx.Voxels;
using Voxelgine.Engine;
using Voxelgine.FishGfxClient.Voxels;
using Voxelgine.Graphics;
using Voxelgine.States;

namespace UnitTest;

public sealed class AtlasEditingTests
{
	[Fact]
	public void BitmapCodecPreservesPixelCoordinatesAcrossDpiChanges()
	{
		using Bitmap source = new(64, 64, PixelFormat.Format32bppArgb);
		source.SetResolution(96, 96);
		using (Graphics graphics = Graphics.FromImage(source))
			graphics.Clear(Color.FromArgb(255, 12, 34, 56));
		source.SetPixel(31, 7, Color.Red);
		source.SetPixel(32, 7, Color.Blue);
		source.SetPixel(63, 63, Color.Lime);

		byte[] pixels = AtlasBitmapCodec.Read(source);
		using Bitmap roundTrip = AtlasBitmapCodec.Create(64, 64, pixels);

		Assert.Equal(Color.Red.ToArgb(), roundTrip.GetPixel(31, 7).ToArgb());
		Assert.Equal(Color.Blue.ToArgb(), roundTrip.GetPixel(32, 7).ToArgb());
		Assert.Equal(Color.Lime.ToArgb(), roundTrip.GetPixel(63, 63).ToArgb());
		Assert.Equal(Color.FromArgb(255, 12, 34, 56).ToArgb(), roundTrip.GetPixel(40, 7).ToArgb());
	}

	[Fact]
	public void ExplicitSourceRootWinsAndInvalidExplicitRootFailsClearly()
	{
		using TestAtlasRoots roots = new();
		AtlasAssetPaths paths = AtlasAssetPaths.Resolve(
			roots.Source, roots.Runtime, Path.GetTempPath(), AppContext.BaseDirectory);
		Assert.Equal(Path.GetFullPath(roots.Source), paths.SourceRoot);

		string invalid = Path.Combine(roots.Root, "invalid");
		Directory.CreateDirectory(invalid);
		Assert.Throws<DirectoryNotFoundException>(() => AtlasAssetPaths.Resolve(
			invalid, roots.Runtime, roots.Root, roots.Root));
	}

	[Fact]
	public void StrokeDeduplicatesPixelsAndUndoRedoRestoresExactValues()
	{
		using TestAtlasRoots roots = new();
		var session = new AtlasEditingSession(new AtlasAssetPaths(roots.Source, roots.Runtime));
		AtlasPaintTarget target = session.GetTarget(BlockType.Stone, AtlasPaintLayer.BaseColor, 0);
		AtlasPixel original = target.Get(0, 0);
		var hit = new VoxelPaintHit(1, default, default, FishGfx.Voxels.VoxelFace.PositiveY,
			0, default, 0, target, 0, 0);
		var stroke = new VoxelPaintStroke(session);

		Assert.True(stroke.Paint(hit, new AtlasPixel(10, 20, 30, 40)));
		Assert.True(stroke.Paint(hit, new AtlasPixel(50, 60, 70, 80)));
		Assert.True(stroke.Commit());
		Assert.Equal(new AtlasPixel(50, 60, 70, 80), target.Get(0, 0));
		Assert.True(session.History.Undo(session.Documents));
		Assert.Equal(original, target.Get(0, 0));
		Assert.True(session.History.Redo(session.Documents));
		Assert.Equal(new AtlasPixel(50, 60, 70, 80), target.Get(0, 0));
	}

	[Fact]
	public void GuardedSaveWritesOnlyInjectedRootsAndDetectsLaterConflict()
	{
		using TestAtlasRoots roots = new();
		var paths = new AtlasAssetPaths(roots.Source, roots.Runtime);
		var session = new AtlasEditingSession(paths);
		var save = new AtlasSaveService(paths, session.Documents.Values);
		AtlasPaintTarget target = session.GetTarget(BlockType.Stone, AtlasPaintLayer.BaseColor, 0);
		AtlasPixel painted = new(7, 11, 13, 255);
		Assert.True(target.Set(3, 5, painted));

		AtlasSaveResult result = save.Save(session.BuildSaveDocuments());
		Assert.Equal(AtlasSaveStatus.Saved, result.Status);
		foreach (string root in new[] { roots.Source, roots.Runtime })
		using (Bitmap bitmap = new(Path.Combine(root, "textures", "atlas.png")))
			Assert.Equal(Color.FromArgb(painted.A, painted.R, painted.G, painted.B), bitmap.GetPixel(3, 5));

		using (Bitmap external = new(Path.Combine(roots.Source, "textures", "atlas_normal.png")))
		{
			external.SetPixel(0, 0, Color.Magenta);
			external.Save(Path.Combine(roots.Source, "textures", "atlas_normal.changed.png"), ImageFormat.Png);
		}
		File.Move(
			Path.Combine(roots.Source, "textures", "atlas_normal.changed.png"),
			Path.Combine(roots.Source, "textures", "atlas_normal.png"), overwrite: true);
		AtlasPaintTarget normal = session.GetTarget(BlockType.Stone, AtlasPaintLayer.Normal, 0);
		Assert.True(normal.Set(1, 1, new AtlasPixel(1, 2, 3, 255)));
		Assert.Equal(AtlasSaveStatus.Conflict, save.Save(session.BuildSaveDocuments()).Status);
	}

	[Theory]
	[InlineData(0, 0, 0, 31)]
	[InlineData(1, 1, 31, 0)]
	[InlineData(0.9999f, 0.0001f, 31, 31)]
	public void UvConversionUsesTopLeftOriginAndClampsEdges(float u, float v, int x, int y)
	{
		Assert.Equal((x, y), VoxelMaterialPicker.UvToTopLeftPixel(new System.Numerics.Vector2(u, v), 32, 32));
	}

	[Fact]
	public void LogicalPickingCoordinatesUseIndependentHighDpiScales()
	{
		Assert.Equal(new System.Numerics.Vector2(300, 400), VoxelMaterialPicker.LogicalToFramebuffer(
			new System.Numerics.Vector2(100, 100),
			new System.Numerics.Vector2(800, 600),
			new System.Numerics.Vector2(2400, 2400)));
	}

	[Fact]
	public void ExactTrianglePickingReturnsNearestDisplayedCubeFace()
	{
		using TestAtlasRoots roots = new();
		var session = new AtlasEditingSession(new AtlasAssetPaths(roots.Source, roots.Runtime));
		VoxelModel model = StairVoxelModelBuilder.CreateCube(new VoxelFaceTiles(0));
		var material = new VoxelMaterial("Pick cube", VoxelRenderMode.Opaque, new VoxelFaceTiles(0));
		var geometry = new VoxelMaterialPaintGeometry(
			new BlockValue(BlockType.Stone), 1, material, model);
		var camera = new Camera { Position = new System.Numerics.Vector3(0.5f, 0.5f, 3) };
		camera.LookAt(new System.Numerics.Vector3(0.5f));
		camera.SetPerspective(new System.Numerics.Vector2(800, 600), MathF.PI / 4, 0.05f, 20);

		Assert.True(VoxelMaterialPicker.TryPick(camera, new System.Numerics.Vector2(400, 300),
			new System.Numerics.Vector2(800, 600), new System.Numerics.Vector2(800, 600),
			geometry, session, AtlasPaintLayer.BaseColor, out VoxelPaintHit hit));
		Assert.Equal(VoxelFace.PositiveZ, hit.Face);
		Assert.InRange(hit.Distance, 1.94f, 1.96f);
	}

	[Fact]
	public void CutoutPickingRejectsTransparentTrianglesBeforeReturningAHit()
	{
		using TestAtlasRoots roots = new();
		var session = new AtlasEditingSession(new AtlasAssetPaths(roots.Source, roots.Runtime));
		AtlasPaintTarget target = session.GetTarget(BlockType.Stone, AtlasPaintLayer.BaseColor, 0);
		for (int y = 0; y < target.Height; y++)
		for (int x = 0; x < target.Width; x++)
			target.Set(x, y, AtlasPixel.Transparent);
		VoxelModel model = StairVoxelModelBuilder.CreateCube(new VoxelFaceTiles(0));
		var material = new VoxelMaterial("Cutout cube", VoxelRenderMode.Cutout,
			new VoxelFaceTiles(0), occludesFaces: false);
		var geometry = new VoxelMaterialPaintGeometry(
			new BlockValue(BlockType.Stone), 1, material, model);
		var camera = new Camera { Position = new System.Numerics.Vector3(0.5f, 0.5f, 3) };
		camera.LookAt(new System.Numerics.Vector3(0.5f));
		camera.SetPerspective(new System.Numerics.Vector2(800, 600), MathF.PI / 4, 0.05f, 20);

		Assert.False(VoxelMaterialPicker.TryPick(camera, new System.Numerics.Vector2(400, 300),
			new System.Numerics.Vector2(800, 600), new System.Numerics.Vector2(800, 600),
			geometry, session, AtlasPaintLayer.BaseColor, out _));
	}

	private sealed class TestAtlasRoots : IDisposable
	{
		internal TestAtlasRoots()
		{
			Root = Path.Combine(Path.GetTempPath(), $"aurora-atlas-editor-{Guid.NewGuid():N}");
			Source = Path.Combine(Root, "source");
			Runtime = Path.Combine(Root, "runtime");
			CreateDataRoot(Source);
			CreateDataRoot(Runtime);
		}

		internal string Root { get; }
		internal string Source { get; }
		internal string Runtime { get; }

		public void Dispose()
		{
			if (Directory.Exists(Root))
				Directory.Delete(Root, recursive: true);
		}

		private static void CreateDataRoot(string root)
		{
			Directory.CreateDirectory(Path.Combine(root, "textures"));
			foreach (string name in new[] { "atlas.png", "atlas_normal.png", "atlas_specular.png", "atlas_roughness.png" })
			{
				using Bitmap bitmap = new(512, 512);
				using Graphics graphics = Graphics.FromImage(bitmap);
				graphics.Clear(name == "atlas_normal.png" ? Color.FromArgb(255, 128, 128, 255) : Color.Black);
				bitmap.Save(Path.Combine(root, "textures", name), ImageFormat.Png);
			}
			CreateModel(root, "barrel", "barrel_tex.png", 64, 64);
			CreateModel(root, "campfire", "campfire_tex.png", 64, 64);
			CreateModel(root, "torch", "torch_tex.png", 16, 16);
			CreateModel(root, "grass", "grass1_tex.png", 16, 16);
		}

		private static void CreateModel(string root, string directory, string name, int width, int height)
		{
			string path = Path.Combine(root, "models", directory);
			Directory.CreateDirectory(path);
			using Bitmap bitmap = new(width, height);
			bitmap.Save(Path.Combine(path, name), ImageFormat.Png);
		}
	}
}
