using System.Numerics;
using Voxelgine.Engine;

#if WINDOWS
using FishGfx.Graphics;
using FishGfx.Graphics.Shadows;
using Voxelgine.FishGfxClient.Entities;
using FishColor = FishGfx.Color;
#endif

namespace Voxelgine.States;

public unsafe partial class MPClientGameState
{
	private void UpdateRemoteInterpolation(float totalTime, float deltaTime)
	{
		foreach (RemotePlayer remotePlayer in _simulation.Players.GetAllRemotePlayers())
		{
			remotePlayer.Update(totalTime, deltaTime);
			if (_snd is not null && remotePlayer.TryPlayFootstep())
			{
				_snd.Emit(new Voxelgine.Engine.Audio.GameAudioEvent(
					"walk",
					remotePlayer.Position,
					remotePlayer.Velocity,
					remotePlayer.PlayerId
				));
			}
		}
	}

	private void UpdateFishGfxEntityAdapters(float deltaTime)
	{
		if (_fishEntityAssets is null || _fishVoxelScene is null || _simulation is null)
			return;

		ViewFrustum frustum = ViewFrustum.FromCamera(_fishWorldCamera);
		float maximumDistanceSquared = _fishVoxelScene.MaxChunkDrawDistance
			* _fishVoxelScene.MaxChunkDrawDistance;
		long lightingRevision = _fishVoxelScene.GeometryRevision;
		_activeRemotePlayerIds.Clear();
		foreach (RemotePlayer remotePlayer in _simulation.Players.GetAllRemotePlayers())
		{
			_activeRemotePlayerIds.Add(remotePlayer.PlayerId);
			if (!_fishRemotePlayers.TryGetValue(
				remotePlayer.PlayerId,
				out FishGfxRemotePlayerRenderAdapter adapter
			))
			{
				adapter = _fishEntityAssets.CreateRemotePlayerAdapter();
				_fishRemotePlayers.Add(remotePlayer.PlayerId, adapter);
			}

			bool hasRemoteLight = _remotePlayerLightCache.TryGetValue(
				remotePlayer.PlayerId,
				out ActorLightCache cachedRemoteLight);
			EntityLightSample light = hasRemoteLight
				? cachedRemoteLight.Sample
				: default;
			adapter.Update(new RemotePlayerRenderState(
				remotePlayer.Position,
				remotePlayer.CameraAngle,
				remotePlayer.CurrentAnimationState,
				light
			), deltaTime);
			if (IsVisibleActor(frustum, adapter.GetAnimationBounds(), maximumDistanceSquared)
				&& (!hasRemoteLight
					|| NeedsLightSample(remotePlayer.Position, lightingRevision, cachedRemoteLight)))
			{
				light = _fishVoxelScene.SampleEntityLight(remotePlayer.Position);
				adapter.SetLight(light);
				_remotePlayerLightCache[remotePlayer.PlayerId] = CreateLightCache(
					remotePlayer.Position,
					lightingRevision,
					light);
			}
		}
		RemoveStaleActors(_fishRemotePlayers, _remotePlayerLightCache, _activeRemotePlayerIds);

		_activeNpcIds.Clear();
		foreach (VEntNPC npc in _simulation.Entities.GetAllEntities().OfType<VEntNPC>())
		{
			_activeNpcIds.Add(npc.NetworkId);
			if (!_fishNpcs.TryGetValue(npc.NetworkId, out FishGfxNpcRenderAdapter adapter))
			{
				adapter = _fishEntityAssets.CreateNpcAdapter();
				_fishNpcs.Add(npc.NetworkId, adapter);
			}

			Vector3 lightPosition = npc.Position + Vector3.UnitY;
			bool hasNpcLight = _npcLightCache.TryGetValue(
				npc.NetworkId,
				out ActorLightCache cachedNpcLight);
			EntityLightSample light = hasNpcLight
				? cachedNpcLight.Sample
				: default;
			adapter.Update(new NpcRenderState(
				npc.Position,
				npc.Size,
				npc.LookDirection,
				npc.CurrentAnimationName,
				npc.HeadTrackRotation,
				npc.TextureAssetId,
				light
			), deltaTime);
			if (IsVisibleActor(frustum, adapter.GetAnimationBounds(), maximumDistanceSquared)
				&& (!hasNpcLight || NeedsLightSample(lightPosition, lightingRevision, cachedNpcLight)))
			{
				light = _fishVoxelScene.SampleEntityLight(lightPosition);
				adapter.SetLight(light);
				_npcLightCache[npc.NetworkId] = CreateLightCache(
					lightPosition,
					lightingRevision,
					light);
			}
		}
		RemoveStaleActors(_fishNpcs, _npcLightCache, _activeNpcIds);
	}

	private static ActorLightCache CreateLightCache(
		Vector3 position,
		long revision,
		in EntityLightSample sample) => new(
		(int)MathF.Floor(position.X),
		(int)MathF.Floor(position.Y),
		(int)MathF.Floor(position.Z),
		revision,
		sample);

	private static bool NeedsLightSample(
		Vector3 position,
		long revision,
		in ActorLightCache cached) =>
		cached.LightingRevision != revision
		|| cached.X != (int)MathF.Floor(position.X)
		|| cached.Y != (int)MathF.Floor(position.Y)
		|| cached.Z != (int)MathF.Floor(position.Z);

	private void RemoveStaleActors<TAdapter>(
		Dictionary<int, TAdapter> adapters,
		Dictionary<int, ActorLightCache> lightCache,
		HashSet<int> activeIds)
	{
		_staleActorIds.Clear();
		foreach (int id in adapters.Keys)
		{
			if (!activeIds.Contains(id))
			{
				_staleActorIds.Add(id);
			}
		}

		foreach (int id in _staleActorIds)
		{
			adapters.Remove(id);
			lightCache.Remove(id);
		}
	}

	private static Vector3 NormalizeOr(Vector3 value, Vector3 fallback)
	{
		float lengthSquared = value.LengthSquared();
		return float.IsFinite(lengthSquared) && lengthSquared > 1e-12f
			? value / MathF.Sqrt(lengthSquared)
			: fallback;
	}

	private bool IsVisibleActor(
		ViewFrustum frustum,
		EntityRenderBounds bounds,
		float maximumDistanceSquared)
	{
		return !bounds.IsEmpty
			&& Vector3.DistanceSquared(_fishCameraState.Position, bounds.Center)
				<= maximumDistanceSquared
			&& Intersects(frustum, bounds);
	}

	private static EntityRenderBounds ToRenderBounds(AABB bounds) =>
		bounds.IsEmpty
			? EntityRenderBounds.Empty
			: new EntityRenderBounds(bounds.Min, bounds.Max);

	private void DrawFishGfxActorShadowCasters(
		RenderPass pass,
		in DirectionalShadowCascade cascade)
	{
		ViewFrustum frustum = cascade.CasterFrustum;

		foreach (FishGfxRemotePlayerRenderAdapter adapter in _fishRemotePlayers.Values)
		{
			if (Intersects(frustum, adapter.GetAnimationBounds()))
			{
				adapter.RenderShadow(pass);
			}
		}

		foreach (VoxEntity entity in _simulation.Entities.GetAllEntities())
		{
			switch (entity)
			{
				case VEntNPC npc when _fishNpcs.TryGetValue(
					npc.NetworkId,
					out FishGfxNpcRenderAdapter npcAdapter):
					if (Intersects(frustum, npcAdapter.GetAnimationBounds()))
					{
						npcAdapter.RenderShadow(pass);
					}
					break;

				case VEntSlidingDoor door when _fishDoorRenderer is not null:
					SlidingDoorRenderState doorState = new(
						door.Position,
						door.Size,
						door.FacingDirection,
						door.OpenAmount,
						door.OpenAngleDeg,
						default
					);
					if (Intersects(frustum, _fishDoorRenderer.GetAnimationBounds(doorState)))
					{
						_fishDoorRenderer.RenderShadow(pass, doorState);
					}
					break;

				case VEntPickup pickup when _fishPickupRenderer is not null:
					PickupRenderState pickupState = new(
						pickup.Position,
						pickup.Size,
						pickup.RotationDegrees,
						pickup.VerticalModelOffset,
						default
					);
					if (Intersects(frustum, _fishPickupRenderer.GetAnimationBounds(pickupState)))
					{
						_fishPickupRenderer.RenderShadow(pass, pickupState);
					}
					break;
			}
		}
	}

	private static bool Intersects(ViewFrustum frustum, EntityRenderBounds bounds)
	{
		return !bounds.IsEmpty
			&& frustum.Intersects(new FishGfx.AxisAlignedBoundingBox(bounds.Min, bounds.Max));
	}

}
