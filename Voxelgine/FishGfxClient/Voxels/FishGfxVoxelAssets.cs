#if WINDOWS
using System.Collections.ObjectModel;
using System.Numerics;
using FishGfx;
using FishGfx.Graphics;
using FishGfx.Voxels;
using Voxelgine.Engine;
using Voxelgine.FishGfxClient.Assets;
using Voxelgine.Graphics;
using Bitmap = System.Drawing.Bitmap;

namespace Voxelgine.FishGfxClient.Voxels;

internal sealed class FishGfxVoxelAssets
{
	internal const string SurfaceTextureAssetId = "voxel.surface-textures";
	internal const float CutoutAlphaCutoff = VoxelRendererOptions.DefaultAlphaCutoff;
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
	private readonly ReadOnlyDictionary<VoxelMaterialKey, ushort> materialValueIds;
	private readonly ReadOnlyDictionary<ushort, BlockValue> authoritativeValues;
	private readonly ushort[] wheatMaterialIds;
	private readonly AssetHandle<VoxelSurfaceAssetsResource> surfaceTextures;
	private readonly GameAssetStore assetStore;
	private readonly GraphicsContext graphics;

	internal FishGfxVoxelAssets(GraphicsContext graphics, GameAssetStore assetStore)
	{
		ArgumentNullException.ThrowIfNull(graphics);
		ArgumentNullException.ThrowIfNull(assetStore);
		this.assetStore = assetStore;
		this.graphics = graphics;
		ModelAssets models = LoadModels();
		(Palette, materialIds, materialValueIds, authoritativeValues, wheatMaterialIds) = CreatePalette(models);
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
	internal IReadOnlyDictionary<VoxelMaterialKey, ushort> MaterialValueIds => materialValueIds;
	internal IReadOnlyList<ushort> WheatMaterialIds => wheatMaterialIds;

	internal ushort GetMaterialId(BlockType blockType)
		=> GetMaterialId(new BlockValue(blockType));

	internal ushort GetMaterialId(BlockValue value)
	{
		if (value.Type == BlockType.None)
			return 0;

		if (materialValueIds.TryGetValue(VoxelMaterialKey.From(value), out ushort materialId))
			return materialId;

		throw new InvalidOperationException($"Block value '{value.Type}' state {value.State} has no FishGfx material.");
	}

	internal BlockType GetBlockType(ushort materialId)
		=> GetBlockValue(materialId).Type;

	internal BlockValue GetBlockValue(ushort materialId)
	{
		if (materialId == 0)
			return BlockValue.Empty;

		if (authoritativeValues.TryGetValue(materialId, out BlockValue value))
			return value;

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

	private static (
		VoxelPalette Palette,
		ReadOnlyDictionary<BlockType, ushort> MaterialIds,
		ReadOnlyDictionary<VoxelMaterialKey, ushort> MaterialValueIds,
		ReadOnlyDictionary<ushort, BlockValue> AuthoritativeValues,
		ushort[] WheatMaterialIds)
		CreatePalette(ModelAssets models)
	{
		VoxelPaletteBuilder builder = new();
		Dictionary<BlockType, ushort> ids = new();
		Dictionary<VoxelMaterialKey, ushort> valueIds = new();
		Dictionary<ushort, BlockValue> reverse = new();
		ushort[] wheatIds = new ushort[8];

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
		Add(BlockType.Leaf, CreateLeafMaterial());
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
		for (int stage = 0; stage < wheatIds.Length; stage++)
		{
			wheatIds[stage] = builder.Add(new VoxelMaterial(
				$"Wheat Stage {stage}", VoxelRenderMode.Cutout, new VoxelFaceTiles(56 + stage),
				occludesFaces: false, doubleSided: true,
				models: new VoxelModelSet(CreatePlantModel(56 + stage)),
				light: new VoxelMaterialLightSettings(1)));
			reverse.Add(wheatIds[stage], new BlockValue(BlockType.Foliage));
		}
		Add(BlockType.Gravel, Opaque("Gravel", 21));
		foreach (MachineBlockTextureDefinition definition in MachineBlockTextureCatalog.All)
		{
			BlockFaceTextureTiles faces = definition.Faces;
			Add(
				definition.Block,
				Opaque(
					definition.DisplayName,
					new VoxelFaceTiles(
						faces.PositiveX,
						faces.NegativeX,
						faces.PositiveY,
						faces.NegativeY,
						faces.PositiveZ,
						faces.NegativeZ)));
		}
		Add(BlockType.DryFarmland, Opaque("Dry Farmland", new VoxelFaceTiles(1, 1, 53, 1, 1, 1)));
		Add(BlockType.WetFarmland, Opaque("Wet Farmland", new VoxelFaceTiles(1, 1, 54, 1, 1, 1)));
		Add(BlockType.Concrete, Opaque("Concrete", 55));
		AddStairs(BlockType.StoneStairs, "Stone Stairs", 0);
		AddStairs(BlockType.WoodStairs, "Wood Stairs", 5);
		AddStairs(BlockType.ConcreteStairs, "Concrete Stairs", 55);

		foreach (BlockType blockType in Enum.GetValues<BlockType>())
		{
			if (blockType != BlockType.None && !ids.ContainsKey(blockType))
				throw new InvalidOperationException($"Block type '{blockType}' has no palette entry.");
		}

		return (
			builder.Build(),
			new ReadOnlyDictionary<BlockType, ushort>(ids),
			new ReadOnlyDictionary<VoxelMaterialKey, ushort>(valueIds),
			new ReadOnlyDictionary<ushort, BlockValue>(reverse),
			wheatIds);

		void Add(BlockType blockType, VoxelMaterial material)
		{
			ushort materialId = builder.Add(material);
			BlockValue value = new(blockType);
			if (!ids.TryAdd(blockType, materialId) ||
				!valueIds.TryAdd(VoxelMaterialKey.From(value), materialId) ||
				!reverse.TryAdd(materialId, value))
				throw new InvalidOperationException($"Block type '{blockType}' is mapped twice.");
		}

		void AddStairs(BlockType blockType, string name, int tile)
		{
			for (byte state = 0; state < 8; state++)
			{
				BlockValue value = new(blockType, state);
				ushort materialId = builder.Add(new VoxelMaterial(
					$"{name} {state}",
					VoxelRenderMode.Opaque,
					new VoxelFaceTiles(tile),
					occludesFaces: false,
					models: new VoxelModelSet(CreateStairModel(value, tile))));
				if (!valueIds.TryAdd(VoxelMaterialKey.From(value), materialId) ||
					!reverse.TryAdd(materialId, value))
					throw new InvalidOperationException($"Block value '{blockType}' state {state} is mapped twice.");
				if (state == 0 && !ids.TryAdd(blockType, materialId))
					throw new InvalidOperationException($"Block type '{blockType}' is mapped twice.");
			}
		}
	}

	private static VoxelModel CreateStairModel(BlockValue value, int tile) =>
		StairVoxelModelBuilder.Create(value, tile);

	private static VoxelModel CreatePlantModel(int tile)
	{
		List<VoxelVertex> vertices = new();
		VoxelTextureRegion region = new((tile % 16) * 32, (tile / 16) * 32, 32, 32, AtlasSize, AtlasSize);
		AddPlane(new Vector3(0,0,0), new Vector3(1,0,1), new Vector3(1,1,1), new Vector3(0,1,0));
		AddPlane(new Vector3(1,0,0), new Vector3(0,0,1), new Vector3(0,1,1), new Vector3(1,1,0));
		return new VoxelModel(vertices);

		void AddPlane(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
		{
			Vector3 normal = Vector3.Normalize(Vector3.Cross(b-a, c-a));
			Vector2[] uv={Vector2.UnitY,Vector2.One,Vector2.UnitX,Vector2.UnitY,Vector2.UnitX,Vector2.Zero};
			Vector3[] positions={a,b,c,a,c,d};
			for(int index=0;index<6;index++) vertices.Add(new VoxelVertex(positions[index], Color.White, region.Map(uv[index]), normal));
		}
	}

	internal static VoxelMaterial CreateLeafMaterial()
	{
		return new VoxelMaterial(
			"Leaf",
			VoxelRenderMode.Cutout,
			new VoxelFaceTiles(9),
			occludesFaces: false,
			light: new VoxelMaterialLightSettings(1),
			shadowAlphaCutoff: CutoutAlphaCutoff
		);
	}

	private static VoxelMaterial Opaque(
		string name,
		int tile,
		VoxelMaterialLightSettings? light = null)
	{
		return Opaque(name, new VoxelFaceTiles(tile), light);
	}

	private static VoxelMaterial Opaque(
		string name,
		VoxelFaceTiles tiles,
		VoxelMaterialLightSettings? light = null)
	{
		return new VoxelMaterial(
			name,
			VoxelRenderMode.Opaque,
			tiles,
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

	internal VoxelMaterialPaintGeometry GetPaintGeometry(BlockValue value)
	{
		ushort materialId = GetMaterialId(value);
		VoxelMaterial material = Palette[materialId];
		VoxelModel model = material.Models?.Select(0, 0, 0)
			?? StairVoxelModelBuilder.CreateCube(material.Tiles);
		return new VoxelMaterialPaintGeometry(value, materialId, material, model);
	}

	internal OwnedVoxelSurfaceTextureSet CreateEditorSurfaceTextures(
		Bitmap baseColor,
		Bitmap normal,
		Bitmap specular,
		Bitmap roughness)
	{
		ArgumentNullException.ThrowIfNull(baseColor);
		ArgumentNullException.ThrowIfNull(normal);
		ArgumentNullException.ThrowIfNull(specular);
		ArgumentNullException.ThrowIfNull(roughness);
		return CreateSurfaceTextures(baseColor, normal, specular, roughness);
	}

	private VoxelSurfaceAssetsResource LoadSurfaceTextures(
		GraphicsContext graphics
	)
	{
		using Bitmap baseColorBitmap = CreateAtlasBitmap();
		using Bitmap normalBitmap = LoadAndValidateAtlas("atlas_normal.png");
		using Bitmap specularBitmap = LoadAndValidateAtlas("atlas_specular.png");
		using Bitmap roughnessBitmap = LoadAndValidateAtlas("atlas_roughness.png");
		return new VoxelSurfaceAssetsResource(CreateSurfaceTextures(
			baseColorBitmap, normalBitmap, specularBitmap, roughnessBitmap));
	}

	private OwnedVoxelSurfaceTextureSet CreateSurfaceTextures(
		Bitmap baseColorBitmap,
		Bitmap normalBitmap,
		Bitmap specularBitmap,
		Bitmap roughnessBitmap)
	{
		Texture modelAtlas = null;
		Texture baseColor = null;
		Texture packedSurface = null;

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
			packedSurface = VoxelAtlasArrayBuilder.CreatePackedSurfaceMaps(
				graphics,
				normalBitmap,
				specularBitmap,
				roughnessBitmap,
				CubeColumns,
				CubeRows,
				out int[] layerInfo
			);
			return new OwnedVoxelSurfaceTextureSet(
				new VoxelSurfaceTextureSet(
					modelAtlas,
					baseColor,
					packedSurface,
					layerInfo
				));
		}
		catch
		{
			packedSurface?.Dispose();
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
		private OwnedVoxelSurfaceTextureSet owned;

		internal VoxelSurfaceAssetsResource(OwnedVoxelSurfaceTextureSet owned)
		{
			this.owned = owned ?? throw new ArgumentNullException(nameof(owned));
		}

		internal VoxelSurfaceTextureSet Textures => owned.Textures;

		public void Dispose()
		{
			Interlocked.Exchange(ref owned, null)?.Dispose();
		}
	}
}
#endif
