#if WINDOWS
using FishGfx;
using FishGfx.Graphics;
using System.Numerics;
using System.Runtime.CompilerServices;
using Voxelgine.Engine;
using Voxelgine.Engine.Geometry;
using Voxelgine.FishGfxClient.Assets;

namespace Voxelgine.FishGfxClient.Entities;

/// <summary>
/// Window-scoped entity render resources. GPU meshes are owned here and must be
/// disposed before the FishGfx window. Texture ownership follows the same scope,
/// which also makes repeated game-state construction and teardown leak-free.
/// </summary>
public sealed class FishGfxEntityRenderAssets : IDisposable
{
	private static readonly ConditionalWeakTable<IFishGfxGameWindow, SharedResources> ResourcesByWindow = new();
	private readonly SharedResources resources;
	private bool disposed;

	public FishGfxEntityRenderAssets(IFishGfxGameWindow window)
	{
		ArgumentNullException.ThrowIfNull(window);
		resources = ResourcesByWindow.GetValue(window, CreateSharedResources);
	}

	private static SharedResources CreateSharedResources(IFishGfxGameWindow window)
	{
		string models = Path.Combine(AppContext.BaseDirectory, "data", "models");
		string textures = Path.Combine(AppContext.BaseDirectory, "data", "textures");
		string humanoidPath = Path.Combine(models, EntityAssetIds.HumanoidModel);
		string doorPath = Path.Combine(models, EntityAssetIds.SlidingDoorModel);
		string orbPath = Path.Combine(models, EntityAssetIds.ExperienceOrbModel);
		string animationDirectory = Path.Combine(
			AppContext.BaseDirectory,
			"data",
			"animations",
			"npc"
		);
		string[] animationPaths = new[] { "idle", "walk", "attack", "crouch" }
			.Select(name => Path.Combine(animationDirectory, $"{name}.npcanim.json"))
			.ToArray();

		AssetHandle<FishGfxEntityModel> humanoid = window.Assets.Register(
			"entity.humanoid.model",
			() => new FishGfxEntityModel(
				window.RenderWindow.Graphics,
				EntityModelSource.LoadBlockModel(humanoidPath)
			),
			humanoidPath
		);
		AssetHandle<FishGfxEntityModel> door = window.Assets.Register(
			"entity.door.model",
			() => new FishGfxEntityModel(
				window.RenderWindow.Graphics,
				EntityModelSource.LoadBlockModel(doorPath)
			),
			doorPath
		);
		AssetHandle<FishGfxEntityModel> orb = window.Assets.Register(
			"entity.orb.model",
			() => new FishGfxEntityModel(
				window.RenderWindow.Graphics,
				EntityModelSource.LoadObj(orbPath)
			),
			orbPath
		);
		AssetHandle<Texture> humanoidTexture = window.Assets.LoadColorTexture(
			"entity.humanoid.texture",
			Path.Combine(textures, EntityAssetIds.HumanoidTexture)
		);
		AssetHandle<Texture> alternateTexture = window.Assets.LoadColorTexture(
			"entity.humanoid.alternate-texture",
			Path.Combine(textures, EntityAssetIds.HumanoidTextureAlternate)
		);
		AssetHandle<Texture> doorTexture = window.Assets.Register(
			"entity.door.texture",
			() => SlidingDoorTextureFactory.Create(window.RenderWindow.Graphics)
		);
		AssetHandle<Texture> orbTexture = window.Assets.LoadColorTexture(
			"entity.orb.texture",
			Path.Combine(models, EntityAssetIds.ExperienceOrbTexture)
		);
		AssetHandle<FishGfxAnimationLibrary> animations = window.Assets.Register(
			"entity.animations",
			() => FishGfxAnimationLibrary.LoadStandard(animationDirectory),
			animationPaths
		);
		AssetHandle<ShaderProgram> litShader = window.Assets.LoadShader(
			"entity.lit.shader",
			"data/shaders/fishgfx/entity_lit.vert",
			"data/shaders/fishgfx/entity_lit.frag"
		);
		AssetHandle<ShaderProgram> shadowShader = window.Assets.LoadShader(
			"entity.shadow.shader",
			"data/shaders/fishgfx/entity_shadow.vert",
			"data/shaders/fishgfx/entity_shadow.frag"
		);

		return new SharedResources(
			humanoid,
			door,
			orb,
			humanoidTexture,
			alternateTexture,
			doorTexture,
			orbTexture,
			animations,
			litShader,
			shadowShader
		);
	}

	public FishGfxRemotePlayerRenderAdapter CreateRemotePlayerAdapter()
	{
		ThrowIfDisposed();
		return new FishGfxRemotePlayerRenderAdapter(
			this,
			new FishGfxAnimationPlayer(() => resources.Animations.Value)
		);
	}

	public FishGfxNpcRenderAdapter CreateNpcAdapter()
	{
		ThrowIfDisposed();
		return new FishGfxNpcRenderAdapter(
			this,
			new FishGfxAnimationPlayer(() => resources.Animations.Value)
		);
	}

	public FishGfxSlidingDoorRenderAdapter CreateSlidingDoorAdapter()
	{
		ThrowIfDisposed();
		return new FishGfxSlidingDoorRenderAdapter(this);
	}

	public FishGfxPickupRenderAdapter CreatePickupAdapter()
	{
		ThrowIfDisposed();
		return new FishGfxPickupRenderAdapter(this);
	}

	public void Dispose()
	{
		if (disposed)
		{
			return;
		}

		disposed = true;
	}

	internal FishGfxEntityModel Humanoid
	{
		get
		{
			ThrowIfDisposed();
			return resources.Humanoid.Value;
		}
	}

	internal FishGfxEntityModel SlidingDoor
	{
		get
		{
			ThrowIfDisposed();
			return resources.SlidingDoor.Value;
		}
	}

	internal FishGfxEntityModel ExperienceOrb
	{
		get
		{
			ThrowIfDisposed();
			return resources.ExperienceOrb.Value;
		}
	}

	internal Texture HumanoidTexture(string assetId)
	{
		ThrowIfDisposed();
		return string.Equals(
			assetId,
			EntityAssetIds.HumanoidTextureAlternate,
			StringComparison.OrdinalIgnoreCase
		)
			? resources.HumanoidTextureAlternate.Value
			: resources.HumanoidTexture.Value;
	}

	internal Texture SlidingDoorTexture
	{
		get
		{
			ThrowIfDisposed();
			return resources.SlidingDoorTexture.Value;
		}
	}

	internal Texture ExperienceOrbTexture
	{
		get
		{
			ThrowIfDisposed();
			return resources.ExperienceOrbTexture.Value;
		}
	}

	internal ShaderProgram LitShader
	{
		get
		{
			ThrowIfDisposed();
			return resources.LitShader.Value;
		}
	}

	internal ShaderProgram ShadowShader
	{
		get
		{
			ThrowIfDisposed();
			return resources.ShadowShader.Value;
		}
	}

	private void ThrowIfDisposed()
	{
		ObjectDisposedException.ThrowIf(disposed, this);
	}

	private sealed record SharedResources(
		AssetHandle<FishGfxEntityModel> Humanoid,
		AssetHandle<FishGfxEntityModel> SlidingDoor,
		AssetHandle<FishGfxEntityModel> ExperienceOrb,
		AssetHandle<Texture> HumanoidTexture,
		AssetHandle<Texture> HumanoidTextureAlternate,
		AssetHandle<Texture> SlidingDoorTexture,
		AssetHandle<Texture> ExperienceOrbTexture,
		AssetHandle<FishGfxAnimationLibrary> Animations,
		AssetHandle<ShaderProgram> LitShader,
		AssetHandle<ShaderProgram> ShadowShader
	);
}

public sealed class FishGfxRemotePlayerRenderAdapter
{
	private readonly FishGfxEntityRenderAssets assets;
	private readonly FishGfxAnimationPlayer animation;
	private readonly EntityModelFrameData frameData;
	private RemotePlayerRenderState state;
	private Matrix4x4 rootTransform;
	private EntityRenderBounds bounds;
	private bool hasState;

	internal FishGfxRemotePlayerRenderAdapter(
		FishGfxEntityRenderAssets assets,
		FishGfxAnimationPlayer animation
	)
	{
		this.assets = assets;
		this.animation = animation;
		frameData = assets.Humanoid.CreateFrameData();
	}

	public void Update(in RemotePlayerRenderState newState, float deltaSeconds)
	{
		state = newState;
		animation.SetRemoteAnimationState(state.AnimationState);
		animation.Update(
			deltaSeconds,
			new Vector3(Math.Clamp(state.CameraAngle.Y, -80, 80), 0, 0),
			replaceHeadPitch: true
		);
		Vector3 feet = state.EyePosition - Vector3.UnitY * Player.PlayerEyeOffset;
		float yaw = state.CameraAngle.X * MathF.PI / 180;
		rootTransform = EntityTransformMath.CreateFacing(
			feet,
			new Vector3(MathF.Sin(yaw), 0, MathF.Cos(yaw))
		);
		assets.Humanoid.UpdateFrameData(frameData, rootTransform, animation.Pose);
		bounds = frameData.Bounds;
		hasState = true;
	}

	public void Render(RenderPass pass, in EntityWorldLighting lighting)
	{
		EnsureState();
		assets.Humanoid.Render(
			pass,
			frameData,
			assets.HumanoidTexture(EntityAssetIds.HumanoidTexture),
			Color.White,
			assets.LitShader,
			state.Light,
			lighting
		);
	}

	public void Render(RenderPass pass)
	{
		EntityWorldLighting lighting = EntityWorldLighting.Unshadowed;
		Render(pass, lighting);
	}

	public void RenderShadow(RenderPass pass)
	{
		EnsureState();
		assets.Humanoid.RenderShadow(
			pass,
			frameData,
			assets.HumanoidTexture(EntityAssetIds.HumanoidTexture),
			assets.ShadowShader
		);
	}

	public EntityRenderBounds GetAnimationBounds()
	{
		EnsureState();
		return bounds;
	}

	public void SetLight(in EntityLightSample light)
	{
		EnsureState();
		state = state with { Light = light };
	}

	public bool TryPick(in Ray3 ray, out EntityModelHit hit)
	{
		EnsureState();
		return assets.Humanoid.TryIntersect(ray, rootTransform, animation.Pose, out hit);
	}

	public Matrix4x4 GetAttachmentTransform(string partName)
	{
		EnsureState();
		return assets.Humanoid.GetPartTransform(partName, frameData);
	}

	private void EnsureState()
	{
		if (!hasState)
		{
			throw new InvalidOperationException("Update must be called before rendering or querying a remote player.");
		}
	}

	private static Color ToFishColor(Rgba32 color)
	{
		return new Color(color.R, color.G, color.B, color.A);
	}
}

public sealed class FishGfxNpcRenderAdapter
{
	private readonly FishGfxEntityRenderAssets assets;
	private readonly FishGfxAnimationPlayer animation;
	private readonly EntityModelFrameData frameData;
	private NpcRenderState state;
	private Matrix4x4 rootTransform;
	private EntityRenderBounds bounds;
	private bool hasState;

	internal FishGfxNpcRenderAdapter(
		FishGfxEntityRenderAssets assets,
		FishGfxAnimationPlayer animation
	)
	{
		this.assets = assets;
		this.animation = animation;
		frameData = assets.Humanoid.CreateFrameData();
	}

	public void Update(in NpcRenderState newState, float deltaSeconds)
	{
		state = newState;
		animation.SetBaseAnimation(state.AnimationName);
		animation.Update(deltaSeconds, state.HeadRotation);
		Vector3 position = state.Position;
		rootTransform = EntityTransformMath.CreateFacing(position, state.LookDirection);
		assets.Humanoid.UpdateFrameData(frameData, rootTransform, animation.Pose);
		bounds = frameData.Bounds;
		hasState = true;
	}

	public void Render(RenderPass pass, in EntityWorldLighting lighting)
	{
		EnsureState();
		assets.Humanoid.Render(
			pass,
			frameData,
			assets.HumanoidTexture(state.TextureAssetId),
			Color.White,
			assets.LitShader,
			state.Light,
			lighting
		);
	}

	public void Render(RenderPass pass)
	{
		EntityWorldLighting lighting = EntityWorldLighting.Unshadowed;
		Render(pass, lighting);
	}

	public void RenderShadow(RenderPass pass)
	{
		EnsureState();
		assets.Humanoid.RenderShadow(
			pass,
			frameData,
			assets.HumanoidTexture(state.TextureAssetId),
			assets.ShadowShader
		);
	}

	public EntityRenderBounds GetAnimationBounds()
	{
		EnsureState();
		return bounds;
	}

	public bool TryPick(in Ray3 ray, out EntityModelHit hit)
	{
		EnsureState();
		return assets.Humanoid.TryIntersect(ray, rootTransform, animation.Pose, out hit);
	}

	public Matrix4x4 GetAttachmentTransform(string partName)
	{
		EnsureState();
		return assets.Humanoid.GetPartTransform(partName, frameData);
	}

	public void SetLight(in EntityLightSample light)
	{
		EnsureState();
		state = state with { Light = light };
	}

	private void EnsureState()
	{
		if (!hasState)
		{
			throw new InvalidOperationException("Update must be called before rendering or querying an NPC.");
		}
	}

	private static Color ToFishColor(Rgba32 color)
	{
		return new Color(color.R, color.G, color.B, color.A);
	}
}

public sealed class FishGfxSlidingDoorRenderAdapter
{
	private static readonly EntityModelPose RestPose = new();
	private readonly FishGfxEntityRenderAssets assets;

	internal FishGfxSlidingDoorRenderAdapter(FishGfxEntityRenderAssets assets)
	{
		this.assets = assets;
	}

	public void Render(
		RenderPass pass,
		in SlidingDoorRenderState state,
		in EntityWorldLighting lighting)
	{
		assets.SlidingDoor.Render(
			pass,
			CreateTransform(state),
			RestPose,
			assets.SlidingDoorTexture,
			Color.White,
			assets.LitShader,
			state.Light,
			lighting
		);
	}

	public void Render(RenderPass pass, in SlidingDoorRenderState state)
	{
		EntityWorldLighting lighting = EntityWorldLighting.Unshadowed;
		Render(pass, state, lighting);
	}

	public void RenderShadow(RenderPass pass, in SlidingDoorRenderState state)
	{
		assets.SlidingDoor.RenderShadow(
			pass,
			CreateTransform(state),
			RestPose,
			assets.SlidingDoorTexture,
			assets.ShadowShader
		);
	}

	public EntityRenderBounds GetAnimationBounds(in SlidingDoorRenderState state)
	{
		return assets.SlidingDoor.CalculateBounds(CreateTransform(state), RestPose);
	}

	public bool TryPick(
		in Ray3 ray,
		in SlidingDoorRenderState state,
		out EntityModelHit hit
	)
	{
		return assets.SlidingDoor.TryIntersect(ray, CreateTransform(state), RestPose, out hit);
	}

	private static Matrix4x4 CreateTransform(in SlidingDoorRenderState state)
	{
		Vector3 facing = EntityTransformMath.NormalizeHorizontal(state.FacingDirection);
		float facingAngle = MathF.Atan2(facing.X, facing.Z) - MathF.PI;
		float hingeAngle = -Math.Clamp(state.OpenProgress, 0, 1)
			* state.OpenAngleDegrees
			* MathF.PI / 180;
		Vector3 position = state.Position;
		return Matrix4x4.CreateTranslation(-0.5f, 0, 0)
			* Matrix4x4.CreateRotationY(hingeAngle)
			* Matrix4x4.CreateTranslation(0.5f, 0, 0)
			* Matrix4x4.CreateRotationY(facingAngle)
			* Matrix4x4.CreateTranslation(position);
	}

	private static Color ToFishColor(Rgba32 color)
	{
		return new Color(color.R, color.G, color.B, color.A);
	}
}

public sealed class FishGfxPickupRenderAdapter
{
	private static readonly EntityModelPose RestPose = new();
	private readonly FishGfxEntityRenderAssets assets;

	internal FishGfxPickupRenderAdapter(FishGfxEntityRenderAssets assets)
	{
		this.assets = assets;
	}

	public void Render(
		RenderPass pass,
		in PickupRenderState state,
		in EntityWorldLighting lighting)
	{
		assets.ExperienceOrb.Render(
			pass,
			CreateTransform(state),
			RestPose,
			assets.ExperienceOrbTexture,
			Color.White,
			assets.LitShader,
			state.Light,
			lighting
		);
	}

	public void Render(RenderPass pass, in PickupRenderState state)
	{
		EntityWorldLighting lighting = EntityWorldLighting.Unshadowed;
		Render(pass, state, lighting);
	}

	public void RenderShadow(RenderPass pass, in PickupRenderState state)
	{
		assets.ExperienceOrb.RenderShadow(
			pass,
			CreateTransform(state),
			RestPose,
			assets.ExperienceOrbTexture,
			assets.ShadowShader
		);
	}

	public EntityRenderBounds GetAnimationBounds(in PickupRenderState state)
	{
		return assets.ExperienceOrb.CalculateBounds(CreateTransform(state), RestPose);
	}

	public bool TryPick(in Ray3 ray, in PickupRenderState state, out EntityModelHit hit)
	{
		return assets.ExperienceOrb.TryIntersect(ray, CreateTransform(state), RestPose, out hit);
	}

	private static Matrix4x4 CreateTransform(in PickupRenderState state)
	{
		Vector3 position = state.Position + new Vector3(
			state.Size.X * 0.5f,
			state.BobOffset,
			state.Size.Z * 0.5f
		);
		Quaternion rotation = Quaternion.CreateFromAxisAngle(
			Vector3.UnitY,
			state.RotationDegrees * MathF.PI / 180
		);
		return FishGfx.Graphics.Camera.CreateModel(position, Vector3.One, rotation);
	}

	private static Color ToFishColor(Rgba32 color)
	{
		return new Color(color.R, color.G, color.B, color.A);
	}
}

internal static class EntityTransformMath
{
	public static Matrix4x4 CreateFacing(Vector3 position, Vector3 direction)
	{
		Vector3 horizontal = NormalizeHorizontal(direction);
		float angle = MathF.Atan2(horizontal.X, horizontal.Z) - MathF.PI;
		return Matrix4x4.CreateRotationY(angle) * Matrix4x4.CreateTranslation(position);
	}

	public static Vector3 NormalizeHorizontal(Vector3 direction)
	{
		Vector3 horizontal = new(direction.X, 0, direction.Z);
		return horizontal.LengthSquared() > 0.000001f
			? Vector3.Normalize(horizontal)
			: Vector3.UnitZ;
	}
}
#endif
