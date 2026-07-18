#if WINDOWS
using System.Collections.Concurrent;
using System.Numerics;
using FishGfx.Game;
using FishGfx.Graphics;
using FishGfx.Voxels;
using Voxelgine.Engine;
using Voxelgine.FishGfxClient.Assets;
using Voxelgine.Graphics;

namespace Voxelgine.FishGfxClient.Voxels;

public sealed class FishGfxVoxelScene : IDisposable
{
	private readonly ChunkMap source;
	private readonly FishGfxVoxelAssets assets;
	private readonly CampfireEmitterIndex campfires = new();
	private readonly ConcurrentQueue<BlockChange> pendingChanges = new();
	private readonly HashSet<ChunkCoordinate> residents = new();
	private int resetPending;
	private bool disposed;

	public FishGfxVoxelScene(
		GraphicsContext graphics,
		GameAssetStore assetStore,
		ChunkMap source)
	{
		ArgumentNullException.ThrowIfNull(graphics);
		this.source = source ?? throw new ArgumentNullException(nameof(source));
		assets = new FishGfxVoxelAssets(graphics, assetStore);
		World = new VoxelWorld();
		Lighting = new VoxelLighting(
			World,
			assets.Palette,
			new VoxelLightingOptions { UpdateBudget = 65_536 });
		Renderer = new VoxelRenderer(
			graphics,
			World,
			assets.Palette,
			assets.Atlas,
			assets.AtlasLayout,
			Lighting,
			new VoxelRendererOptions
			{
				WorkerCount = Math.Max(2, Environment.ProcessorCount - 1),
				MaxRenderDistance = 108,
				MeshUploadBudget = 24,
				MeshUploadTimeBudgetMilliseconds = 2,
			});

		source.BlockChanged += QueueChange;
		source.WorldReset += QueueReset;
		SynchronizeAll();
	}

	public VoxelWorld World { get; }

	public VoxelLighting Lighting { get; }

	public VoxelRenderer Renderer { get; }

	public IReadOnlyDictionary<BlockType, ushort> MaterialIds => assets.MaterialIds;

	public IReadOnlyList<Vector3> CampfirePositions => campfires.Positions;

	public void ProcessPendingChanges()
	{
		ThrowIfDisposed();
		if (Interlocked.Exchange(ref resetPending, 0) != 0)
		{
			while (pendingChanges.TryDequeue(out _))
			{
			}

			SynchronizeAll();
			return;
		}

		while (pendingChanges.TryDequeue(out BlockChange change))
			Apply(change);
	}

	public void Update(Camera camera)
	{
		ArgumentNullException.ThrowIfNull(camera);
		SynchronizeAtlas();
		ProcessPendingChanges();
		Lighting.Update();
		Renderer.UpdateMeshes(camera);
	}

	public void Enqueue(RenderQueue queue, Camera camera)
	{
		ArgumentNullException.ThrowIfNull(queue);
		ArgumentNullException.ThrowIfNull(camera);
		SynchronizeAtlas();
		Renderer.EnqueueVisible(queue, camera);
	}

	public Rgba32 SampleLight(Vector3 worldPosition)
	{
		ThrowIfDisposed();
		VoxelLight light = Lighting.GetLight(
			(int)MathF.Floor(worldPosition.X),
			(int)MathF.Floor(worldPosition.Y),
			(int)MathF.Floor(worldPosition.Z));
		byte sky = light.Sky;
		VoxelBlockLight block = light.Block;

		return new Rgba32(
			ScaleLight(Math.Max(sky, block.Red)),
			ScaleLight(Math.Max(sky, block.Green)),
			ScaleLight(Math.Max(sky, block.Blue)));
	}

	public VoxelEnvironmentSample SampleEnvironment(Vector3 worldPosition)
	{
		ThrowIfDisposed();
		int x = (int)MathF.Floor(worldPosition.X);
		int y = (int)MathF.Floor(worldPosition.Y);
		int z = (int)MathF.Floor(worldPosition.Z);
		Span<byte> probes = stackalloc byte[6]
		{
			Lighting.GetLight(x, y, z).Sky,
			Lighting.GetLight(x, y + 1, z).Sky,
			Lighting.GetLight(x - 1, y, z).Sky,
			Lighting.GetLight(x + 1, y, z).Sky,
			Lighting.GetLight(x, y, z - 1).Sky,
			Lighting.GetLight(x, y, z + 1).Sky,
		};
		BlockType material = GetBlockType(x, y, z);

		return new VoxelEnvironmentSample(
			VoxelEnvironmentSampling.CalculateOutdoorExposure(probes),
			VoxelEnvironmentSampling.NormalizeSkyLight(probes[0]),
			material == BlockType.Water,
			material);
	}

	public BlockType GetBlockType(Vector3 worldPosition)
	{
		return GetBlockType(
			(int)MathF.Floor(worldPosition.X),
			(int)MathF.Floor(worldPosition.Y),
			(int)MathF.Floor(worldPosition.Z));
	}

	public bool IsSolid(Vector3 worldPosition)
	{
		return BlockInfo.IsSolid(GetBlockType(worldPosition));
	}

	public bool IsInsideMaterial(Vector3 worldPosition, BlockType blockType)
	{
		ThrowIfDisposed();
		ushort materialId = assets.GetMaterialId(blockType);
		return World.GetVoxel(
			(int)MathF.Floor(worldPosition.X),
			(int)MathF.Floor(worldPosition.Y),
			(int)MathF.Floor(worldPosition.Z)).MaterialId == materialId;
	}

	public void Dispose()
	{
		if (disposed)
			return;

		disposed = true;
		source.BlockChanged -= QueueChange;
		source.WorldReset -= QueueReset;
		Renderer.Dispose();
		Lighting.Dispose();
	}

	private void SynchronizeAtlas()
	{
		Texture current = assets.Atlas;
		if (!ReferenceEquals(Renderer.AtlasTexture, current))
		{
			Renderer.SetAtlasTexture(current);
		}
	}

	private void SynchronizeAll()
	{
		foreach (ChunkCoordinate coordinate in residents)
			Lighting.UnloadChunk(coordinate);

		residents.Clear();
		foreach (VoxelChunk chunk in World.LoadedChunks)
			World.RemoveChunk(chunk.Coordinate);

		IReadOnlyList<ChunkSnapshot> snapshots = source.CaptureChunks();
		campfires.Reset(snapshots);
		Dictionary<(int X, int Z), int> highestChunks = new();
		foreach (ChunkSnapshot snapshot in snapshots)
		{
			(int X, int Z) column = (snapshot.ChunkX, snapshot.ChunkZ);
			if (!highestChunks.TryGetValue(column, out int highest) || snapshot.ChunkY > highest)
				highestChunks[column] = snapshot.ChunkY;
		}

		foreach (ChunkSnapshot snapshot in snapshots)
		{
			ChunkCoordinate coordinate = new(snapshot.ChunkX, snapshot.ChunkY, snapshot.ChunkZ);
			VoxelCell[] cells = new VoxelCell[VoxelWorld.ChunkVolume];
			for (int index = 0; index < cells.Length; index++)
				cells[index] = new VoxelCell(assets.GetMaterialId(snapshot.Blocks[index]));

			World.SetChunk(coordinate, cells);
			residents.Add(coordinate);
			Lighting.LoadChunk(
				coordinate,
				skyExposedAbove: snapshot.ChunkY == highestChunks[(snapshot.ChunkX, snapshot.ChunkZ)]);
		}

		Lighting.RequestFullRebuild();
	}

	private void Apply(BlockChange change)
	{
		campfires.Apply(change);
		ushort materialId = assets.GetMaterialId(change.NewType);
		World.SetVoxel(change.X, change.Y, change.Z, new VoxelCell(materialId));
		ChunkCoordinate coordinate = ChunkCoordinate.FromWorld(
			change.X,
			change.Y,
			change.Z,
			out _,
			out _,
			out _);

		if (residents.Add(coordinate))
		{
			Lighting.LoadChunk(coordinate);
			RefreshSkyExposure(coordinate.X, coordinate.Z);
		}
	}

	private BlockType GetBlockType(int x, int y, int z)
	{
		return assets.GetBlockType(World.GetVoxel(x, y, z).MaterialId);
	}

	private void RefreshSkyExposure(int chunkX, int chunkZ)
	{
		int highestY = int.MinValue;
		foreach (ChunkCoordinate coordinate in residents)
		{
			if (coordinate.X == chunkX && coordinate.Z == chunkZ)
				highestY = Math.Max(highestY, coordinate.Y);
		}

		foreach (ChunkCoordinate coordinate in residents)
		{
			if (coordinate.X == chunkX && coordinate.Z == chunkZ)
				Lighting.SetSkyExposedAbove(coordinate, coordinate.Y == highestY);
		}
	}

	private void QueueChange(BlockChange change)
	{
		if (Volatile.Read(ref resetPending) == 0)
			pendingChanges.Enqueue(change);
	}

	private void QueueReset()
	{
		Interlocked.Exchange(ref resetPending, 1);
	}

	private static byte ScaleLight(int light)
	{
		return (byte)Math.Clamp(light * 17, 0, byte.MaxValue);
	}

	private void ThrowIfDisposed()
	{
		ObjectDisposedException.ThrowIf(disposed, this);
	}
}
#endif
