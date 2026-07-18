using System.Numerics;
using Voxelgine;
using Voxelgine.Engine;

namespace VoxelgineEngine.Tests;

public sealed class AabbTests
{
	[Fact]
	public void DefaultConstructor_CreatesUnitBox()
	{
		AABB aabb = new();
		Assert.Equal(Vector3.Zero, aabb.Position);
		Assert.Equal(Vector3.One, aabb.Size);
		Assert.False(aabb.IsEmpty);
	}

	[Fact]
	public void Constructor_WithPositionAndSize_SetsProperties()
	{
		Vector3 position = new(1, 2, 3);
		Vector3 size = new(4, 5, 6);
		AABB aabb = new(position, size);

		Assert.Equal(position, aabb.Position);
		Assert.Equal(size, aabb.Size);
	}

	[Fact]
	public void Empty_IsMarkedAsEmpty()
	{
		Assert.True(AABB.Empty.IsEmpty);
	}

	[Fact]
	public void Contains_PointInsideOrOnEdge_ReturnsTrue()
	{
		AABB aabb = new(Vector3.Zero, new Vector3(10));

		Assert.True(aabb.Contains(new Vector3(5)));
		Assert.True(aabb.Contains(Vector3.Zero));
		Assert.True(aabb.Contains(new Vector3(10)));
	}

	[Fact]
	public void Contains_PointOutside_ReturnsFalse()
	{
		AABB aabb = new(Vector3.Zero, new Vector3(10));
		Assert.False(aabb.Contains(new Vector3(15, 5, 5)));
	}

	[Fact]
	public void Offset_ReturnsNewAabbWithOffsetPosition()
	{
		AABB aabb = new(Vector3.Zero, Vector3.One);
		Vector3 offset = new(5);
		AABB result = aabb.Offset(offset);

		Assert.Equal(offset, result.Position);
		Assert.Equal(Vector3.One, result.Size);
	}

	[Fact]
	public void GetCorners_ReturnsAllEightCorners()
	{
		AABB aabb = new(Vector3.Zero, Vector3.One);
		Vector3[] corners = aabb.GetCorners();

		Assert.Equal(8, corners.Length);
		Assert.Contains(Vector3.Zero, corners);
		Assert.Contains(Vector3.One, corners);
	}

	[Fact]
	public void Union_CombinesTwoAabbs()
	{
		AABB first = new(Vector3.Zero, Vector3.One);
		AABB second = new(new Vector3(2), Vector3.One);
		AABB result = AABB.Union(first, second);

		Assert.Equal(Vector3.Zero, result.Position);
		Assert.Equal(new Vector3(3), result.Size);
	}
}

public sealed class EasingTests
{
	[Fact]
	public void Linear_ReturnsInputValue()
	{
		Assert.Equal(0f, Easing.Linear(0f));
		Assert.Equal(0.5f, Easing.Linear(0.5f));
		Assert.Equal(1f, Easing.Linear(1f));
	}

	[Theory]
	[MemberData(nameof(Curves))]
	public void Curve_StartsAtZeroAndEndsAtOne(Func<float, float> curve)
	{
		Assert.Equal(0f, curve(0f), 0.001f);
		Assert.Equal(1f, curve(1f), 0.001f);
	}

	[Fact]
	public void EaseInOutCubic_AtHalf_ReturnsHalf()
	{
		Assert.Equal(0.5f, Easing.EaseInOutCubic(0.5f), 0.001f);
	}

	public static TheoryData<Func<float, float>> Curves => new()
	{
		Easing.EaseInSine,
		Easing.EaseInOutCubic,
		Easing.EaseInOutQuint,
		Easing.EaseInOutQuart,
		Easing.EaseOutBounce,
		Easing.EaseInBounce,
		Easing.EaseInOutBounce,
	};
}

public sealed class UtilsTests
{
	[Theory]
	[InlineData(5, 3, 2)]
	[InlineData(-1, 3, 2)]
	[InlineData(0, 3, 0)]
	[InlineData(3, 3, 0)]
	[InlineData(-3, 3, 0)]
	public void Mod_ReturnsNonNegativeRemainder(int value, int modulus, int expected)
	{
		Assert.Equal(expected, Utils.Mod(value, modulus));
	}

	[Theory]
	[InlineData(5f, 0f, 10f, 5f)]
	[InlineData(-5f, 0f, 10f, 0f)]
	[InlineData(15f, 0f, 10f, 10f)]
	public void ClampFloat_ReturnsClampedValue(float value, float minimum, float maximum, float expected)
	{
		Assert.Equal(expected, Utils.Clamp(value, minimum, maximum));
	}

	[Theory]
	[InlineData(5, 0, 10, 5)]
	[InlineData(-5, 0, 10, 0)]
	[InlineData(15, 0, 10, 10)]
	public void ClampInt_ReturnsClampedValue(int value, int minimum, int maximum, int expected)
	{
		Assert.Equal(expected, Utils.Clamp(value, minimum, maximum));
	}

	[Fact]
	public void ParseFloat_UsesInvariantNumericTextAndDefault()
	{
		Assert.Equal(3.14f, "3.14".ParseFloat(), 0.001f);
		Assert.Equal(5f, string.Empty.ParseFloat(5f));
	}

	[Fact]
	public void ParseInt_UsesNumericTextAndDefault()
	{
		Assert.Equal(42, "42".ParseInt());
		Assert.Equal(10, string.Empty.ParseInt(10));
	}

	[Fact]
	public void Swap_ExchangesValues()
	{
		int first = 1;
		int second = 2;

		Utils.Swap(ref first, ref second);

		Assert.Equal(2, first);
		Assert.Equal(1, second);
	}

	[Fact]
	public void AngleConversions_RoundTrip()
	{
		Assert.Equal(180f, Utils.ToDeg(MathF.PI), 4);
		Assert.Equal(MathF.PI, Utils.ToRad(180f), 4);
	}

	[Theory]
	[InlineData(-1d, 0d, 24d, 23d)]
	[InlineData(24d, 0d, 24d, 0d)]
	[InlineData(25d, 0d, 24d, 1d)]
	public void NormalizeLoop_WrapsIntoRange(double value, double start, double end, double expected)
	{
		Assert.Equal(expected, Utils.NormalizeLoop(value, start, end), 8);
	}

	[Theory]
	[InlineData(-1, 0, 0, 0)]
	[InlineData(1, 0, 0, 1)]
	[InlineData(0, -1, 0, 2)]
	[InlineData(0, 1, 0, 3)]
	[InlineData(0, 0, -1, 4)]
	[InlineData(0, 0, 1, 5)]
	public void DirToByte_PreservesAxisDirectionContract(float x, float y, float z, byte expected)
	{
		Assert.Equal(expected, Utils.DirToByte(new Vector3(x, y, z)));
	}
}

public sealed class NoiseTests
{
	[Fact]
	public void CalcArrays_ReturnRequestedDimensions()
	{
		Assert.Equal(10, Noise.Calc1D(10, 0.1f).Length);

		float[,] twoDimensions = Noise.Calc2D(10, 20, 0.1f);
		Assert.Equal(10, twoDimensions.GetLength(0));
		Assert.Equal(20, twoDimensions.GetLength(1));

		float[,,] threeDimensions = Noise.Calc3D(5, 10, 15, 0.1f);
		Assert.Equal(5, threeDimensions.GetLength(0));
		Assert.Equal(10, threeDimensions.GetLength(1));
		Assert.Equal(15, threeDimensions.GetLength(2));
	}

	[Fact]
	public void PixelSamples_RemainWithinByteRange()
	{
		Assert.InRange(Noise.CalcPixel1D(5, 0.1f), 0, 256);
		Assert.InRange(Noise.CalcPixel2D(5, 10, 0.1f), 0, 256);
		Assert.InRange(Noise.CalcPixel3D(5, 10, 15, 0.1f), 0, 256);
	}

	[Fact]
	public void SameSeed_ProducesSameResult()
	{
		Noise.Seed = 12345;
		float first = Noise.CalcPixel2D(10, 10, 0.1f);

		Noise.Seed = 12345;
		float second = Noise.CalcPixel2D(10, 10, 0.1f);

		Assert.Equal(first, second);
	}

	[Fact]
	public void DifferentSeeds_ProduceDifferentResults()
	{
		Noise.Seed = 111;
		float first = Noise.CalcPixel2D(100, 100, 0.5f);

		Noise.Seed = 222;
		float second = Noise.CalcPixel2D(100, 100, 0.5f);

		Assert.NotEqual(first, second);
	}
}
