#if WINDOWS
using System.Collections.ObjectModel;
using FishGfx.Graphics;
using FishGfx.Voxels;
using Voxelgine.Engine;
using Voxelgine.FishGfxClient.Assets;
using Bitmap = System.Drawing.Bitmap;

namespace Voxelgine.FishGfxClient.Voxels;

internal sealed class FishGfxVoxelAssets
{
	internal const string SurfaceTextureAssetId = "voxel.surface-textures";
	private const int AtlasSize = 512;
	private const int CubeColumns = 16;
	private const int CubeRows = 16;
	private static readonly TextureSamplingState SurfaceSampling = new(
		TextureFilter.Nearest,
		TextureFilter.Nearest,
		TextureWrap.ClampToEdge,
		TextureWrap.ClampToEdge
	);

	private static readonly VoxelTextureRegion BarrelRegion =
		new(8, 72, 64, 64, AtlasSize, AtlasSize);
	private static readonly VoxelTextureRegion CampfireRegion =
		new(88, 72, 64, 64, AtlasSize, AtlasSize);
	private static readonly VoxelTextureRegion TorchRegion =
		new(168, 72, 16, 16, AtlasSize, AtlasSize);
	private static readonly VoxelTextureRegion FoliageRegion =
		new(200, 72, 16, 16, AtlasSize, AtlasSize);

	private readonly ReadOnlyDictionary<BlockType, ushort> materialIds;
	private readonly AssetHandle<VoxelSurfaceAssetsResource> surfaceTextures;
	private readonly GameAssetStore assetStore;

	internal FishGfxVoxelAssets(GraphicsContext graphics, GameAssetStore assetStore)
	{
		ArgumentNullException.ThrowIfNull(graphics);
		ArgumentNullException.ThrowIfNull(assetStore);
		this.assetStore = assetStore;
		ModelAssets models = LoadModels();
		(Palette, materialIds) = CreatePalette(models);
		surfaceTextures = assetStore.GetOrRegister(
			SurfaceTextureAssetId,
			() => LoadSurfaceTextures(graphics),
			TexturePath("atlas.png"),
			TexturePath("atlas_normal.png"),
			TexturePath("atlas_specular.png"),
			TexturePath("atlas_roughness.png"),
			ModelPath("barrel", "barrel_tex.png"),
			ModelPath("campfire", "campfire_tex.png"),
			ModelPath("torch", "torch_tex.png"),
			ModelPath("grass", "grass1_tex.png")
		);
	}

	internal VoxelSurfaceTextureSet SurfaceTextures => surfaceTextures.Value.Textures;

	internal VoxelPalette Palette { get; }

	internal VoxelAtlasLayout AtlasLayout =>
		new(CubeColumns, CubeRows, AtlasSize, AtlasSize);

	internal IReadOnlyDictionary<BlockType, ushort> MaterialIds => materialIds;

	internal ushort GetMaterialId(BlockType blockType)
	{
		if (blockType == BlockType.None)
			return 0;

		if (materialIds.TryGetValue(blockType, out ushort materialId))
			return materialId;

		throw new InvalidOperationException($"Block type '{blockType}' has no FishGfx material.");
	}

	internal BlockType GetBlockType(ushort materialId)
	{
		if (materialId == 0)
			return BlockType.None;

		foreach ((BlockType blockType, ushort candidate) in materialIds)
		{
			if (candidate == materialId)
				return blockType;
		}

		throw new InvalidOperationException(
			$"FishGfx material ID '{materialId}' has no authoritative block mapping.");
	}

	private static ModelAssets LoadModels()
	{
		VoxelModel barrel = LoadModel("barrel", "barrel.json", "barrel_tex.png", BarrelRegion);
		VoxelModel campfire = LoadModel(
			"campfire",
			"campfire.json",
			"campfire_tex.png",
			CampfireRegion);
		VoxelModel torch = LoadModel("torch", "torch.json", "torch_tex.png", TorchRegion);
		VoxelModel[] foliage =
		{
			LoadModel("grass", "grass1.json", "grass1_tex.png", FoliageRegion),
			LoadModel("grass", "grass2.json", "grass1_tex.png", FoliageRegion),
			LoadModel("grass", "grass3.json", "grass1_tex.png", FoliageRegion),
		};

		return new ModelAssets(barrel, campfire, torch, new VoxelModelSet(foliage));
	}

	private static VoxelModel LoadModel(
		string directory,
		string fileName,
		string textureFileName,
		VoxelTextureRegion region)
	{
		using Bitmap texture = new(ModelPath(directory, textureFileName));
		if (texture.Width != region.Width || texture.Height != region.Height)
		{
			throw new InvalidDataException(
				$"Model texture '{textureFileName}' is {texture.Width}x{texture.Height}, " +
				$"but its atlas region is {region.Width}x{region.Height}.");
		}

		Dictionary<string, VoxelTextureRegion> regions = new()
		{
			["0"] = region,
		};

		return MinecraftVoxelModelLoader.LoadFile(ModelPath(directory, fileName), regions);
	}

	private static (VoxelPalette Palette, ReadOnlyDictionary<BlockType, ushort> MaterialIds)
		CreatePalette(ModelAssets models)
	{
		VoxelPaletteBuilder builder = new();
		Dictionary<BlockType, ushort> ids = new();

		Add(BlockType.Stone, Opaque("Stone", 0));
		Add(BlockType.Dirt, Opaque("Dirt", 1));
		Add(BlockType.StoneBrick, Opaque("Stone Brick", 2));
		Add(BlockType.Sand, Opaque("Sand", 3));
		Add(BlockType.Bricks, Opaque("Bricks", 4));
		Add(BlockType.Plank, Opaque("Plank", 5));
		Add(BlockType.EndStoneBrick, Opaque("End Stone Brick", 6));
		Add(
			BlockType.Ice,
			new VoxelMaterial(
				"Ice",
				VoxelRenderMode.Transparent,
				new VoxelFaceTiles(7),
				occludesFaces: false,
				doubleSided: true,
				light: new VoxelMaterialLightSettings(1)));
		Add(BlockType.Test, Opaque("Test", 8));
		Add(
			BlockType.Leaf,
			new VoxelMaterial(
				"Leaf",
				VoxelRenderMode.Transparent,
				new VoxelFaceTiles(9),
				occludesFaces: false,
				light: new VoxelMaterialLightSettings(1),
				shadowCasterMode: VoxelShadowCasterMode.AlphaTest,
				shadowAlphaCutoff: 0.35f));
		Add(
			BlockType.Water,
			new VoxelMaterial(
				"Water",
				VoxelRenderMode.Transparent,
				new VoxelFaceTiles(10),
				occludesFaces: false,
				doubleSided: true,
				wave: new VoxelWaveSettings(0.1f, 6f, 0.2f),
				light: new VoxelMaterialLightSettings(1)));
		Add(
			BlockType.Glass,
			new VoxelMaterial(
				"Glass",
				VoxelRenderMode.Transparent,
				new VoxelFaceTiles(11),
				occludesFaces: false,
				doubleSided: true,
				light: new VoxelMaterialLightSettings(0)));
		Add(
			BlockType.Glowstone,
			Opaque(
				"Glowstone",
				12,
				new VoxelMaterialLightSettings(15, new VoxelBlockLight(15, 12, 8))));
		Add(BlockType.Test2, Opaque("Test 2", 13));
		Add(
			BlockType.Grass,
			new VoxelMaterial(
				"Grass",
				VoxelRenderMode.Opaque,
				new VoxelFaceTiles(241, 241, 240, 1, 241, 241)));
		Add(
			BlockType.Wood,
			new VoxelMaterial(
				"Wood",
				VoxelRenderMode.Opaque,
				new VoxelFaceTiles(242, 242, 243, 243, 242, 242)));
		Add(
			BlockType.CraftingTable,
			new VoxelMaterial(
				"Crafting Table",
				VoxelRenderMode.Opaque,
				new VoxelFaceTiles(245, 245, 244, 247, 246, 246)));
		Add(
			BlockType.Barrel,
			Custom("Barrel", VoxelRenderMode.Opaque, models.Barrel, true));
		Add(
			BlockType.Campfire,
			Custom(
				"Campfire",
				VoxelRenderMode.Cutout,
				models.Campfire,
				false,
				new VoxelMaterialLightSettings(0, new VoxelBlockLight(15, 7, 2))));
		Add(
			BlockType.Torch,
			Custom(
				"Torch",
				VoxelRenderMode.Cutout,
				models.Torch,
				false,
				new VoxelMaterialLightSettings(0, new VoxelBlockLight(15, 10, 5))));
		Add(
			BlockType.Foliage,
			new VoxelMaterial(
				"Foliage",
				VoxelRenderMode.Cutout,
				new VoxelFaceTiles(0),
				occludesFaces: false,
				models: models.Foliage,
				light: new VoxelMaterialLightSettings(1)));
		Add(BlockType.Gravel, Opaque("Gravel", 21));

		foreach (BlockType blockType in Enum.GetValues<BlockType>())
		{
			if (blockType != BlockType.None && !ids.ContainsKey(blockType))
				throw new InvalidOperationException($"Block type '{blockType}' has no palette entry.");
		}

		return (builder.Build(), new ReadOnlyDictionary<BlockType, ushort>(ids));

		void Add(BlockType blockType, VoxelMaterial material)
		{
			if (!ids.TryAdd(blockType, builder.Add(material)))
				throw new InvalidOperationException($"Block type '{blockType}' is mapped twice.");
		}
	}

	private static VoxelMaterial Opaque(
		string name,
		int tile,
		VoxelMaterialLightSettings? light = null)
	{
		return new VoxelMaterial(
			name,
			VoxelRenderMode.Opaque,
			new VoxelFaceTiles(tile),
			light: light);
	}

	private static VoxelMaterial Custom(
		string name,
		VoxelRenderMode mode,
		VoxelModel model,
		bool occludesFaces,
		VoxelMaterialLightSettings? light = null)
	{
		return new VoxelMaterial(
			name,
			mode,
			new VoxelFaceTiles(0),
			occludesFaces: occludesFaces,
			models: new VoxelModelSet(model),
			light: light);
	}

	private static Bitmap CreateAtlasBitmap()
	{
		Bitmap atlas = new(TexturePath("atlas.png"));
		DrawAsset(atlas, ModelPath("barrel", "barrel_tex.png"), BarrelRegion);
		DrawAsset(atlas, ModelPath("campfire", "campfire_tex.png"), CampfireRegion);
		DrawAsset(atlas, ModelPath("torch", "torch_tex.png"), TorchRegion);
		DrawAsset(atlas, ModelPath("grass", "grass1_tex.png"), FoliageRegion);
		return atlas;
	}

	internal bool RequestSurfaceTextureReload()
	{
		return assetStore.RequestReload(SurfaceTextureAssetId);
	}

	internal VoxelMaterialPreviewInfo GetPreviewInfo(BlockType blockType)
	{
		ushort materialId = GetMaterialId(blockType);
		VoxelMaterial material = Palette[materialId];
		bool isCustomModel = material.Models != null;
		return new VoxelMaterialPreviewInfo(
			blockType,
			material.Name,
			material.RenderMode,
			isCustomModel,
			!isCustomModel,
			material.Tiles
		);
	}

	private VoxelSurfaceAssetsResource LoadSurfaceTextures(
		GraphicsContext graphics
	)
	{
		using Bitmap baseColorBitmap = CreateAtlasBitmap();
		using Bitmap normalBitmap = LoadAndValidateAtlas("atlas_normal.png");
		using Bitmap specularBitmap = LoadAndValidateAtlas("atlas_specular.png");
		using Bitmap roughnessBitmap = LoadAndValidateAtlas("atlas_roughness.png");
		Texture modelAtlas = null;
		Texture baseColor = null;
		Texture normal = null;
		Texture specular = null;
		Texture roughness = null;

		try
		{
			modelAtlas = graphics.CreateTextureFromImage(
				baseColorBitmap,
				new TextureLoadOptions
				{
					Format = TextureFormat.SRGB8Alpha8,
					MipLevels = 1,
					Sampling = SurfaceSampling,
				}
			);
			IReadOnlyDictionary<int, float> alphaCutoffs = GetCubeAlphaCutoffs();
			baseColor = VoxelAtlasArrayBuilder.Create(
				graphics,
				baseColorBitmap,
				CubeColumns,
				CubeRows,
				TextureFormat.SRGB8Alpha8,
				VoxelAtlasMipKind.BaseColor,
				alphaCutoffs
			);
			normal = VoxelAtlasArrayBuilder.Create(
				graphics,
				normalBitmap,
				CubeColumns,
				CubeRows,
				TextureFormat.RGBA8Unorm,
				VoxelAtlasMipKind.Normal
			);
			specular = VoxelAtlasArrayBuilder.Create(
				graphics,
				specularBitmap,
				CubeColumns,
				CubeRows,
				TextureFormat.RGBA8Unorm,
				VoxelAtlasMipKind.Linear
			);
			roughness = VoxelAtlasArrayBuilder.Create(
				graphics,
				roughnessBitmap,
				CubeColumns,
				CubeRows,
				TextureFormat.RGBA8Unorm,
				VoxelAtlasMipKind.Linear
			);
			return new VoxelSurfaceAssetsResource(
				new VoxelSurfaceTextureSet(
					modelAtlas,
					baseColor,
					normal,
					specular,
					roughness
				)
			);
		}
		catch
		{
			roughness?.Dispose();
			specular?.Dispose();
			normal?.Dispose();
			baseColor?.Dispose();
			modelAtlas?.Dispose();
			throw;
		}
	}

	private IReadOnlyDictionary<int, float> GetCubeAlphaCutoffs()
	{
		Dictionary<int, float> cutoffs = new();

		foreach (VoxelMaterial material in Palette.Materials)
		{
			if (material == null
				|| material.Models != null
				|| material.ShadowCasterMode != VoxelShadowCasterMode.AlphaTest)
			{
				continue;
			}

			foreach (VoxelFace face in Enum.GetValues<VoxelFace>())
			{
				int tile = material.Tiles[face];

				if (!cutoffs.TryGetValue(tile, out float existing)
					|| material.ShadowAlphaCutoff < existing)
				{
					cutoffs[tile] = material.ShadowAlphaCutoff;
				}
			}
		}

		return cutoffs;
	}

	private static Bitmap LoadAndValidateAtlas(string fileName)
	{
		Bitmap bitmap = new(TexturePath(fileName));

		if (bitmap.Width == AtlasSize && bitmap.Height == AtlasSize)
		{
			return bitmap;
		}

		bitmap.Dispose();
		throw new InvalidDataException(
			$"Voxel surface atlas '{fileName}' must be {AtlasSize}x{AtlasSize}."
		);
	}

	private static void DrawAsset(
		Bitmap destination,
		string path,
		VoxelTextureRegion region)
	{
		using Bitmap source = new(path);
		for (int y = 0; y < source.Height; y++)
		{
			for (int x = 0; x < source.Width; x++)
				destination.SetPixel(region.X + x, region.Y + y, source.GetPixel(x, y));
		}

		for (int y = 0; y < source.Height; y++)
		{
			destination.SetPixel(region.X - 1, region.Y + y, source.GetPixel(0, y));
			destination.SetPixel(
				region.X + source.Width,
				region.Y + y,
				source.GetPixel(source.Width - 1, y));
		}

		for (int x = 0; x < source.Width; x++)
		{
			destination.SetPixel(region.X + x, region.Y - 1, source.GetPixel(x, 0));
			destination.SetPixel(
				region.X + x,
				region.Y + source.Height,
				source.GetPixel(x, source.Height - 1));
		}

		destination.SetPixel(region.X - 1, region.Y - 1, source.GetPixel(0, 0));
		destination.SetPixel(
			region.X + source.Width,
			region.Y - 1,
			source.GetPixel(source.Width - 1, 0));
		destination.SetPixel(
			region.X - 1,
			region.Y + source.Height,
			source.GetPixel(0, source.Height - 1));
		destination.SetPixel(
			region.X + source.Width,
			region.Y + source.Height,
			source.GetPixel(source.Width - 1, source.Height - 1));
	}

	private static string TexturePath(string fileName)
	{
		return Path.Combine(AppContext.BaseDirectory, "data", "textures", fileName);
	}

	private static string ModelPath(string directory, string fileName)
	{
		return Path.Combine(AppContext.BaseDirectory, "data", "models", directory, fileName);
	}

	private sealed record ModelAssets(
		VoxelModel Barrel,
		VoxelModel Campfire,
		VoxelModel Torch,
		VoxelModelSet Foliage);

	private sealed class VoxelSurfaceAssetsResource : IDisposable
	{
		internal VoxelSurfaceAssetsResource(VoxelSurfaceTextureSet textures)
		{
			Textures = textures ?? throw new ArgumentNullException(nameof(textures));
		}

		internal VoxelSurfaceTextureSet Textures { get; }

		public void Dispose()
		{
			Textures.Roughness.Dispose();
			Textures.Specular.Dispose();
			Textures.Normal.Dispose();
			Textures.CubeBaseColor.Dispose();
			Textures.ModelAtlas.Dispose();
		}
	}
}
#endif
