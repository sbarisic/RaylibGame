#if WINDOWS
using System.Numerics;
using Voxelgine.Engine;
using Voxelgine.Engine.Geometry;

namespace Voxelgine.FishGfxClient.Entities;

/// <summary>
/// Stable content identifiers used by authoritative entity spawn data.
/// These values intentionally match the existing save/network asset names.
/// </summary>
public static class EntityAssetIds
{
	public const string HumanoidModel = "npc/humanoid.json";
	public const string HumanoidTexture = "npc/humanoid.png";
	public const string HumanoidTextureAlternate = "npc/humanoid2.png";
	public const string SlidingDoorModel = "door/door.json";
	public const string ExperienceOrbModel = "orb_xp/orb_xp.obj";

	internal const string SlidingDoorTexture = "door/door_tex.png";
	internal const string ExperienceOrbTexture = "orb_xp/orb_xp.png";
}

public readonly record struct EntityRenderBounds(Vector3 Min, Vector3 Max)
{
	public Vector3 Size => Max - Min;

	public Vector3 Center => (Min + Max) * 0.5f;

	public static EntityRenderBounds Empty => new(
		new Vector3(float.PositiveInfinity),
		new Vector3(float.NegativeInfinity)
	);

	public bool IsEmpty => !float.IsFinite(Min.X) || !float.IsFinite(Max.X);

	internal EntityRenderBounds Include(Vector3 point)
	{
		return IsEmpty
			? new EntityRenderBounds(point, point)
			: new EntityRenderBounds(Vector3.Min(Min, point), Vector3.Max(Max, point));
	}
}

public readonly record struct EntityModelHit(
	string PartName,
	float Distance,
	Vector3 Position,
	Vector3 Normal
)
{
	internal static EntityModelHit From(string partName, in TriangleHit hit)
	{
		return new EntityModelHit(partName, hit.Distance, hit.Position, hit.Normal);
	}
}

public readonly record struct RemotePlayerRenderState(
	Vector3 EyePosition,
	Vector2 CameraAngle,
	byte AnimationState,
	Rgba32 LightTint
);

public readonly record struct NpcRenderState(
	Vector3 Position,
	Vector3 Size,
	Vector3 LookDirection,
	string AnimationName,
	Vector3 HeadRotation,
	string TextureAssetId,
	Rgba32 LightTint
);

public readonly record struct SlidingDoorRenderState(
	Vector3 Position,
	Vector3 Size,
	Vector3 FacingDirection,
	float OpenProgress,
	float OpenAngleDegrees,
	Rgba32 LightTint
);

public readonly record struct PickupRenderState(
	Vector3 Position,
	Vector3 Size,
	float RotationDegrees,
	float BobOffset,
	Rgba32 LightTint
);

internal readonly record struct EntityPartPose(Vector3 RotationDegrees, Vector3 PositionOffset);

internal sealed class EntityModelPose
{
	private readonly Dictionary<string, EntityPartPose> parts = new(StringComparer.Ordinal);

	public EntityPartPose this[string partName]
	{
		get => parts.TryGetValue(partName, out EntityPartPose pose) ? pose : default;
		set => parts[partName] = value;
	}

	public void Clear()
	{
		parts.Clear();
	}
}
#endif
