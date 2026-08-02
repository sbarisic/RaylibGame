using Voxelgine.Graphics;

namespace Voxelgine.Engine.World.Structures;

internal static class WorldStructureStamper
{
	public static void Stamp(
		ChunkMap map,
		Chunk[,,] grid,
		StructureBlueprintCatalog catalog,
		WorldFeaturePlan plan,
		int width,
		int worldHeight,
		int length,
		int chunkSize,
		CancellationToken cancellationToken)
	{
		int gridHeight = grid.GetLength(1) * chunkSize;
		foreach (PlannedSite site in plan.Sites)
		{
			cancellationToken.ThrowIfCancellationRequested();
			StructureBlueprint blueprint = catalog.Get(site.BlueprintId);
			ClearReservedWater(grid, site.Reservation, width, gridHeight, length, chunkSize);
			if (site.EmergencyFallback)
				PrepareEmergencyFoundation(grid, site, blueprint, width, gridHeight, length, chunkSize);
			for (int y = 0; y < blueprint.Size.Y; y++)
			{
				for (int z = 0; z < blueprint.Size.Z; z++)
				{
					for (int x = 0; x < blueprint.Size.X; x++)
					{
						char cell = blueprint.GetCell(x, y, z);
						if (cell == '.')
							continue;
						BlockCoordinate rotated = WorldStructurePlanner.Rotate(new BlockCoordinate(x, y, z), blueprint.Size, site.Rotation);
						BlockCoordinate world = site.Origin + rotated;
						BlockType block = cell == '_' ? BlockType.None : blueprint.Palette[cell];
						SetGridBlock(grid, world.X, world.Y, world.Z, block, width, gridHeight, length, chunkSize);
						map.TrackInfrastructureBlock(world, block);
					}
				}
			}

			foreach (StructureFogVolume fog in blueprint.FogVolumes)
			{
				BlockCoordinate minimum = site.Origin + WorldStructurePlanner.RotateBoundsMinimum(
					fog.Minimum, fog.Size, blueprint.Size, site.Rotation);
				BlockCoordinate size = WorldStructurePlanner.RotateBoundsSize(fog.Size, site.Rotation);
				map.FillFog(minimum.X, minimum.Y, minimum.Z, size.X, size.Y, size.Z, fog.Fog);
			}

			ValidateAndClearConnectorExits(grid, site, width, gridHeight, length, chunkSize);
		}

		foreach (PlannedRoute route in plan.Routes)
		{
			BlockType routeBlock = route.Kind == StructureConnectorKind.Conduit
				? BlockType.PowerConduit
				: BlockType.Gravel;
			foreach (BlockCoordinate cell in route.Cells)
			{
				cancellationToken.ThrowIfCancellationRequested();
				SetGridBlock(grid, cell.X, cell.Y, cell.Z, routeBlock, width, gridHeight, length, chunkSize);
				map.TrackInfrastructureBlock(cell, routeBlock);
				if (route.Kind == StructureConnectorKind.Road)
				{
					SetGridBlock(grid, cell.X, cell.Y + 1, cell.Z, BlockType.None, width, gridHeight, length, chunkSize);
					SetGridBlock(grid, cell.X, cell.Y + 2, cell.Z, BlockType.None, width, gridHeight, length, chunkSize);
				}
			}
		}
	}

	private static void ClearReservedWater(
		Chunk[,,] grid,
		StructureBounds reservation,
		int width,
		int height,
		int length,
		int chunkSize)
	{
		int minimumX = Math.Max(0, reservation.Minimum.X);
		int minimumY = Math.Max(0, reservation.Minimum.Y);
		int minimumZ = Math.Max(0, reservation.Minimum.Z);
		int maximumX = Math.Min(width - 1, reservation.Maximum.X);
		int maximumY = Math.Min(height - 1, reservation.Maximum.Y);
		int maximumZ = Math.Min(length - 1, reservation.Maximum.Z);
		for (int x = minimumX; x <= maximumX; x++)
		{
			for (int z = minimumZ; z <= maximumZ; z++)
			{
				for (int y = minimumY; y <= maximumY; y++)
				{
					if (GetGridBlock(grid, x, y, z, width, height, length, chunkSize) == BlockType.Water)
						SetGridBlock(grid, x, y, z, BlockType.None, width, height, length, chunkSize);
				}
			}
		}
	}

	private static void PrepareEmergencyFoundation(
		Chunk[,,] grid,
		PlannedSite site,
		StructureBlueprint blueprint,
		int width,
		int height,
		int length,
		int chunkSize)
	{
		int rotatedWidth = site.Rotation is 90 or 270 ? blueprint.Size.Z : blueprint.Size.X;
		int rotatedDepth = site.Rotation is 90 or 270 ? blueprint.Size.X : blueprint.Size.Z;
		int floorY = site.Origin.Y - 1;
		for (int x = site.Origin.X; x < site.Origin.X + rotatedWidth; x++)
		{
			for (int z = site.Origin.Z; z < site.Origin.Z + rotatedDepth; z++)
			{
				SetGridBlock(grid, x, floorY, z, BlockType.SteelFrame, width, height, length, chunkSize);
				for (int y = Math.Max(site.ModificationBounds.Minimum.Y, floorY - 12); y < floorY; y++)
				{
					if (GetGridBlock(grid, x, y, z, width, height, length, chunkSize) == BlockType.None)
						SetGridBlock(grid, x, y, z, BlockType.Stone, width, height, length, chunkSize);
				}
			}
		}
	}

	private static void ValidateAndClearConnectorExits(
		Chunk[,,] grid,
		PlannedSite site,
		int width,
		int height,
		int length,
		int chunkSize)
	{
		foreach (PlannedConnector connector in site.Connectors)
		{
			BlockCoordinate exit = connector.Position + connector.Direction;
			if (!site.Reservation.Contains(exit))
				throw new InvalidDataException($"Connector {site.Id}/{connector.Id} exits its reservation.");
			BlockType exitBlock = GetGridBlock(grid, exit.X, exit.Y, exit.Z, width, height, length, chunkSize);
			if (connector.Kind == StructureConnectorKind.Conduit)
			{
				if (exitBlock != BlockType.None && exitBlock != BlockType.PowerConduit)
					SetGridBlock(grid, exit.X, exit.Y, exit.Z, BlockType.PowerConduit, width, height, length, chunkSize);
				continue;
			}

			SetGridBlock(grid, exit.X, exit.Y, exit.Z, BlockType.None, width, height, length, chunkSize);
			if (connector.Kind == StructureConnectorKind.Road)
				SetGridBlock(grid, exit.X, exit.Y + 1, exit.Z, BlockType.None, width, height, length, chunkSize);
		}
	}

	private static void SetGridBlock(Chunk[,,] grid, int x, int y, int z, BlockType type, int width, int height, int length, int chunkSize)
	{
		if ((uint)x >= (uint)width || (uint)y >= (uint)height || (uint)z >= (uint)length)
			return;
		grid[x / chunkSize, y / chunkSize, z / chunkSize].SetBlock(x % chunkSize, y % chunkSize, z % chunkSize, new PlacedBlock(type));
	}

	private static BlockType GetGridBlock(Chunk[,,] grid, int x, int y, int z, int width, int height, int length, int chunkSize)
	{
		if ((uint)x >= (uint)width || (uint)y >= (uint)height || (uint)z >= (uint)length)
			return BlockType.None;
		return grid[x / chunkSize, y / chunkSize, z / chunkSize].GetBlock(x % chunkSize, y % chunkSize, z % chunkSize).Type;
	}
}
