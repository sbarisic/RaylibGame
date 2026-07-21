#if WINDOWS
using System.Diagnostics;
using System.Numerics;
using FishGfx;
using Voxelgine.Engine;
using Voxelgine.Graphics;

namespace Voxelgine.FishGfxClient.Voxels;

public sealed partial class FishGfxFogVolume
{
	private void RequestBuild(Vector3 origin, bool force)
	{
		lock (buildLock)
		{
			if (!force && requestedOrigin == origin)
			{
				return;
			}

			requestedOrigin = origin;
			long generation = ++requestedGeneration;
			List<FogChunkSnapshotLease> snapshots = new();
			try
			{
				source.CaptureFogChunks(CalculateChunkBounds(origin), snapshots);
			}
			catch
			{
				DisposeSnapshots(snapshots);
				throw;
			}

			BuildRequest request = new(origin, snapshots.ToArray(), generation);
			if (runningBuild is null)
			{
				StartBuild(request);
			}
			else
			{
				latestRequest?.Dispose();
				latestRequest = request;
			}
		}
	}

	private void StartBuild(BuildRequest request)
	{
		CancellationToken token = cancellation.Token;
		// Do not pass the token to Task.Run itself: Build owns the request leases
		// and must run its finally block even when cancellation races scheduling.
		runningBuild = Task.Run(() => Build(request, token));
	}

	private void ConsumeBuild()
	{
		lock (buildLock)
		{
			if (runningBuild is null || !runningBuild.IsCompleted)
			{
				return;
			}
			if (runningBuild.IsFaulted)
			{
				Exception failure = runningBuild.Exception?.GetBaseException()
					?? new InvalidOperationException("The fog-volume worker failed.");
				runningBuild.Dispose();
				runningBuild = null;
				throw new InvalidOperationException(
					"The fog-volume worker failed.",
					failure
				);
			}

			if (runningBuild.IsCompletedSuccessfully)
			{
				BuildResult result = runningBuild.Result;
				if (result.Generation == requestedGeneration)
				{
					completedBuild = result;
				}
			}

			runningBuild.Dispose();
			runningBuild = null;
			if (latestRequest is not null)
			{
				BuildRequest next = latestRequest;
				latestRequest = null;
				StartBuild(next);
			}
		}

		if (completedBuild.Bytes is null || uploadBytes is not null)
		{
			return;
		}

		uploadBytes = completedBuild.Bytes;
		uploadOccupancyBytes = completedBuild.OccupancyBytes;
		uploadOrigin = completedBuild.Origin;
		uploadActiveCells = completedBuild.ActiveCells;
		rebuildMilliseconds = completedBuild.Milliseconds;
		workerAllocatedBytes = completedBuild.AllocatedBytes;
		activeGeneration = completedBuild.Generation;
		completedBuild = default;
		uploadDepth = 0;
		if (uploadActiveCells == 0)
		{
			uploadTexture?.Dispose();
			uploadTexture = null;
		}
		else
		{
			uploadTexture ??= CreateTexture();
			uploadOccupancyTexture ??= CreateOccupancyTexture();
		}
	}

	private static BuildResult Build(BuildRequest request, CancellationToken token)
	{
		try
		{
			long started = Stopwatch.GetTimestamp();
			long allocationStart = GC.GetAllocatedBytesForCurrentThread();
			// The authoritative layer stores authored premultiplied sRGB bytes. The
			// sampled GPU volume stores premultiplied linear bytes so trilinear
			// filtering and scene compositing remain colorimetrically correct.
			byte[] bytes = new byte[Width * Height * Depth * BytesPerCell];
			int active = 0;
			foreach (FogChunkSnapshotLease snapshot in request.Snapshots)
			{
				token.ThrowIfCancellationRequested();
				int baseX = snapshot.ChunkX * Chunk.ChunkSize - (int)request.Origin.X;
				int baseY = snapshot.ChunkY * Chunk.ChunkSize - (int)request.Origin.Y;
				int baseZ = snapshot.ChunkZ * Chunk.ChunkSize - (int)request.Origin.Z;
				ReadOnlySpan<FogVoxel> fog = snapshot.Fog.Span;
				for (int z = 0; z < Chunk.ChunkSize; z++)
				for (int y = 0; y < Chunk.ChunkSize; y++)
				for (int x = 0; x < Chunk.ChunkSize; x++)
				{
					int volumeX = baseX + x;
					int volumeY = baseY + y;
					int volumeZ = baseZ + z;
					if ((uint)volumeX >= Width || (uint)volumeY >= Height || (uint)volumeZ >= Depth)
					{
						continue;
					}

					FogVoxel value = fog[x + Chunk.ChunkSize * (y + Chunk.ChunkSize * z)];
					if (value.Density == 0)
					{
						continue;
					}

					int output = ((volumeZ * Height + volumeY) * Width + volumeX) * BytesPerCell;
					WriteLinearPremultipliedFog(bytes, output, value);
					active++;
				}
			}

			byte[] occupancy = BuildOccupancy(bytes);
			return new BuildResult(
				request.Origin,
				bytes,
				occupancy,
				active,
				request.Generation,
				Stopwatch.GetElapsedTime(started).TotalMilliseconds,
				GC.GetAllocatedBytesForCurrentThread() - allocationStart
			);
		}
		finally
		{
			request.Dispose();
		}
	}

	internal static byte[] BuildOccupancy(ReadOnlySpan<byte> fogBytes)
	{
		if (fogBytes.Length != Width * Height * Depth * BytesPerCell)
		{
			throw new ArgumentException("Fog volume byte count is invalid.", nameof(fogBytes));
		}

		byte[] occupancy = new byte[OccupancyWidth * OccupancyHeight * OccupancyDepth];
		for (int z = 0; z < OccupancyDepth; z++)
		for (int y = 0; y < OccupancyHeight; y++)
		for (int x = 0; x < OccupancyWidth; x++)
		{
			if (IsBrickOccupied(fogBytes, x, y, z))
			{
				occupancy[(z * OccupancyHeight + y) * OccupancyWidth + x] = byte.MaxValue;
			}
		}
		return occupancy;
	}

	private static bool IsBrickOccupied(
		ReadOnlySpan<byte> fogBytes,
		int brickX,
		int brickY,
		int brickZ)
	{
		int startX = brickX * BrickSize;
		int startY = brickY * BrickSize;
		int startZ = brickZ * BrickSize;
		for (int z = 0; z < BrickSize; z++)
		for (int y = 0; y < BrickSize; y++)
		for (int x = 0; x < BrickSize; x++)
		{
			int offset = ((((startZ + z) * Height + startY + y) * Width
				+ startX + x) * BytesPerCell) + 3;
			if (fogBytes[offset] != 0)
			{
				return true;
			}
		}
		return false;
	}

	internal static void WriteLinearPremultipliedFog(
		Span<byte> destination,
		int offset,
		FogVoxel value)
	{
		if (offset < 0 || offset > destination.Length - BytesPerCell)
		{
			throw new ArgumentOutOfRangeException(nameof(offset));
		}

		destination[offset + 3] = value.Density;
		if (value.Density == 0)
		{
			destination[offset] = 0;
			destination[offset + 1] = 0;
			destination[offset + 2] = 0;
			return;
		}

		float density = value.Density / (float)byte.MaxValue;
		destination[offset] = ToByte(
			ColorSpace.SrgbToLinear(value.R / (float)value.Density) * density
		);
		destination[offset + 1] = ToByte(
			ColorSpace.SrgbToLinear(value.G / (float)value.Density) * density
		);
		destination[offset + 2] = ToByte(
			ColorSpace.SrgbToLinear(value.B / (float)value.Density) * density
		);
	}

	private static byte ToByte(float value)
	{
		return (byte)Math.Clamp(MathF.Round(value * byte.MaxValue), 0, byte.MaxValue);
	}

	private void CancelBuilds()
	{
		cancellation.Cancel();
		try
		{
			runningBuild?.GetAwaiter().GetResult();
		}
		catch (OperationCanceledException)
		{
		}
		runningBuild?.Dispose();
		runningBuild = null;
		latestRequest?.Dispose();
		latestRequest = null;
		completedBuild = default;
		cancellation.Dispose();
		cancellation = new CancellationTokenSource();
	}

	private void PublishEmpty(Vector3 origin)
	{
		if (!sourceKnownEmpty
			|| runningBuild is not null
			|| latestRequest is not null
			|| uploadBytes is not null
			|| activeTexture is not null)
		{
			CancelBuilds();
			DisposeTextures();
			activeBytes = null;
			activeCells = 0;
			requestedOrigin = origin;
			activeOrigin = origin;
			activeGeneration = ++requestedGeneration;
			while (pendingChanges.TryDequeue(out _))
			{
			}
		}

		sourceKnownEmpty = true;
		Interlocked.Exchange(ref sourceInvalidated, 0);
	}

	private static void DisposeSnapshots(IEnumerable<FogChunkSnapshotLease> snapshots)
	{
		foreach (FogChunkSnapshotLease snapshot in snapshots)
		{
			snapshot.Dispose();
		}
	}

	private sealed class BuildRequest : IDisposable
	{
		private FogChunkSnapshotLease[] snapshots;

		public BuildRequest(
			Vector3 origin,
			FogChunkSnapshotLease[] snapshots,
			long generation)
		{
			Origin = origin;
			this.snapshots = snapshots ?? throw new ArgumentNullException(nameof(snapshots));
			Generation = generation;
		}

		public Vector3 Origin { get; }

		public IReadOnlyList<FogChunkSnapshotLease> Snapshots => snapshots
			?? throw new ObjectDisposedException(nameof(BuildRequest));

		public long Generation { get; }

		public void Dispose()
		{
			FogChunkSnapshotLease[] released = Interlocked.Exchange(ref snapshots, null);
			if (released is not null)
			{
				DisposeSnapshots(released);
			}
		}
	}

	private readonly record struct BuildResult(
		Vector3 Origin,
		byte[] Bytes,
		byte[] OccupancyBytes,
		int ActiveCells,
		long Generation,
		double Milliseconds,
		long AllocatedBytes);
}
#endif
