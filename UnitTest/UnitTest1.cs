using System.Numerics;
using Voxelgine;
using Voxelgine.Engine;

namespace UnitTest {
	public class AABBTests {
		[Fact]
		public void DefaultConstructor_CreatesUnitBox() {
			var aabb = new AABB();
			Assert.Equal(Vector3.Zero, aabb.Position);
			Assert.Equal(Vector3.One, aabb.Size);
			Assert.False(aabb.IsEmpty);
		}

		[Fact]
		public void Constructor_WithPositionAndSize_SetsProperties() {
			var pos = new Vector3(1, 2, 3);
			var size = new Vector3(4, 5, 6);
			var aabb = new AABB(pos, size);

			Assert.Equal(pos, aabb.Position);
			Assert.Equal(size, aabb.Size);
		}

		[Fact]
		public void Empty_IsMarkedAsEmpty() {
			Assert.True(AABB.Empty.IsEmpty);
		}

		[Fact]
		public void Contains_PointInside_ReturnsTrue() {
			var aabb = new AABB(Vector3.Zero, new Vector3(10, 10, 10));
			Assert.True(aabb.Contains(new Vector3(5, 5, 5)));
		}

		[Fact]
		public void Contains_PointOutside_ReturnsFalse() {
			var aabb = new AABB(Vector3.Zero, new Vector3(10, 10, 10));
			Assert.False(aabb.Contains(new Vector3(15, 5, 5)));
		}

		[Fact]
		public void Contains_PointOnEdge_ReturnsTrue() {
			var aabb = new AABB(Vector3.Zero, new Vector3(10, 10, 10));
			Assert.True(aabb.Contains(new Vector3(0, 0, 0)));
			Assert.True(aabb.Contains(new Vector3(10, 10, 10)));
		}

		[Fact]
		public void Offset_ReturnsNewAABBWithOffsetPosition() {
			var aabb = new AABB(Vector3.Zero, Vector3.One);
			var offset = new Vector3(5, 5, 5);
			var result = aabb.Offset(offset);

			Assert.Equal(offset, result.Position);
			Assert.Equal(Vector3.One, result.Size);
		}

		[Fact]
		public void GetCorners_Returns8Corners() {
			var aabb = new AABB(Vector3.Zero, new Vector3(1, 1, 1));
			var corners = aabb.GetCorners();

			Assert.Equal(8, corners.Length);
			Assert.Contains(new Vector3(0, 0, 0), corners);
			Assert.Contains(new Vector3(1, 1, 1), corners);
		}

		[Fact]
		public void Union_CombinesTwoAABBs() {
			var a = new AABB(Vector3.Zero, Vector3.One);
			var b = new AABB(new Vector3(2, 2, 2), Vector3.One);
			var result = AABB.Union(a, b);

			Assert.Equal(Vector3.Zero, result.Position);
			Assert.Equal(new Vector3(3, 3, 3), result.Size);
		}
	}

	public class EasingTests {
		[Fact]
		public void Linear_ReturnsInputValue() {
			Assert.Equal(0f, Easing.Linear(0f));
			Assert.Equal(0.5f, Easing.Linear(0.5f));
			Assert.Equal(1f, Easing.Linear(1f));
		}

		[Fact]
		public void EaseInSine_StartsAt0_EndsAt1() {
			Assert.Equal(0f, Easing.EaseInSine(0f), 0.001f);
			Assert.Equal(1f, Easing.EaseInSine(1f), 0.001f);
		}

		[Fact]
		public void EaseInOutCubic_StartsAt0_EndsAt1() {
			Assert.Equal(0f, Easing.EaseInOutCubic(0f), 0.001f);
			Assert.Equal(1f, Easing.EaseInOutCubic(1f), 0.001f);
		}

		[Fact]
		public void EaseInOutCubic_AtHalf_Returns0Point5() {
			Assert.Equal(0.5f, Easing.EaseInOutCubic(0.5f), 0.001f);
		}

		[Fact]
		public void EaseInOutQuint_StartsAt0_EndsAt1() {
			Assert.Equal(0f, Easing.EaseInOutQuint(0f), 0.001f);
			Assert.Equal(1f, Easing.EaseInOutQuint(1f), 0.001f);
		}

		[Fact]
		public void EaseInOutQuart_StartsAt0_EndsAt1() {
			Assert.Equal(0f, Easing.EaseInOutQuart(0f), 0.001f);
			Assert.Equal(1f, Easing.EaseInOutQuart(1f), 0.001f);
		}

		[Fact]
		public void EaseOutBounce_StartsAt0_EndsAt1() {
			Assert.Equal(0f, Easing.EaseOutBounce(0f), 0.001f);
			Assert.Equal(1f, Easing.EaseOutBounce(1f), 0.001f);
		}

		[Fact]
		public void EaseInBounce_StartsAt0_EndsAt1() {
			Assert.Equal(0f, Easing.EaseInBounce(0f), 0.001f);
			Assert.Equal(1f, Easing.EaseInBounce(1f), 0.001f);
		}

		[Fact]
		public void EaseInOutBounce_StartsAt0_EndsAt1() {
			Assert.Equal(0f, Easing.EaseInOutBounce(0f), 0.001f);
			Assert.Equal(1f, Easing.EaseInOutBounce(1f), 0.001f);
		}
	}

	public class UtilsTests {
		[Theory]
		[InlineData(5, 3, 2)]
		[InlineData(-1, 3, 2)]
		[InlineData(0, 3, 0)]
		[InlineData(3, 3, 0)]
		[InlineData(-3, 3, 0)]
		public void Mod_ReturnsCorrectResult(int a, int b, int expected) {
			Assert.Equal(expected, Utils.Mod(a, b));
		}

		[Theory]
		[InlineData(5f, 0f, 10f, 5f)]
		[InlineData(-5f, 0f, 10f, 0f)]
		[InlineData(15f, 0f, 10f, 10f)]
		public void ClampFloat_ReturnsClampedValue(float num, float min, float max, float expected) {
			Assert.Equal(expected, Utils.Clamp(num, min, max));
		}

		[Theory]
		[InlineData(5, 0, 10, 5)]
		[InlineData(-5, 0, 10, 0)]
		[InlineData(15, 0, 10, 10)]
		public void ClampInt_ReturnsClampedValue(int num, int min, int max, int expected) {
			Assert.Equal(expected, Utils.Clamp(num, min, max));
		}

		[Fact]
		public void ParseFloat_ValidString_ReturnsFloat() {
			Assert.Equal(3.14f, "3.14".ParseFloat(), 0.001f);
		}

		[Fact]
		public void ParseFloat_WithDefault_EmptyString_ReturnsDefault() {
			Assert.Equal(5.0f, "".ParseFloat(5.0f));
		}

		[Fact]
		public void ParseInt_ValidString_ReturnsInt() {
			Assert.Equal(42, "42".ParseInt());
		}

		[Fact]
		public void ParseInt_WithDefault_EmptyString_ReturnsDefault() {
			Assert.Equal(10, "".ParseInt(10));
		}

		[Fact]
		public void Swap_SwapsValues() {
			int a = 1, b = 2;
			Utils.Swap(ref a, ref b);
			Assert.Equal(2, a);
			Assert.Equal(1, b);
		}
	}

	public class NoiseTests {
		[Fact]
		public void Calc1D_ReturnsArrayOfCorrectLength() {
			var result = Noise.Calc1D(10, 0.1f);
			Assert.Equal(10, result.Length);
		}

		[Fact]
		public void Calc2D_ReturnsArrayOfCorrectDimensions() {
			var result = Noise.Calc2D(10, 20, 0.1f);
			Assert.Equal(10, result.GetLength(0));
			Assert.Equal(20, result.GetLength(1));
		}

		[Fact]
		public void Calc3D_ReturnsArrayOfCorrectDimensions() {
			var result = Noise.Calc3D(5, 10, 15, 0.1f);
			Assert.Equal(5, result.GetLength(0));
			Assert.Equal(10, result.GetLength(1));
			Assert.Equal(15, result.GetLength(2));
		}

		[Fact]
		public void CalcPixel1D_ReturnsValueInRange() {
			var result = Noise.CalcPixel1D(5, 0.1f);
			Assert.InRange(result, 0, 256);
		}

		[Fact]
		public void CalcPixel2D_ReturnsValueInRange() {
			var result = Noise.CalcPixel2D(5, 10, 0.1f);
			Assert.InRange(result, 0, 256);
		}

		[Fact]
		public void CalcPixel3D_ReturnsValueInRange() {
			var result = Noise.CalcPixel3D(5, 10, 15, 0.1f);
			Assert.InRange(result, 0, 256);
		}

		[Fact]
		public void SameSeed_ProducesSameResults() {
			Noise.Seed = 12345;
			var result1 = Noise.CalcPixel2D(10, 10, 0.1f);

			Noise.Seed = 12345;
			var result2 = Noise.CalcPixel2D(10, 10, 0.1f);

			Assert.Equal(result1, result2);
		}

		[Fact]
		public void DifferentSeeds_ProduceDifferentResults() {
			// Use larger coordinates to ensure seed difference is visible
			Noise.Seed = 111;
			var result1 = Noise.CalcPixel2D(100, 100, 0.5f);

			Noise.Seed = 222;
			var result2 = Noise.CalcPixel2D(100, 100, 0.5f);

			Assert.NotEqual(result1, result2);
		}
	}
}
