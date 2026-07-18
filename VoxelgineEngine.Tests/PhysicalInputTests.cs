using Voxelgine.Engine.Input;

namespace VoxelgineEngine.Tests;

public sealed class PhysicalInputTests
{
	[Theory]
	[InlineData("Kp0", PhysicalKey.Numpad0)]
	[InlineData("Kp9", PhysicalKey.Numpad9)]
	[InlineData("KpEnter", PhysicalKey.NumpadEnter)]
	[InlineData("Zero", PhysicalKey.Alpha0)]
	[InlineData("Grave", PhysicalKey.GraveAccent)]
	[InlineData("escape", PhysicalKey.Escape)]
	public void LegacyAndCurrentKeyNamesRemainLoadable(string serializedName, PhysicalKey expected)
	{
		Assert.True(PhysicalInputNames.TryParseKey(serializedName, out PhysicalKey actual));
		Assert.Equal(expected, actual);
	}

	[Theory]
	[InlineData("Left", PhysicalMouseButton.Left)]
	[InlineData("middle", PhysicalMouseButton.Middle)]
	[InlineData("Button8", PhysicalMouseButton.Button8)]
	public void MouseButtonNamesAreCaseInsensitive(string serializedName, PhysicalMouseButton expected)
	{
		Assert.True(PhysicalInputNames.TryParseMouseButton(serializedName, out PhysicalMouseButton actual));
		Assert.Equal(expected, actual);
	}

	[Fact]
	public void UnknownKeyNameIsRejected()
	{
		Assert.False(PhysicalInputNames.TryParseKey("NotAKey", out _));
	}

	[Fact]
	public void PhysicalCodesMatchTheWindowBackendCodes()
	{
		Assert.Equal(65, (int)PhysicalKey.A);
		Assert.Equal(256, (int)PhysicalKey.Escape);
		Assert.Equal(320, (int)PhysicalKey.Numpad0);
		Assert.Equal(0, (int)PhysicalMouseButton.Left);
		Assert.Equal(2, (int)PhysicalMouseButton.Middle);
	}
}
