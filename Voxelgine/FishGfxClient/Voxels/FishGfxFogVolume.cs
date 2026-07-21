#if WINDOWS
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;
using FishGfx;
using FishGfx.Graphics;
using Voxelgine.Engine;
using Voxelgine.Graphics;

namespace Voxelgine.FishGfxClient.Voxels;

public readonly record struct FishGfxFogDiagnostics(
	int ActiveCells,
	Vector3 Origin,
	int PendingBuilds,
	int PendingUploads,
	long UploadedBytes,
	double RebuildMilliseconds,
	double UploadMilliseconds,
	long MainThreadAllocatedBytes,
	long WorkerAllocatedBytes,
	long Generation);

public readonly record struct FishGfxFogFrame(
	Texture Texture,
	Texture OccupancyTexture,
	Vector3 Origin,
	Vector3 Size,
	float StepLength,
	int MaximumSteps,
	FishGfxFogDiagnostics Diagnostics);

/// <summary>
/// Maintains the camera-local, double-buffered fog texture. CPU replacements are
/// produced off-thread from immutable chunk snapshots; all OpenGL work remains
/// on the frame thread.
/// </summary>
public sealed partial class FishGfxFogVolume : IDisposable
{
	public const int Width = 128;
	public const int Height = 96;
	public const int Depth = 128;
	public const int MaximumUploadBytesPerFrame = 2 * 1024 * 1024;
	private const int BytesPerCell = 4;
	private const int BrickSize = 8;
	private const int OccupancyWidth = Width / BrickSize;
	private const int OccupancyHeight = Height / BrickSize;
	private const int OccupancyDepth = Depth / BrickSize;
	private const int SliceBytes = Width * Height * BytesPerCell;
	private const int UploadSlices = MaximumUploadBytesPerFrame / SliceBytes;

	private readonly GraphicsContext graphics;
	private readonly ChunkMap source;
	private readonly object buildLock = new();
	private readonly ConcurrentQueue<FogChange> pendingChanges = new();
	private readonly HashSet<(int X, int Y, int Z)> dirtyChunks = new();
	private readonly byte[] dirtyChunkStaging = new byte[16 * 16 * 16 * BytesPerCell];
	private CancellationTokenSource cancellation = new();
	private Task<BuildResult> runningBuild;
	private BuildRequest latestRequest;
	private BuildResult completedBuild;
	private Texture activeTexture;
	private Texture uploadTexture;
	private Texture activeOccupancyTexture;
	private Texture uploadOccupancyTexture;
	private byte[] activeBytes;
	private byte[] uploadBytes;
	private byte[] activeOccupancyBytes;
	private byte[] uploadOccupancyBytes;
	private Vector3 activeOrigin;
	private Vector3 uploadOrigin;
	private int uploadDepth;
	private int activeCells;
	private int uploadActiveCells;
	private long requestedGeneration;
	private long activeGeneration;
	private Vector3? requestedOrigin;
	private int sourceInvalidated = 1;
	private long uploadedBytes;
	private double rebuildMilliseconds;
	private double uploadMilliseconds;
	private long mainThreadAllocatedBytes;
	private long workerAllocatedBytes;
	private VolumetricFogQuality quality;
	private bool sourceKnownEmpty;
	private bool disposed;

	public FishGfxFogVolume(
		GraphicsContext graphics,
		ChunkMap source,
		VolumetricFogQuality quality)
	{
		this.graphics = graphics ?? throw new ArgumentNullException(nameof(graphics));
		this.source = source ?? throw new ArgumentNullException(nameof(source));
		this.quality = NormalizeQuality(quality);
		source.FogChanged += QueueChange;
		source.ColumnCommitted += QueueColumn;
		source.WorldReset += QueueReset;
	}

	public VolumetricFogQuality Quality
	{
		get => quality;
		set
		{
			ThrowIfDisposed();
			VolumetricFogQuality normalized = NormalizeQuality(value);
			if (quality == normalized)
			{
				return;
			}

			quality = normalized;
			if (quality == VolumetricFogQuality.Off)
			{
				CancelBuilds();
				DisposeTextures();
				activeBytes = null;
				activeCells = 0;
			}
			else
			{
				activeGeneration = 0;
				Interlocked.Exchange(ref sourceInvalidated, 1);
			}
		}
	}

	public FishGfxFogDiagnostics Diagnostics => new(
		activeCells,
		activeOrigin,
		runningBuild is null ? 0 : 1,
		uploadBytes is null ? 0 : 1,
		uploadedBytes,
		rebuildMilliseconds,
		uploadMilliseconds,
		mainThreadAllocatedBytes,
		workerAllocatedBytes,
		activeGeneration);

	public FishGfxFogFrame? CurrentFrame
	{
		get
		{
			if (quality == VolumetricFogQuality.Off
				|| activeTexture is null
				|| activeCells == 0)
			{
				return null;
			}

			(float stepLength, int maximumSteps) = QualitySettings(quality);
			return new FishGfxFogFrame(
				activeTexture,
				activeOccupancyTexture,
				activeOrigin,
				new Vector3(Width, Height, Depth),
				stepLength,
				maximumSteps,
				Diagnostics);
		}
	}

	public void Update(Vector3 cameraPosition)
	{
		ThrowIfDisposed();
		long allocationStart = GC.GetAllocatedBytesForCurrentThread();
		if (quality == VolumetricFogQuality.Off)
		{
			mainThreadAllocatedBytes = GC.GetAllocatedBytesForCurrentThread()
				- allocationStart;
			return;
		}

		Vector3 desiredOrigin = CalculateOrigin(cameraPosition);
		if (source.NonEmptyFogVoxelCount == 0)
		{
			PublishEmpty(desiredOrigin);
			mainThreadAllocatedBytes = GC.GetAllocatedBytesForCurrentThread()
				- allocationStart;
			return;
		}

		sourceKnownEmpty = false;
		bool force = Interlocked.Exchange(ref sourceInvalidated, 0) != 0;
		if (force || requestedOrigin != desiredOrigin)
		{
			RequestBuild(desiredOrigin, force);
		}

		ConsumeBuild();
		UploadReplacement();
		ApplyLiveChanges();
		mainThreadAllocatedBytes = GC.GetAllocatedBytesForCurrentThread()
			- allocationStart;
	}

	public void Dispose()
	{
		if (disposed)
		{
			return;
		}

		disposed = true;
		source.FogChanged -= QueueChange;
		source.ColumnCommitted -= QueueColumn;
		source.WorldReset -= QueueReset;
		CancelBuilds();
		DisposeTextures();
		cancellation.Dispose();
	}

	private void UploadReplacement()
	{
		if (uploadBytes is null)
		{
			return;
		}

		if (uploadActiveCells == 0)
		{
			activeBytes = uploadBytes;
			activeOccupancyBytes = uploadOccupancyBytes;
			activeOrigin = uploadOrigin;
			activeCells = 0;
			uploadBytes = null;
			uploadOccupancyBytes = null;
			activeTexture?.Dispose();
			activeTexture = null;
			activeOccupancyTexture?.Dispose();
			activeOccupancyTexture = null;
			return;
		}

		int slices = Math.Min(UploadSlices, Depth - uploadDepth);
		int byteOffset = uploadDepth * SliceBytes;
		int byteCount = slices * SliceBytes;
		long started = Stopwatch.GetTimestamp();
		uploadTexture.Write(
			uploadBytes.AsSpan(byteOffset, byteCount),
			TextureDataFormat.RGBA8Unorm,
			new TextureRegion3D(0, 0, uploadDepth, Width, Height, slices)
		);
		uploadMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
		uploadedBytes += byteCount;
		uploadDepth += slices;
		if (uploadDepth < Depth)
		{
			return;
		}

		uploadOccupancyTexture.Write(
			uploadOccupancyBytes,
			TextureDataFormat.R8Unorm,
			new TextureRegion3D(
				0, 0, 0, OccupancyWidth, OccupancyHeight, OccupancyDepth));
		uploadedBytes += uploadOccupancyBytes.Length;

		(activeTexture, uploadTexture) = (uploadTexture, activeTexture);
		(activeOccupancyTexture, uploadOccupancyTexture) =
			(uploadOccupancyTexture, activeOccupancyTexture);
		activeBytes = uploadBytes;
		activeOccupancyBytes = uploadOccupancyBytes;
		activeOrigin = uploadOrigin;
		activeCells = uploadActiveCells;
		uploadBytes = null;
		uploadOccupancyBytes = null;
		uploadDepth = 0;
	}

	private void ApplyLiveChanges()
	{
		if (uploadBytes is not null)
		{
			return;
		}

		if (activeTexture is null || activeBytes is null)
		{
			while (pendingChanges.TryDequeue(out _))
			{
			}
			return;
		}

		if (pendingChanges.IsEmpty)
		{
			return;
		}

		dirtyChunks.Clear();
		while (pendingChanges.TryDequeue(out FogChange change))
		{
			int localX = change.X - (int)activeOrigin.X;
			int localY = change.Y - (int)activeOrigin.Y;
			int localZ = change.Z - (int)activeOrigin.Z;
			if ((uint)localX >= Width || (uint)localY >= Height || (uint)localZ >= Depth)
			{
				continue;
			}

			int index = ((localZ * Height + localY) * Width + localX) * BytesPerCell;
			bool wasEmpty = activeBytes[index + 3] == 0;
			bool isEmpty = change.NewValue.Density == 0;
			WriteLinearPremultipliedFog(activeBytes, index, change.NewValue);
			if (wasEmpty != isEmpty)
			{
				activeCells += isEmpty ? -1 : 1;
			}
			dirtyChunks.Add((localX >> 4, localY >> 4, localZ >> 4));
		}

		foreach ((int chunkX, int chunkY, int chunkZ) in dirtyChunks)
		{
			int destination = 0;
			for (int z = 0; z < 16; z++)
			for (int y = 0; y < 16; y++)
			{
				int sourceOffset = ((((chunkZ << 4) + z) * Height
					+ (chunkY << 4) + y) * Width + (chunkX << 4)) * BytesPerCell;
				activeBytes.AsSpan(sourceOffset, 16 * BytesPerCell)
					.CopyTo(dirtyChunkStaging.AsSpan(destination));
				destination += 16 * BytesPerCell;
			}

			activeTexture.Write(
				dirtyChunkStaging,
				TextureDataFormat.RGBA8Unorm,
				new TextureRegion3D(chunkX << 4, chunkY << 4, chunkZ << 4, 16, 16, 16)
			);
			uploadedBytes += dirtyChunkStaging.Length;
			UpdateOccupancyChunk(chunkX, chunkY, chunkZ);
		}

		if (activeCells == 0)
		{
			activeTexture.Dispose();
			activeTexture = null;
			activeOccupancyTexture.Dispose();
			activeOccupancyTexture = null;
		}
	}

	private void UpdateOccupancyChunk(int chunkX, int chunkY, int chunkZ)
	{
		Span<byte> staging = stackalloc byte[8];
		int write = 0;
		for (int brickZ = 0; brickZ < 2; brickZ++)
		for (int brickY = 0; brickY < 2; brickY++)
		for (int brickX = 0; brickX < 2; brickX++)
		{
			int occupancyX = chunkX * 2 + brickX;
			int occupancyY = chunkY * 2 + brickY;
			int occupancyZ = chunkZ * 2 + brickZ;
			byte occupied = IsBrickOccupied(activeBytes, occupancyX, occupancyY, occupancyZ)
				? byte.MaxValue
				: (byte)0;
			activeOccupancyBytes[(occupancyZ * OccupancyHeight + occupancyY)
				* OccupancyWidth + occupancyX] = occupied;
			staging[write++] = occupied;
		}

		// The staging order above is X-major within Y/Z, matching the 3D upload.
		activeOccupancyTexture.Write(
			staging,
			TextureDataFormat.R8Unorm,
			new TextureRegion3D(chunkX * 2, chunkY * 2, chunkZ * 2, 2, 2, 2));
		uploadedBytes += staging.Length;
	}

	private Texture CreateTexture()
	{
		return graphics.CreateTexture(new TextureDescriptor(
			Width,
			Height,
			TextureFormat.RGBA8Unorm,
			TextureUsageFlags.Sampled | TextureUsageFlags.TransferDestination,
			TextureDimension.Texture3D,
			sampling: new TextureSamplingState(
				TextureFilter.Linear,
				TextureFilter.Linear,
				TextureWrap.ClampToEdge,
				TextureWrap.ClampToEdge,
				TextureWrap.ClampToEdge
			),
			depth: Depth
		));
	}

	private Texture CreateOccupancyTexture()
	{
		return graphics.CreateTexture(new TextureDescriptor(
			OccupancyWidth,
			OccupancyHeight,
			TextureFormat.R8Unorm,
			TextureUsageFlags.Sampled | TextureUsageFlags.TransferDestination,
			TextureDimension.Texture3D,
			sampling: new TextureSamplingState(
				TextureFilter.Nearest,
				TextureFilter.Nearest,
				TextureWrap.ClampToEdge,
				TextureWrap.ClampToEdge,
				TextureWrap.ClampToEdge),
			depth: OccupancyDepth));
	}

	private static FogChunkBounds CalculateChunkBounds(Vector3 origin)
	{
		int minimumX = (int)MathF.Floor(origin.X / Chunk.ChunkSize);
		int minimumY = (int)MathF.Floor(origin.Y / Chunk.ChunkSize);
		int minimumZ = (int)MathF.Floor(origin.Z / Chunk.ChunkSize);
		return new FogChunkBounds(
			minimumX,
			minimumY,
			minimumZ,
			minimumX + Width / Chunk.ChunkSize,
			minimumY + Height / Chunk.ChunkSize,
			minimumZ + Depth / Chunk.ChunkSize
		);
	}

	private static Vector3 CalculateOrigin(Vector3 camera)
	{
		int chunkX = (int)MathF.Floor(camera.X / Chunk.ChunkSize);
		int chunkY = (int)MathF.Floor(camera.Y / Chunk.ChunkSize);
		int chunkZ = (int)MathF.Floor(camera.Z / Chunk.ChunkSize);
		return new Vector3(
			(chunkX - Width / Chunk.ChunkSize / 2) * Chunk.ChunkSize,
			(chunkY - Height / Chunk.ChunkSize / 2) * Chunk.ChunkSize,
			(chunkZ - Depth / Chunk.ChunkSize / 2) * Chunk.ChunkSize
		);
	}

	private static (float StepLength, int MaximumSteps) QualitySettings(
		VolumetricFogQuality value) => value switch
	{
		VolumetricFogQuality.Low => (2, 64),
		VolumetricFogQuality.High => (0.5f, 256),
		_ => (1, 128),
	};

	private static VolumetricFogQuality NormalizeQuality(VolumetricFogQuality value)
	{
		return Enum.IsDefined(value) ? value : VolumetricFogQuality.Medium;
	}

	private void QueueChange(FogChange change)
	{
		pendingChanges.Enqueue(change);
		if (activeTexture is null)
		{
			Interlocked.Exchange(ref sourceInvalidated, 1);
		}
	}

	private void QueueColumn(ChunkColumnCoordinate column)
	{
		Vector3 origin = requestedOrigin ?? activeOrigin;
		int minimumX = column.X * Chunk.ChunkSize;
		int minimumZ = column.Z * Chunk.ChunkSize;
		if (minimumX < origin.X + Width
			&& minimumX + Chunk.ChunkSize > origin.X
			&& minimumZ < origin.Z + Depth
			&& minimumZ + Chunk.ChunkSize > origin.Z)
		{
			Interlocked.Exchange(ref sourceInvalidated, 1);
		}
	}

	private void QueueReset() => Interlocked.Exchange(ref sourceInvalidated, 1);

	private void DisposeTextures()
	{
		activeTexture?.Dispose();
		uploadTexture?.Dispose();
		activeOccupancyTexture?.Dispose();
		uploadOccupancyTexture?.Dispose();
		activeTexture = null;
		uploadTexture = null;
		activeOccupancyTexture = null;
		uploadOccupancyTexture = null;
		uploadBytes = null;
		uploadOccupancyBytes = null;
	}

	private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);

}
#endif
