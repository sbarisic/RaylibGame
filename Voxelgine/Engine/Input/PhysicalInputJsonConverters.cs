using Newtonsoft.Json;
using Voxelgine.Engine.Input;

namespace Voxelgine.Engine;

public sealed class PhysicalKeyJsonConverter : JsonConverter<PhysicalKey>
{
	public override PhysicalKey ReadJson(
		JsonReader reader,
		Type objectType,
		PhysicalKey existingValue,
		bool hasExistingValue,
		JsonSerializer serializer)
	{
		if (reader.TokenType == JsonToken.Integer)
			return (PhysicalKey)Convert.ToInt32(reader.Value);

		if (reader.TokenType == JsonToken.String &&
			PhysicalInputNames.TryParseKey((string)reader.Value, out PhysicalKey key))
		{
			return key;
		}

		throw new JsonSerializationException($"Unknown physical key '{reader.Value}'.");
	}

	public override void WriteJson(JsonWriter writer, PhysicalKey value, JsonSerializer serializer)
	{
		writer.WriteValue(value.ToString());
	}
}

public sealed class PhysicalMouseButtonJsonConverter : JsonConverter<PhysicalMouseButton>
{
	public override PhysicalMouseButton ReadJson(
		JsonReader reader,
		Type objectType,
		PhysicalMouseButton existingValue,
		bool hasExistingValue,
		JsonSerializer serializer)
	{
		if (reader.TokenType == JsonToken.Integer)
			return (PhysicalMouseButton)Convert.ToInt32(reader.Value);

		if (reader.TokenType == JsonToken.String &&
			PhysicalInputNames.TryParseMouseButton((string)reader.Value, out PhysicalMouseButton button))
		{
			return button;
		}

		throw new JsonSerializationException($"Unknown physical mouse button '{reader.Value}'.");
	}

	public override void WriteJson(
		JsonWriter writer,
		PhysicalMouseButton value,
		JsonSerializer serializer)
	{
		writer.WriteValue(value.ToString());
	}
}
