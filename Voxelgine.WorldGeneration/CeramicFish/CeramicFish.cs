namespace Voxelgine.WorldGeneration;

/// <summary>
/// Generates a two-dimensional village layout from prefabs whose contents are
/// three-dimensional. The caller remains responsible for spawning the entities.
/// </summary>
public interface ICeramicFish
{
    /// <summary>Checks the prefab catalog before generation starts.</summary>
    CeramicValidationResult ValidatePrefabs(IReadOnlyList<ICeramicPrefab> prefabs);

    /// <summary>Returns whether two facing sockets may be joined.</summary>
    bool AreSocketsCompatible(CeramicSocket first, CeramicSocket second);

    /// <summary>Generates a deterministic village layout.</summary>
    CeramicGenerationResult Generate(
        CeramicGenerationRequest request,
        IReadOnlyList<ICeramicPrefab> prefabs,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// An authored village module such as a road, field, wall, house corner, or door.
/// Every prefab in one catalog must use the same SizeX and SizeZ footprint.
/// </summary>
public interface ICeramicPrefab
{
    /// <summary>Stable, unique identifier used in saved generation plans.</summary>
    string Id { get; }

    /// <summary>Prefab dimensions in world blocks or other caller-defined units.</summary>
    int SizeX { get; }
    int SizeY { get; }
    int SizeZ { get; }

    /// <summary>Entities stored relative to the prefab's minimum corner.</summary>
    IReadOnlyList<CeramicEntity> Entities { get; }

    /// <summary>Exactly one socket for each horizontal direction.</summary>
    IReadOnlyList<CeramicSocket> Sockets { get; }

    /// <summary>Rotations from which the generator may create prefab variants.</summary>
    CeramicRotationOptions AllowedRotations { get; }

    /// <summary>Relative selection probability after all socket rules are satisfied.</summary>
    int Weight { get; }
}

/// <summary>A caller-owned entity value and its local transform inside a prefab.</summary>
public sealed record CeramicEntity(
    int Value,
    int X,
    int Y,
    int Z,
    CeramicRotation Rotation = CeramicRotation.Rot0);

/// <summary>
/// A label on one horizontal prefab face. Facing sockets are compatible when their
/// types are equal. Use "closed" for a face that must not form a connection.
/// </summary>
public sealed record CeramicSocket(CeramicDirection Direction, string Type)
{
    public const string Closed = "closed";
}

/// <summary>Horizontal directions in clockwise order around the Y axis.</summary>
public enum CeramicDirection : byte
{
    North,
    East,
    South,
    West,
}

/// <summary>A single concrete rotation applied to a placed prefab.</summary>
public enum CeramicRotation : short
{
    Rot0 = 0,
    Rot90CW = 90,
    Rot180CW = 180,
    Rot270CW = 270,
}

/// <summary>The rotations permitted by a prefab definition.</summary>
[Flags]
public enum CeramicRotationOptions : byte
{
    None = 0,
    Rot0 = 1 << 0,
    Rot90CW = 1 << 1,
    Rot180CW = 1 << 2,
    Rot270CW = 1 << 3,
    All = Rot0 | Rot90CW | Rot180CW | Rot270CW,
}

/// <param name="Width">Number of prefab cells along X.</param>
/// <param name="Length">Number of prefab cells along Z.</param>
/// <param name="Seed">Deterministic random seed.</param>
public sealed record CeramicGenerationRequest(int Width, int Length, int Seed)
{
    /// <summary>
    /// Socket required on every outside edge that is not listed in Entrances.
    /// </summary>
    public string BoundarySocket { get; init; } = CeramicSocket.Closed;

    /// <summary>Required connections from the generated village to the outside world.</summary>
    public IReadOnlyList<CeramicEntrance> Entrances { get; init; } = [];

    /// <summary>Maximum complete solve attempts before generation reports failure.</summary>
    public int MaxAttempts { get; init; } = 8;
}

/// <summary>
/// A required outward-facing socket on a boundary cell, typically the access road.
/// </summary>
public sealed record CeramicEntrance(
    int X,
    int Z,
    CeramicDirection Direction,
    string SocketType);

/// <summary>A selected prefab variant at one village-grid cell.</summary>
public sealed record CeramicPlacement(
    string PrefabId,
    int X,
    int Z,
    CeramicRotation Rotation);

/// <summary>The serializable output of village generation.</summary>
public sealed record CeramicGenerationResult(
    bool Success,
    IReadOnlyList<CeramicPlacement> Placements,
    int Attempts,
    string? Error = null);

/// <summary>Problems found while checking a prefab catalog.</summary>
public sealed record CeramicValidationResult(
    bool IsValid,
    IReadOnlyList<string> Errors);
