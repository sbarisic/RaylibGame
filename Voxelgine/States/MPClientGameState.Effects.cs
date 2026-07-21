using System.Numerics;
using Voxelgine.Engine;

#if WINDOWS
using FishGfx.Graphics;
using Voxelgine.FishGfxClient.Effects;
using Voxelgine.FishGfxClient.Entities;
using Voxelgine.FishGfxClient.Gameplay;
using FishColor = FishGfx.Color;
#endif

namespace Voxelgine.States;

public unsafe partial class MPClientGameState
{
	private void DrawFishGfxGameplayGeometry(
		RenderPass pass,
		in EntityWorldLighting lighting)
	{
		ViewFrustum frustum = ViewFrustum.FromCamera(_fishWorldCamera);
		float maximumDistanceSquared = _fishVoxelScene.MaxChunkDrawDistance
			* _fishVoxelScene.MaxChunkDrawDistance;
		foreach (RemotePlayer remotePlayer in _simulation.Players.GetAllRemotePlayers())
		{
			if (_fishRemotePlayers.TryGetValue(remotePlayer.PlayerId, out FishGfxRemotePlayerRenderAdapter adapter)
				&& IsVisibleActor(
					frustum,
					adapter.GetAnimationBounds(),
					maximumDistanceSquared))
			{
				adapter.Render(pass, lighting);
			}
		}

		foreach (VoxEntity entity in _simulation.Entities.GetAllEntities())
		{
			switch (entity)
			{
				case VEntNPC npc when _fishNpcs.TryGetValue(npc.NetworkId, out FishGfxNpcRenderAdapter npcAdapter):
					if (IsVisibleActor(
						frustum,
						npcAdapter.GetAnimationBounds(),
						maximumDistanceSquared))
					{
						npcAdapter.Render(pass, lighting);
					}
					break;
				case VEntSlidingDoor door when _fishDoorRenderer is not null:
					if (!IsVisibleActor(frustum, ToRenderBounds(door.WorldBounds), maximumDistanceSquared))
					{
						break;
					}
					EntityLightSample doorLight = _fishVoxelScene.SampleEntityLight(
						door.Position + Vector3.UnitY * 0.5f
					);
					_fishDoorRenderer.Render(pass, new SlidingDoorRenderState(
						door.Position,
						door.Size,
						door.FacingDirection,
						door.OpenAmount,
						door.OpenAngleDeg,
						doorLight
					), lighting);
					break;
				case VEntPickup pickup when _fishPickupRenderer is not null:
					if (!IsVisibleActor(frustum, ToRenderBounds(pickup.WorldBounds), maximumDistanceSquared))
					{
						break;
					}
					EntityLightSample pickupLight = _fishVoxelScene.SampleEntityLight(
						pickup.Position + Vector3.UnitY * 0.5f
					);
					_fishPickupRenderer.Render(pass, new PickupRenderState(
						pickup.Position,
						pickup.Size,
						pickup.RotationDegrees,
						pickup.VerticalModelOffset,
						pickupLight
					), lighting);
					break;
				default:
					if (IsVisibleActor(frustum, ToRenderBounds(entity.WorldBounds), maximumDistanceSquared))
					{
						FishGfxGameplayPrimitives.DrawWireBox(
							pass,
							entity.WorldBounds.Min,
							entity.WorldBounds.Max,
							FishColor.Amber
						);
					}
					break;
			}
		}

		InventoryItem activeItem = (_simulation.LocalPlayer as ClientPlayer)?.GetActiveItem();
		if (activeItem is null || !activeItem.IsPlaceableBlock())
		{
			return;
		}

		Vector3? placement = activeItem.GetBlockPlacementPosition(
			_simulation.Map,
			_simulation.LocalPlayer.Position,
			_simulation.LocalPlayer.GetForward(),
			20
		);
		if (placement.HasValue)
		{
			FishGfxGameplayPrimitives.DrawWireBox(
				pass,
				placement.Value,
				placement.Value + Vector3.One,
				FishColor.White,
				2
			);
		}
	}

	private BlockType GetBlockAtImpact(Vector3 hitPosition, Vector3 hitNormal)
	{
		if (_simulation?.Map is null)
		{
			return BlockType.None;
		}

		Vector3 sample = hitPosition - hitNormal * 0.5f;
		return _simulation.Map.GetBlock(
			(int)MathF.Floor(sample.X),
			(int)MathF.Floor(sample.Y),
			(int)MathF.Floor(sample.Z)
		);
	}

	private void SpawnFishGfxEntityHit(
		bool isFlesh,
		Vector3 hitPosition,
		Vector3 hitNormal
	)
	{
		if (_fishParticles is null)
		{
			return;
		}

		Vector3 normal = NormalizeHitNormal(hitNormal);
		if (isFlesh)
		{
			for (int index = 0; index < 8; index++)
			{
				float scale = 0.68f + Random.Shared.NextSingle() * 0.34f;
				_fishParticles.EnqueueBlood(hitPosition, normal * 0.5f, scale);
			}
			return;
		}

		SpawnDirectionalFishParticles(
			FishHitParticleKind.Spark,
			6,
			hitPosition,
			normal,
			Rgba32.White,
			0.5f,
			0.5f,
			10.6f
		);
	}

	private void SpawnFishGfxBlockHit(
		BlockType blockType,
		Vector3 hitPosition,
		Vector3 hitNormal
	)
	{
		if (_fishParticles is null)
		{
			return;
		}

		FishHitMaterial material = blockType switch
		{
			BlockType.None or BlockType.Water or BlockType.Leaf or BlockType.Foliage =>
				FishHitMaterial.None,
			BlockType.Plank or BlockType.Wood or BlockType.CraftingTable or
				BlockType.Barrel or BlockType.Campfire or BlockType.Torch =>
				FishHitMaterial.Wood,
			BlockType.Dirt or BlockType.Grass or BlockType.Sand => FishHitMaterial.Earth,
			BlockType.Glass or BlockType.Ice => FishHitMaterial.Glass,
			_ => FishHitMaterial.Stone,
		};

		Vector3 normal = NormalizeHitNormal(hitNormal);
		switch (material)
		{
			case FishHitMaterial.Stone:
				SpawnDirectionalFishParticles(
					FishHitParticleKind.Spark,
					6,
					hitPosition,
					normal,
					Rgba32.White,
					0.5f,
					0.5f,
					10.6f
				);
				break;
			case FishHitMaterial.Wood:
				SpawnDirectionalFishParticles(
					FishHitParticleKind.Fire,
					4,
					hitPosition,
					normal,
					Rgba32.White,
					0.5f,
					0.5f,
					8
				);
				SpawnDirectionalFishParticles(
					FishHitParticleKind.Smoke,
					2,
					hitPosition,
					normal,
					new Rgba32(180, 160, 130),
					0.4f,
					0.3f,
					4
				);
				break;
			case FishHitMaterial.Earth:
				SpawnDirectionalFishParticles(
					FishHitParticleKind.Smoke,
					5,
					hitPosition,
					normal,
					new Rgba32(160, 140, 100),
					0.5f,
					0.3f,
					5
				);
				break;
			case FishHitMaterial.Glass:
				SpawnDirectionalFishParticles(
					FishHitParticleKind.Spark,
					8,
					hitPosition,
					normal,
					new Rgba32(200, 220, 255),
					0.3f,
					0.3f,
					12
				);
				break;
		}
	}

	private void SpawnDirectionalFishParticles(
		FishHitParticleKind kind,
		int count,
		Vector3 position,
		Vector3 normal,
		Rgba32 color,
		float minimumScale,
		float scaleRange,
		float force
	)
	{
		float spread = 0.6f;
		if (MathF.Abs(normal.Y) < 0.001f)
		{
			force *= 2;
			spread = 0.4f;
		}

		for (int index = 0; index < count; index++)
		{
			Vector3 direction = normal + Utils.GetRandomUnitVector() * spread;
			if (direction.LengthSquared() < 0.000001f)
			{
				direction = normal;
			}
			direction = Vector3.Normalize(direction);
			float scale = minimumScale + Random.Shared.NextSingle() * scaleRange;

			switch (kind)
			{
				case FishHitParticleKind.Fire:
					_fishParticles.EnqueueFire(
						position,
						direction * force,
						color,
						scale,
						noCollision: true,
						lifetime: 1
					);
					break;
				case FishHitParticleKind.Spark:
					_fishParticles.EnqueueSpark(position, direction * force, color, scale);
					break;
				case FishHitParticleKind.Smoke:
					_fishParticles.EnqueueShortSmoke(position, direction * force, color);
					break;
			}
		}
	}

	private static Vector3 NormalizeHitNormal(Vector3 hitNormal)
	{
		return hitNormal.LengthSquared() >= 0.000001f
			? Vector3.Normalize(hitNormal)
			: Vector3.UnitY;
	}

	private enum FishHitMaterial
	{
		None,
		Stone,
		Wood,
		Earth,
		Glass,
	}

	private enum FishHitParticleKind
	{
		Fire,
		Spark,
		Smoke,
	}

}
