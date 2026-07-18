using Newtonsoft.Json;
using Voxelgine.Engine;
using Voxelgine.Engine.DI;
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

	[Fact]
	public void GameConfigLoadsLegacyKeysBeforeGeneralEnumConversion()
	{
		const string json = """
		{
		  "LogLevel": "Info",
		  "TwoKeysDown": [
		    {
		      "Key": "Num0",
		      "Value": { "Key": "Zero", "Value": "Kp0" }
		    }
		  ]
		}
		""";
		GameConfig config = new(null);

		JsonConvert.PopulateObject(json, config, GameConfig.CreateJsonSettings());

		Assert.Equal(GameLogLevel.Info, config.LogLevel);
		KeyValuePair<InputKey, KeyValuePair<PhysicalKey, PhysicalKey>> binding =
			Assert.Single(config.TwoKeysDown);
		Assert.Equal(InputKey.Num0, binding.Key);
		Assert.Equal(PhysicalKey.Alpha0, binding.Value.Key);
		Assert.Equal(PhysicalKey.Numpad0, binding.Value.Value);

		string roundTrip = JsonConvert.SerializeObject(config, GameConfig.CreateJsonSettings());
		Assert.Contains("\"Alpha0\"", roundTrip, StringComparison.Ordinal);
		Assert.Contains("\"Numpad0\"", roundTrip, StringComparison.Ordinal);
	}
}
