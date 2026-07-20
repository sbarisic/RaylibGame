#if WINDOWS
using System.Collections.Concurrent;
using System.Numerics;
using FishGfx.Game;
using FishGfx.Graphics;
using FishGfx.Voxels;
using FishGfx.Graphics.Shadows;
using Voxelgine.Engine;
using Voxelgine.FishGfxClient.Assets;
using Voxelgine.FishGfxClient.Entities;
using Voxelgine.Graphics;

namespace Voxelgine.FishGfxClient.Voxels;

public sealed class FishGfxVoxelScene : IDisposable
{
	public const string SurfaceTextureAssetId = FishGfxVoxelAssets.SurfaceTextureAssetId;
	private readonly ChunkMap source;
	private readonly FishGfxVoxelAssets assets;
	private readonly CampfireEmitterIndex campfires = new();
	private readonly ConcurrentQueue<BlockChange> pendingChanges = new();
	private readonly ConcurrentQueue<ChunkColumnSnapshot> pendingColumns = new();
	private readonly HashSet<ChunkCoordinate> residents = new();
	private readonly VoxelRendererOptions rendererOptions;
	private float skyLightMultiplier = 1;
	private byte minimumAmbientLight;
	private int resetPending;
	private bool disposed;

	public FishGfxVoxelScene(GraphicsContext graphics, GameAssetStore assetStore, ChunkMap source,
		int maxChunkDrawDistance = GameConfig.DefaultMaxChunkDrawDistance,
		int chunkMeshUploadBudget = GameConfig.DefaultChunkMeshUploadBudget,
		VolumetricFogQuality fogQuality = VolumetricFogQuality.Medium,
		bool synchronizeExisting = true)
	{
		ArgumentNullException.ThrowIfNull(graphics);
		this.source = source ?? throw new ArgumentNullException(nameof(source));
		assets = new FishGfxVoxelAssets(graphics, assetStore);
		World = new VoxelWorld();
		Lighting = new VoxelLighting(
			World,
			assets.Palette,
			new VoxelLightingOptions { UpdateBudget = 65_536 });
		rendererOptions = new VoxelRendererOptions
		{
			WorkerCount = Math.Max(2, Environment.ProcessorCount - 1),
			MaxRenderDistance = Math.Clamp(
				maxChunkDrawDistance,
				GameConfig.MinimumMaxChunkDrawDistance,
				GameConfig.MaximumMaxChunkDrawDistance
			),
			MeshUploadBudget = Math.Clamp(
				chunkMeshUploadBudget,
				GameConfig.MinimumChunkMeshUploadBudget,
				GameConfig.MaximumChunkMeshUploadBudget
			),
			MeshUploadTimeBudgetMilliseconds = 2,
		};
		Renderer = new VoxelRenderer(
			graphics,
			World,
			assets.Palette,
			assets.Atlas,
			assets.AtlasLayout,
			Lighting,
			rendererOptions);
		Renderer.SetSurfaceTextures(assets.SurfaceTextures);
		FogVolume = new FishGfxFogVolume(graphics, source, fogQuality);

		source.BlockChanged += QueueChange;
		source.ColumnLoaded += QueueColumn;
		source.WorldReset += QueueReset;
		if (synchronizeExisting)
			SynchronizeAll();
	}

	public VoxelWorld World { get; }

	public VoxelLighting Lighting { get; }

	public VoxelRenderer Renderer { get; }

	public FishGfxFogVolume FogVolume { get; }

	public IReadOnlyDictionary<BlockType, ushort> MaterialIds => assets.MaterialIds;

	public IReadOnlyList<Vector3> CampfirePositions => campfires.Positions;

	public IReadOnlyList<VoxelFireEmitter> FireParticleEmitters => campfires.ParticleEmitters;

	public int TorchCount => campfires.TorchCount;

	public int MaxChunkDrawDistance => (int)rendererOptions.MaxRenderDistance;

	public int ChunkMeshUploadBudget => rendererOptions.MeshUploadBudget;

	public bool GpuProfilingEnabled
	{
		get => Renderer.GpuProfilingEnabled;
		set => Renderer.GpuProfilingEnabled = value;
	}

	public VoxelRendererFrameDiagnostics FrameDiagnostics => Renderer.FrameDiagnostics;

	public VoxelRendererStatistics Statistics => Renderer.Statistics;

	public bool RequestSurfaceTextureReload()
	{
		ThrowIfDisposed();
		return assets.RequestSurfaceTextureReload();
	}

	public VoxelMaterialPreviewInfo GetMaterialPreviewInfo(BlockType blockType)
	{
		ThrowIfDisposed();
		return assets.GetPreviewInfo(blockType);
	}

	public long GeometryRevision => Renderer.GeometryRevision;

	public bool IsLightingIdle => Lighting.IsIdle;

	public bool HasValidTransparentOrdering => Renderer.HasValidTransparentOrdering;

	public VoxelPresentationState GetPresentationState(ChunkCoordinate coordinate) =>
		Renderer.GetPresentationState(coordinate);

	public void SetOptimizationSettings(int maxChunkDrawDistance, int chunkMeshUploadBudget)
	{
		ThrowIfDisposed();
		rendererOptions.MaxRenderDistance = Math.Clamp(
			maxChunkDrawDistance,
			GameConfig.MinimumMaxChunkDrawDistance,
			GameConfig.MaximumMaxChunkDrawDistance
		);
		rendererOptions.MeshUploadBudget = Math.Clamp(
			chunkMeshUploadBudget,
			GameConfig.MinimumChunkMeshUploadBudget,
			GameConfig.MaximumChunkMeshUploadBudget
		);
	}

	public void SetEnvironmentLighting(float skyMultiplier, byte ambientLight)
	{
		ThrowIfDisposed();
		if (!float.IsFinite(skyMultiplier))
		{
			throw new ArgumentOutOfRangeException(nameof(skyMultiplier));
		}

		skyLightMultiplier = Math.Clamp(skyMultiplier, 0, 1);
		minimumAmbientLight = (byte)Math.Clamp(
			(int)ambientLight,
			0,
			VoxelEnvironmentSampling.MaximumSkyLight
		);
	}

	public void ProcessPendingChanges()
	{
		ThrowIfDisposed();
		if (Interlocked.Exchange(ref resetPending, 0) != 0)
		{
			while (pendingChanges.TryDequeue(out _))
			{
			}
			while (pendingColumns.TryDequeue(out _))
			{
			}

			SynchronizeAll();
			return;
		}

		while (pendingColumns.TryDequeue(out ChunkColumnSnapshot column))
			ApplyColumn(column);

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
		FogVolume.Update(camera.Position);
	}

	public void Enqueue(RenderQueue queue, Camera camera)
	{
		Enqueue(queue, camera, shadows: null);
	}

	public void Enqueue(
		RenderQueue queue,
		Camera camera,
		DirectionalShadowFrame? shadows)
	{
		ArgumentNullException.ThrowIfNull(queue);
		ArgumentNullException.ThrowIfNull(camera);
		SynchronizeAtlas();
		Renderer.EnqueueVisible(queue, camera, shadows);
	}

	public EntityLightSample SampleEntityLight(Vector3 worldPosition)
	{
		ThrowIfDisposed();
		VoxelLight light = Lighting.GetLight(
			(int)MathF.Floor(worldPosition.X),
			(int)MathF.Floor(worldPosition.Y),
			(int)MathF.Floor(worldPosition.Z)
		);
		VoxelBlockLight block = light.Block;
		float inverseMaximum = 1f / VoxelEnvironmentSampling.MaximumSkyLight;

		return new EntityLightSample(
			new Vector3(block.Red, block.Green, block.Blue) * inverseMaximum,
			Math.Max(light.Sky * inverseMaximum, minimumAmbientLight * inverseMaximum)
		);
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
			ScaleLight(VoxelEnvironmentSampling.CombineLightLevel(
				sky,
				block.Red,
				skyLightMultiplier,
				minimumAmbientLight)),
			ScaleLight(VoxelEnvironmentSampling.CombineLightLevel(
				sky,
				block.Green,
				skyLightMultiplier,
				minimumAmbientLight)),
			ScaleLight(VoxelEnvironmentSampling.CombineLightLevel(
				sky,
				block.Blue,
				skyLightMultiplier,
				minimumAmbientLight)));
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
		source.ColumnLoaded -= QueueColumn;
		source.WorldReset -= QueueReset;
		FogVolume.Dispose();
		Renderer.Dispose();
		Lighting.Dispose();
	}

	private void SynchronizeAtlas()
	{
		VoxelSurfaceTextureSet current = assets.SurfaceTextures;
		if (!ReferenceEquals(Renderer.SurfaceTextures, current))
		{
			Renderer.SetSurfaceTextures(current);
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

	private void ApplyColumn(ChunkColumnSnapshot column)
	{
		campfires.ReplaceColumn(column);
		ChunkCoordinate[] previous = residents
			.Where(coordinate => coordinate.X == column.X && coordinate.Z == column.Z)
			.ToArray();
		foreach (ChunkCoordinate coordinate in previous)
		{
			residents.Remove(coordinate);
			Lighting.UnloadChunk(coordinate);
			World.RemoveChunk(coordinate);
		}

		int highestY = column.Chunks.Count == 0
			? int.MinValue
			: column.Chunks.Max(static snapshot => snapshot.ChunkY);
		foreach (ChunkSnapshot snapshot in column.Chunks)
		{
			ChunkCoordinate coordinate = new(snapshot.ChunkX, snapshot.ChunkY, snapshot.ChunkZ);
			VoxelCell[] cells = new VoxelCell[VoxelWorld.ChunkVolume];
			for (int index = 0; index < cells.Length; index++)
				cells[index] = new VoxelCell(assets.GetMaterialId(snapshot.Blocks[index]));

			World.SetChunk(coordinate, cells);
			residents.Add(coordinate);
			Lighting.LoadChunk(coordinate, skyExposedAbove: snapshot.ChunkY == highestY);
		}
		RefreshSkyExposure(column.X, column.Z);
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

	private void QueueColumn(ChunkColumnSnapshot column)
	{
		if (Volatile.Read(ref resetPending) == 0)
			pendingColumns.Enqueue(column);
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
