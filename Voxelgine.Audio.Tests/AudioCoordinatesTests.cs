using System.Numerics;

namespace Voxelgine.Audio.Tests;

public sealed class AudioCoordinatesTests
{
    [Fact]
    public void ToNative_FlipsOnlyForwardAxis()
    {
        Vector3 converted = AudioCoordinates.ToNative(new Vector3(2, 3, 4));

        Assert.Equal(new Vector3(2, 3, -4), converted);
    }

    [Fact]
    public void CalculateStereoPan_UsesGamePositiveXAsRight()
    {
        AudioListener listener = AudioListener.Default;

        float right = AudioCoordinates.CalculateStereoPan(listener, Vector3.UnitX);
        float left = AudioCoordinates.CalculateStereoPan(listener, -Vector3.UnitX);

        Assert.Equal(1.0f, right, 5);
        Assert.Equal(-1.0f, left, 5);
    }

    [Theory]
    [InlineData(0.25f, 1.0f)]
    [InlineData(1.0f, 1.0f)]
    [InlineData(2.0f, 0.5f)]
    [InlineData(8.0f, 0.125f)]
    [InlineData(32.0f, 0.0f)]
    public void InverseDistanceGain_ClampsNearAndFar(
        float distance,
        float expected)
    {
        float actual = AudioCoordinates.InverseDistanceGain(distance, 1, 32);

        Assert.Equal(expected, actual, 5);
    }
}
