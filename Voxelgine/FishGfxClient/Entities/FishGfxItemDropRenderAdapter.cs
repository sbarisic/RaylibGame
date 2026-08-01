#if WINDOWS
using FishGfx;
using FishGfx.Graphics;
using System.Numerics;
using Voxelgine.Engine;
using Voxelgine.Engine.Geometry;

namespace Voxelgine.FishGfxClient.Entities;

public sealed class FishGfxItemDropRenderAdapter
{
	private static readonly EntityModelPose RestPose = new();
	private readonly FishGfxEntityRenderAssets assets;

	internal FishGfxItemDropRenderAdapter(FishGfxEntityRenderAssets assets)
	{
		this.assets = assets;
	}

	public void Render(
		RenderPass pass,
		in ItemDropRenderState state,
		in EntityWorldLighting lighting)
	{
		if (state.Stack.IsEmpty)
			return;

		FishGfxEntityModel model = assets.ItemModel(state.Stack.Item);
		model.Render(
			pass,
			CreateTransform(state),
			RestPose,
			assets.ItemTexture(state.Stack.Item),
			Color.White,
			assets.LitShader,
			state.Light,
			lighting);
	}

	public void RenderShadow(RenderPass pass, in ItemDropRenderState state)
	{
		if (state.Stack.IsEmpty)
			return;

		assets.ItemModel(state.Stack.Item).RenderShadow(
			pass,
			CreateTransform(state),
			RestPose,
			assets.ItemTexture(state.Stack.Item),
			assets.ShadowShader);
	}

	public EntityRenderBounds GetAnimationBounds(in ItemDropRenderState state)
	{
		return state.Stack.IsEmpty
			? EntityRenderBounds.Empty
			: assets.ItemModel(state.Stack.Item).CalculateBounds(
				CreateTransform(state),
				RestPose);
	}

	public bool TryPick(in Ray3 ray, in ItemDropRenderState state, out EntityModelHit hit)
	{
		if (state.Stack.IsEmpty)
		{
			hit = default;
			return false;
		}

		return assets.ItemModel(state.Stack.Item).TryIntersect(
			ray,
			CreateTransform(state),
			RestPose,
			out hit);
	}

	private static Matrix4x4 CreateTransform(in ItemDropRenderState state)
	{
		Vector3 position = state.Position + new Vector3(
			state.Size.X * 0.5f,
			state.BobOffset,
			state.Size.Z * 0.5f);
		ItemDefinition definition = ItemCatalog.Get(state.Stack.Item);
		float yaw = state.RotationDegrees * MathF.PI / 180;
		if (definition.PlacesBlock is null)
		{
			Vector3 towardCamera = state.CameraPosition - position;
			yaw = MathF.Atan2(towardCamera.X, towardCamera.Z);
		}

		Quaternion rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw);
		Vector3 scale = definition.PlacesBlock is null
			? new Vector3(0.45f)
			: new Vector3(0.35f);
		return Camera.CreateModel(position, scale, rotation);
	}
}
#endif
