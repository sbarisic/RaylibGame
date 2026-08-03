namespace Voxelgine.WorldGeneration;

public static class WorldPlanRendering
{
	public const int HillEncodingScale = 16;
	public enum Layer { Height, Biome, Hills, TreeDensity, Features, Combined }
	private static readonly IReadOnlyDictionary<WorldBiome, uint> Palette = new Dictionary<WorldBiome, uint>
	{
		[WorldBiome.Void] = 0x00000000, [WorldBiome.Grassland] = 0x65A84FFF,
		[WorldBiome.Forest] = 0x276B3BFF, [WorldBiome.Sand] = 0xD8C27AFF,
		[WorldBiome.Rocky] = 0x777D83FF, [WorldBiome.Wetland] = 0x468C79FF,
	};

	public static IReadOnlyDictionary<WorldBiome, uint> BiomePalette => Palette;

	public static byte[] Render(WorldPlan plan, Layer layer, CancellationToken cancellationToken = default) => layer switch
	{
		Layer.Height => RenderHeight(plan), Layer.Biome => RenderBiome(plan), Layer.Hills => RenderHills(plan), Layer.TreeDensity => RenderTreeDensity(plan),
		Layer.Features => RenderFeatures(plan), Layer.Combined => RenderCombined(plan, cancellationToken),
		_ => throw new ArgumentOutOfRangeException(nameof(layer)),
	};

	public static byte[] EncodePng(WorldPlan plan, Layer layer, CancellationToken cancellationToken = default) =>
		PngRgbaCodec.Encode(plan.Width, plan.Length, Render(plan, layer, cancellationToken));

	public static byte[] EncodePreviewPng(WorldPlan plan, Layer layer, CancellationToken cancellationToken = default) =>
		PngRgbaCodec.Encode(plan.Width, plan.Length, layer == Layer.Height ? RenderHeightVisualization(plan) : Render(plan, layer, cancellationToken));

	public static byte[] RenderHeight(WorldPlan plan)
	{
		byte[] rgba = new byte[checked(plan.Width * plan.Length * 4)];
		for (int x = 0; x < plan.Width; x++) for (int z = 0; z < plan.Length; z++)
		{
			int pixel = PixelIndex(plan, x, z); byte height = plan.GetHeight(x, z); bool land = plan.IsLand(x, z);
			rgba[pixel] = rgba[pixel + 1] = rgba[pixel + 2] = height; rgba[pixel + 3] = land ? (byte)255 : (byte)0;
		}
		return rgba;
	}

	public static byte[] RenderHeightVisualization(WorldPlan plan)
	{
		byte[] rgba = new byte[checked(plan.Width * plan.Length * 4)];
		for (int x = 0; x < plan.Width; x++) for (int z = 0; z < plan.Length; z++)
		{
			int pixel = PixelIndex(plan, x, z); bool land = plan.IsLand(x, z);
			byte height = land ? (byte)Math.Clamp((int)Math.Round(plan.GetHeight(x, z) * 255d / Math.Max(1, plan.WorldHeight - 1)), 0, 255) : (byte)0;
			rgba[pixel] = rgba[pixel + 1] = rgba[pixel + 2] = height; rgba[pixel + 3] = land ? (byte)255 : (byte)0;
		}
		return rgba;
	}

	public static byte[] RenderBiome(WorldPlan plan)
	{
		byte[] rgba = new byte[checked(plan.Width * plan.Length * 4)];
		for (int x = 0; x < plan.Width; x++) for (int z = 0; z < plan.Length; z++) Write(rgba, PixelIndex(plan, x, z), Palette[plan.GetBiome(x, z)]);
		return rgba;
	}

	public static byte[] RenderTreeDensity(WorldPlan plan)
	{
		byte[] rgba = new byte[checked(plan.Width * plan.Length * 4)];
		for (int x = 0; x < plan.Width; x++) for (int z = 0; z < plan.Length; z++)
		{
			int pixel = PixelIndex(plan, x, z); byte density = plan.GetTreeDensity(x, z);
			rgba[pixel] = rgba[pixel + 1] = rgba[pixel + 2] = density; rgba[pixel + 3] = 255;
		}
		return rgba;
	}

	public static byte[] RenderHills(WorldPlan plan)
	{
		byte[] rgba = new byte[checked(plan.Width * plan.Length * 4)];
		for (int x = 0; x < plan.Width; x++) for (int z = 0; z < plan.Length; z++)
		{
			int pixel = PixelIndex(plan, x, z); byte height = (byte)(plan.GetHillHeight(x, z) * HillEncodingScale);
			rgba[pixel] = rgba[pixel + 1] = rgba[pixel + 2] = height;
			rgba[pixel + 3] = plan.IsLand(x, z) ? (byte)255 : (byte)0;
		}
		return rgba;
	}

	public static byte[] RenderFeatures(WorldPlan plan)
	{
		byte[] rgba = new byte[checked(plan.Width * plan.Length * 4)];
		foreach (PlannedVillageArea village in plan.Villages)
			foreach (PlanPoint point in village.Footprint) Write(rgba, PixelIndex(plan, point.X, point.Z), 0xD884C6D8);
		foreach (PlannedWorldRoute route in plan.Routes)
			foreach (PlanPoint3 cell in route.Cells) WriteFeatureCell(plan, rgba, cell.X, cell.Z, route.Kind == WorldFeatureKind.Road ? 0xB27B42FF : 0xB858E8FF, route.Kind == WorldFeatureKind.Road ? 1 : 0);
		foreach (PlannedVillageArea village in plan.Villages)
			foreach (PlanPoint3 cell in village.AccessRoadCells) WriteFeatureCell(plan, rgba, cell.X, cell.Z, 0xB27B42FF, 1);
		foreach (PlannedWorldSite site in plan.Sites)
		{
			uint color = site.Role switch { WorldStructureRole.Shelter => 0x4DC8FFFF, WorldStructureRole.Relay => 0xE7D84BFF, WorldStructureRole.GravityAnchor => 0xE65C5CFF, WorldStructureRole.Shaft => 0xFFFFFFE0, _ => 0x9A72D9FF };
			for (int x = Math.Max(0, site.Reservation.MinimumX); x <= Math.Min(plan.Width - 1, site.Reservation.MaximumX); x++)
				for (int z = Math.Max(0, site.Reservation.MinimumZ); z <= Math.Min(plan.Length - 1, site.Reservation.MaximumZ); z++) Write(rgba, PixelIndex(plan, x, z), color);
		}
		return rgba;
	}

	private static void WriteFeatureCell(WorldPlan plan, byte[] pixels, int x, int z, uint color, int radius)
	{
		for (int offsetX = -radius; offsetX <= radius; offsetX++)
		for (int offsetZ = -radius; offsetZ <= radius; offsetZ++)
		{
			int targetX = x + offsetX, targetZ = z + offsetZ;
			if ((uint)targetX < (uint)plan.Width && (uint)targetZ < (uint)plan.Length) Write(pixels, PixelIndex(plan, targetX, targetZ), color);
		}
	}

	public static byte[] RenderCombined(WorldPlan plan, CancellationToken cancellationToken = default)
	{
		byte[] rgba = RenderBiome(plan); HashSet<PlanPoint> water = plan.Ponds.SelectMany(p => p.Cells).Select(c => new PlanPoint(c.X, c.Z)).ToHashSet();
		for (int x = 0; x < plan.Width; x++) for (int z = 0; z < plan.Length; z++)
		{
			if (!plan.IsLand(x, z)) continue;
			int pixel = PixelIndex(plan, x, z);
			int height = plan.GetHeight(x, z);
			int west = x > 0 && plan.IsLand(x - 1, z) ? plan.GetHeight(x - 1, z) : height;
			int east = x + 1 < plan.Width && plan.IsLand(x + 1, z) ? plan.GetHeight(x + 1, z) : height;
			int north = z > 0 && plan.IsLand(x, z - 1) ? plan.GetHeight(x, z - 1) : height;
			int south = z + 1 < plan.Length && plan.IsLand(x, z + 1) ? plan.GetHeight(x, z + 1) : height;
			double relief = Math.Clamp((west - east + north - south) * 0.055, -0.22, 0.22);
			double shade = 0.78 + height / (double)Math.Max(1, plan.WorldHeight - 1) * 0.25 + relief;
			rgba[pixel] = (byte)Math.Clamp((int)Math.Round(rgba[pixel] * shade), 0, 255);
			rgba[pixel + 1] = (byte)Math.Clamp((int)Math.Round(rgba[pixel + 1] * shade), 0, 255);
			rgba[pixel + 2] = (byte)Math.Clamp((int)Math.Round(rgba[pixel + 2] * shade), 0, 255);
			if (water.Contains(new(x, z))) Write(rgba, pixel, 0x318DD1FF);
		}
		byte[] features = RenderFeatures(plan);
		for (int i = 0; i < rgba.Length; i += 4) if (features[i + 3] != 0) features.AsSpan(i, 4).CopyTo(rgba.AsSpan(i, 4));
		foreach (PlannedTree tree in WorldPlanGenerator.DeriveTrees(plan, cancellationToken)) Write(rgba, PixelIndex(plan, tree.X, tree.Z), 0x123E22FF);
		return rgba;
	}

	private static int PixelIndex(WorldPlan plan, int x, int z) => (z * plan.Width + x) * 4;
	private static void Write(byte[] pixels, int index, uint rgba)
	{
		pixels[index] = (byte)(rgba >> 24); pixels[index + 1] = (byte)(rgba >> 16); pixels[index + 2] = (byte)(rgba >> 8); pixels[index + 3] = (byte)rgba;
	}
}
