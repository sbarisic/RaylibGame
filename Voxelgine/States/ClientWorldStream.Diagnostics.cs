using System.Diagnostics;
using FishGfx.Voxels;
using Voxelgine.Engine;
using Voxelgine.Engine.DI;
using Voxelgine.FishGfxClient.Voxels;
using Voxelgine.Graphics;

namespace Voxelgine.States;

internal sealed partial class ClientWorldStream
{
	internal int DecodeQueueDepth => columnDecodeChannel?.Reader.Count ?? 0;
	internal int ApplyQueueDepth => decodedColumns.Count;
	internal int DeferredAcknowledgements => acknowledgements.Count;
	internal bool IsBackpressured => CalculateBackpressure();
	internal int CoreReceived => receivedCoreColumns.Count;
	internal int CoreApplied => appliedCoreColumns.Count;
	internal int HaloReceived => receivedHaloColumns.Count;
	internal int HaloApplied => appliedHaloColumns.Count;
	internal int OrdinaryReceived => receivedOrdinaryColumns.Count;
	internal int OrdinaryApplied => appliedOrdinaryColumns.Count;
	internal int CachedColumns => getSimulation()?.Map.ColumnCount ?? 0;
	internal int CoreLit => CountColumnsInState(coreColumnChunks, requireMesh: false);
	internal int CoreMeshed => CountColumnsInState(coreColumnChunks, requireMesh: true);
	internal int HaloLit => CountColumnsInState(haloColumnChunks, requireMesh: false);
	internal int HaloMeshed => CountColumnsInState(haloColumnChunks, requireMesh: true);
	internal double AverageDecodeMilliseconds => decodeCount == 0
		? 0
		: decodeTicks * 1000.0 / Stopwatch.Frequency / decodeCount;
	internal double AverageApplyMilliseconds => applyCount == 0
		? 0
		: applyTicks * 1000.0 / Stopwatch.Frequency / applyCount;

	internal float LoadingProgress
	{
		get
		{
			int expected = expectedCoreColumns + expectedHaloColumns;
			if (expected <= 0)
				return 0;
			float received = Math.Clamp((float)(CoreReceived + HaloReceived) / expected, 0, 1);
			float applied = Math.Clamp((float)(CoreApplied + HaloApplied) / expected, 0, 1);
			return clientReadySent ? 0.95f : received * 0.4f + applied * 0.5f;
		}
	}

	internal string Status
	{
		get
		{
			if (StreamId == 0)
				return getClient()?.State == ClientState.Connecting ? "Connecting" : "Starting hosted server";
			if (CoreReceived < expectedCoreColumns || HaloReceived < expectedHaloColumns)
				return $"Receiving bootstrap columns ({CoreReceived + HaloReceived}/{expectedCoreColumns + expectedHaloColumns})";
			if (CoreApplied < expectedCoreColumns || HaloApplied < expectedHaloColumns)
				return $"Applying terrain ({CoreApplied + HaloApplied}/{expectedCoreColumns + expectedHaloColumns})";
			if (getScene()?.IsLightingIdle != true)
				return "Computing nearby lighting";
			if (!clientReadySent)
				return "Uploading nearby meshes";
			return "Waiting for server start";
		}
	}

	private int CountColumnsInState(
		IReadOnlyDictionary<ChunkColumnCoordinate, ChunkCoordinate[]> columns,
		bool requireMesh)
	{
		FishGfxVoxelScene scene = getScene();
		if (scene == null)
			return 0;

		int complete = 0;
		foreach (ChunkCoordinate[] chunks in columns.Values)
		{
			bool columnComplete = true;
			foreach (ChunkCoordinate chunk in chunks)
			{
				VoxelPresentationState state = scene.GetPresentationState(chunk);
				columnComplete &= requireMesh
					? state is VoxelPresentationState.Resident or VoxelPresentationState.EmptyComplete
					: state is VoxelPresentationState.Meshing or VoxelPresentationState.Resident or VoxelPresentationState.EmptyComplete;
				if (!columnComplete)
					break;
			}
			if (columnComplete)
				complete++;
		}
		return complete;
	}

	private void LogReadinessBlockers()
	{
		float now = getTime();
		if (now < nextReadinessLogTime)
			return;
		nextReadinessLogTime = now + 2;

		int missing = 0;
		int waitingForLighting = 0;
		int meshing = 0;
		int resident = 0;
		int emptyComplete = 0;
		FishGfxVoxelScene scene = getScene();
		if (scene != null)
		{
			foreach (ChunkCoordinate coordinate in coreChunks)
			{
				switch (scene.GetPresentationState(coordinate))
				{
					case VoxelPresentationState.Missing: missing++; break;
					case VoxelPresentationState.WaitingForLighting: waitingForLighting++; break;
					case VoxelPresentationState.Meshing: meshing++; break;
					case VoxelPresentationState.Resident: resident++; break;
					case VoxelPresentationState.EmptyComplete: emptyComplete++; break;
				}
			}
		}

		VoxelRendererWorkload workload = scene?.Workload ?? default;
		VoxelRendererFrameDiagnostics renderer = scene?.FrameDiagnostics ?? default;
		logging.Log(
			GameLogLevel.Debug,
			"WorldStream",
			$"bootstrap-wait streamId={StreamId} complete={bootstrapComplete} "
			+ $"core={CoreApplied}/{expectedCoreColumns} halo={HaloApplied}/{expectedHaloColumns} "
			+ $"lightingIdle={scene?.IsLightingIdle == true} transparentReady={scene?.HasValidTransparentOrdering == true} "
			+ $"states=missing:{missing},lighting:{waitingForLighting},meshing:{meshing},resident:{resident},empty:{emptyComplete} "
			+ $"work=dirty:{workload.DirtyMeshes},running:{workload.InFlightMeshes},completed:{workload.CompletedMeshes},uploads:{workload.PendingUploadJobs},bytes:{workload.PendingUploadBytes},backpressured:{workload.IsBackpressured} "
			+ $"transparent=pending:{renderer.TransparentOrderingPending},running:{renderer.TransparentOrderingRunning},faces:{renderer.TransparentFaceCount},indices:{renderer.TransparentIndexCount},uploadBytes:{renderer.TransparentUploadBytes},stale:{renderer.TransparentStaleResults},reason:{renderer.TransparentInvalidationReason}");
	}
}
