using Newtonsoft.Json;
using Voxelgine.Engine;
using Voxelgine.Engine.Input;

namespace UnitTest;

public class PhysicalInputTests
{
	[Theory]
	[InlineData("Kp0", PhysicalKey.Numpad0)]
	[InlineData("Kp9", PhysicalKey.Numpad9)]
	[InlineData("KpEnter", PhysicalKey.NumpadEnter)]
	[InlineData("Zero", PhysicalKey.Alpha0)]
	[InlineData("Grave", PhysicalKey.GraveAccent)]
	public void LegacyRaylibKeyNamesRemainLoadable(string serializedName, PhysicalKey expected)
	{
		string json = $"\"{serializedName}\"";
		PhysicalKey actual = JsonConvert.DeserializeObject<PhysicalKey>(
			json,
			new PhysicalKeyJsonConverter());

		Assert.Equal(expected, actual);
	}

	[Theory]
	[InlineData("Side", PhysicalMouseButton.Button4)]
	[InlineData("Extra", PhysicalMouseButton.Button5)]
	[InlineData("Forward", PhysicalMouseButton.Button6)]
	[InlineData("Back", PhysicalMouseButton.Button7)]
	public void LegacyRaylibMouseNamesRemainLoadable(
		string serializedName,
		PhysicalMouseButton expected)
	{
		string json = $"\"{serializedName}\"";
		PhysicalMouseButton actual = JsonConvert.DeserializeObject<PhysicalMouseButton>(
			json,
			new PhysicalMouseButtonJsonConverter());

		Assert.Equal(expected, actual);
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
