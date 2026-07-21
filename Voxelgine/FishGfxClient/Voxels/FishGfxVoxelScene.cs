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
	private readonly ConcurrentQueue<PreparedClientColumn> pendingPreparedColumns = new();
	private readonly Queue<(int X, int Z, long Revision)> completedPreparedColumns = new();
	private readonly List<ChunkCoordinate> columnRemovalScratch = new();
	private readonly HashSet<ChunkCoordinate> residents = new();
	private readonly VoxelRendererOptions rendererOptions;
	private float skyLightMultiplier = 1;
	private byte minimumAmbientLight;
	private int resetPending;
	private bool streamingBackpressured;
	private PreparedColumnApplication currentPreparedColumn;
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
			WorkerCount = Math.Min(4, Math.Max(2, Environment.ProcessorCount / 2)),
			MaximumMeshingWorkers = 4,
			MaximumReadyMeshJobs = 128,
			MaximumReadyMeshBytes = 32L * 1024 * 1024,
			ResumeReadyMeshJobs = 64,
			ResumeReadyMeshBytes = 16L * 1024 * 1024,
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
			MeshUploadTimeBudgetMilliseconds = 1,
			MeshUploadByteBudget = 1 * 1024 * 1024,
			MeshUploadSliceBytes = 256 * 1024,
			AlphaCutoff = FishGfxVoxelAssets.CutoutAlphaCutoff,
		};
		Renderer = new VoxelRenderer(
			graphics,
			World,
			assets.Palette,
			assets.SurfaceTextures,
			assets.AtlasLayout,
			Lighting,
			rendererOptions);
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
	public VoxelRendererWorkload Workload => Renderer.Workload;
	public int LightingPendingCount => Lighting.PendingCount;
	public bool IsStreamingBackpressured => streamingBackpressured;
	public event Action<int, int, long> PreparedColumnApplied;

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
			while (pendingPreparedColumns.TryDequeue(out PreparedClientColumn prepared))
				prepared.Dispose();
			completedPreparedColumns.Clear();
			currentPreparedColumn?.Dispose();
			currentPreparedColumn = null;

			SynchronizeAll();
			return;
		}

		if (currentPreparedColumn == null
			&& pendingPreparedColumns.TryDequeue(out PreparedClientColumn preparedColumn))
		{
			currentPreparedColumn = BeginPreparedColumn(preparedColumn);
		}

		if (currentPreparedColumn != null)
		{
			if (!currentPreparedColumn.IsComplete)
				ApplyNextPreparedChunk(currentPreparedColumn);
			if (currentPreparedColumn.IsComplete)
			{
				CompletePreparedColumn(currentPreparedColumn);
				currentPreparedColumn.Dispose();
				currentPreparedColumn = null;
			}
		}

		if (currentPreparedColumn == null && pendingPreparedColumns.IsEmpty)
		{
			while (pendingColumns.TryDequeue(out ChunkColumnSnapshot column))
				ApplyColumn(column);
		}

		if (currentPreparedColumn == null && pendingPreparedColumns.IsEmpty)
		{
			while (pendingChanges.TryDequeue(out BlockChange change))
				Apply(change);
			while (completedPreparedColumns.TryDequeue(out var completed))
				PreparedColumnApplied?.Invoke(completed.X, completed.Z, completed.Revision);
		}
	}

	public PreparedClientColumn PrepareStreamedColumn(ChunkColumnSnapshot column)
	{
		ThrowIfDisposed();
		return PreparedClientColumn.Prepare(column, assets.MaterialIds);
	}

	public void EnqueuePreparedColumn(PreparedClientColumn column)
	{
		ThrowIfDisposed();
		ArgumentNullException.ThrowIfNull(column);
		pendingPreparedColumns.Enqueue(column);
	}

	public int PendingPreparedColumnCount => pendingPreparedColumns.Count
		+ (currentPreparedColumn == null ? 0 : 1);

	public void Update(Camera camera)
	{
		ArgumentNullException.ThrowIfNull(camera);
		SynchronizeAtlas();
		ProcessPendingChanges();
		Lighting.Update();
		Renderer.UpdateMeshes(camera);
		UpdateStreamingBackpressure();
		FogVolume.Update(camera.Position);
	}

	private void UpdateStreamingBackpressure()
	{
		VoxelRendererWorkload workload = Renderer.Workload;
		int lightingPending = Lighting.PendingCount;
		if (streamingBackpressured)
		{
			if (!workload.IsBackpressured
				&& workload.DirtyMeshes <= 128
				&& lightingPending <= 65_536)
			{
				streamingBackpressured = false;
			}
			return;
		}

		if (workload.IsBackpressured
			|| workload.DirtyMeshes >= 256
			|| lightingPending >= 262_144)
		{
			streamingBackpressured = true;
		}
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
		while (pendingPreparedColumns.TryDequeue(out PreparedClientColumn prepared))
			prepared.Dispose();
		currentPreparedColumn?.Dispose();
		currentPreparedColumn = null;
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

	private PreparedColumnApplication BeginPreparedColumn(PreparedClientColumn column)
	{
		PreparedRenderChunk[] chunks = column.ConsumeRenderChunks();
		int columnX = column.DomainColumn.X;
		int columnZ = column.DomainColumn.Z;
		campfires.ReplaceColumn(columnX, columnZ, column.Emitters);
		columnRemovalScratch.Clear();
		foreach (ChunkCoordinate coordinate in residents)
		{
			if (coordinate.X == columnX && coordinate.Z == columnZ)
				columnRemovalScratch.Add(coordinate);
		}
		ChunkCoordinate[] previous = columnRemovalScratch.ToArray();
		long revision = column.Revision;
		VoxelColumnUpdate update = World.BeginColumnUpdate(columnX, columnZ, revision);
		column.Dispose();
		return new PreparedColumnApplication(
			columnX,
			columnZ,
			revision,
			chunks,
			previous,
			update);
	}

	private void ApplyNextPreparedChunk(PreparedColumnApplication application)
	{
		PreparedRenderChunk chunk = application.Chunks[application.NextChunk++];
		try
		{
			World.InstallPreparedChunk(
				application.Update,
				chunk.Coordinate,
				chunk.ConsumeStorage());
		}
		finally
		{
			chunk.Dispose();
		}
	}

	private void CompletePreparedColumn(PreparedColumnApplication application)
	{
		World.CompleteColumnUpdate(application.Update);
		foreach (ChunkCoordinate coordinate in application.PreviousChunks)
		{
			if (World.TryGetChunk(coordinate, out _))
				continue;
			residents.Remove(coordinate);
			Lighting.UnloadChunk(coordinate);
		}
		foreach (PreparedRenderChunk chunk in application.Chunks)
		{
			if (!World.TryGetChunk(chunk.Coordinate, out _))
				continue;
			residents.Add(chunk.Coordinate);
			Lighting.LoadChunk(chunk.Coordinate, chunk.SkyExposedAbove);
			Lighting.MarkChunkDirty(chunk.Coordinate);
		}
		RefreshSkyExposure(application.X, application.Z);
		completedPreparedColumns.Enqueue((application.X, application.Z, application.Revision));
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

	private sealed class PreparedColumnApplication : IDisposable
	{
		internal PreparedColumnApplication(
			int x,
			int z,
			long revision,
			PreparedRenderChunk[] chunks,
			ChunkCoordinate[] previousChunks,
			VoxelColumnUpdate update)
		{
			X = x;
			Z = z;
			Revision = revision;
			Chunks = chunks;
			PreviousChunks = previousChunks;
			Update = update;
		}

		internal int X { get; }
		internal int Z { get; }
		internal long Revision { get; }
		internal PreparedRenderChunk[] Chunks { get; }
		internal ChunkCoordinate[] PreviousChunks { get; }
		internal VoxelColumnUpdate Update { get; }
		internal int NextChunk { get; set; }
		internal bool IsComplete => NextChunk >= Chunks.Length;

		public void Dispose()
		{
			for (int index = NextChunk; index < Chunks.Length; index++)
				Chunks[index].Dispose();
			NextChunk = Chunks.Length;
			Update.Dispose();
		}
	}
}
#endif
