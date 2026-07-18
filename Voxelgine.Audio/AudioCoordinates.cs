using System.Numerics;

namespace Voxelgine.Audio;

public static class AudioCoordinates
{
    // The game is Y-up with +Z forward. miniaudio's listener convention is
    // Y-up with -Z forward, so only the Z axis changes at the native boundary.
    public static Vector3 ToNative(Vector3 gameVector) => new(
        gameVector.X,
        gameVector.Y,
        -gameVector.Z);

    public static float CalculateStereoPan(
        in AudioListener listener,
        Vector3 sourcePosition)
    {
        Vector3 toSource = sourcePosition - listener.Position;
        if (toSource.LengthSquared() <= float.Epsilon)
        {
            return 0.0f;
        }

        Vector3 forward = NormalizeOr(listener.Forward, Vector3.UnitZ);
        Vector3 up = NormalizeOr(listener.Up, Vector3.UnitY);
        Vector3 right = NormalizeOr(Vector3.Cross(up, forward), Vector3.UnitX);
        return Math.Clamp(Vector3.Dot(Vector3.Normalize(toSource), right), -1.0f, 1.0f);
    }

    public static float InverseDistanceGain(
        float distance,
        float minDistance,
        float maxDistance)
    {
        if (minDistance <= 0.0f)
        {
            throw new ArgumentOutOfRangeException(nameof(minDistance));
        }

        if (maxDistance < minDistance)
        {
            throw new ArgumentOutOfRangeException(nameof(maxDistance));
        }

        if (distance >= maxDistance)
        {
            return 0.0f;
        }

        return minDistance / Math.Max(distance, minDistance);
    }

    private static Vector3 NormalizeOr(Vector3 value, Vector3 fallback) =>
        value.LengthSquared() > float.Epsilon
            ? Vector3.Normalize(value)
            : fallback;
}
