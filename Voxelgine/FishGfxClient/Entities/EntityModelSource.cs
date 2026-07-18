#if WINDOWS
using FishGfx;
using FishGfx.Formats;
using FishGfx.Graphics;
using System.Numerics;
using System.Text.Json;
using Voxelgine.Engine.Geometry;
using FishColor = FishGfx.Color;
using FishVertex3 = FishGfx.Vertex3;

namespace Voxelgine.FishGfxClient.Entities;

internal sealed class EntityModelSource
{
	private static readonly int[] TriangleOrder = [0, 1, 2, 3, 0, 2];
	private readonly List<EntityModelPartSource> parts = new();

	private EntityModelSource(Winding winding)
	{
		FrontFaceWinding = winding;
	}

	public IReadOnlyList<EntityModelPartSource> Parts => parts;
	public Winding FrontFaceWinding { get; }

	public static EntityModelSource LoadBlockModel(string path)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);

		try
		{
			using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
			JsonElement root = document.RootElement;
			if (!root.TryGetProperty("elements", out JsonElement elements)
				|| elements.ValueKind != JsonValueKind.Array)
			{
				throw new FormatException("Block model requires an elements array.");
			}

			EntityModelSource source = new(Winding.CounterClockwise);
			int index = 0;
			foreach (JsonElement element in elements.EnumerateArray())
			{
				source.parts.Add(ReadElement(element, index));
				index++;
			}

			ApplyGroupHierarchy(root, source.parts);
			ValidateHierarchy(source.parts);
			return source;
		}
		catch (JsonException exception)
		{
			throw new FormatException($"Block model '{path}' contains invalid JSON.", exception);
		}
	}

	public static EntityModelSource LoadObj(string path)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);
		IReadOnlyList<GenericMesh> meshes = ObjModelSerializer.Load(path);
		EntityModelSource source = new(Winding.Clockwise);
		int index = 0;
		foreach (GenericMesh generic in meshes)
		{
			FishVertex3[] vertices = generic.Vertices.ToArray();
			source.parts.Add(new EntityModelPartSource(
				string.IsNullOrWhiteSpace(generic.MaterialName)
					? $"part_{index}"
					: generic.MaterialName,
				null,
				Vector3.Zero,
				Vector3.Zero,
				vertices,
				CreateTriangles(vertices)
			));
			index++;
		}

		if (source.parts.Count == 0)
		{
			throw new FormatException($"OBJ model '{path}' contains no drawable material groups.");
		}

		return source;
	}

	private static EntityModelPartSource ReadElement(JsonElement element, int index)
	{
		string name = element.TryGetProperty("name", out JsonElement nameElement)
			? nameElement.GetString()
			: null;
		if (string.IsNullOrWhiteSpace(name))
		{
			name = $"element_{index}";
		}

		Vector3 from = ReadVector3(element, "from") / 16 - new Vector3(0.5f, 0, 0.5f);
		Vector3 to = ReadVector3(element, "to") / 16 - new Vector3(0.5f, 0, 0.5f);
		if (to.X < from.X || to.Y < from.Y || to.Z < from.Z)
		{
			throw new FormatException($"Model element '{name}' has unordered bounds.");
		}

		ReadRotation(element, out Vector3 pivot, out Vector3 baseRotation);
		pivot -= new Vector3(0.5f, 0, 0.5f);
		if (!element.TryGetProperty("faces", out JsonElement faces)
			|| faces.ValueKind != JsonValueKind.Object)
		{
			throw new FormatException($"Model element '{name}' requires a faces object.");
		}

		List<FishVertex3> vertices = new();
		foreach (JsonProperty faceProperty in faces.EnumerateObject())
		{
			GetFace(
				faceProperty.Name,
				from,
				to,
				out Vector3[] corners,
				out Vector2[] cornerUvs
			);
			JsonElement face = faceProperty.Value;
			Vector4 sourceUv = ReadVector4(face, "uv") / 16;
			int faceRotation = ReadFaceRotation(face);
			for (int triangleIndex = 0; triangleIndex < TriangleOrder.Length; triangleIndex++)
			{
				int cornerIndex = TriangleOrder[triangleIndex];
				Vector2 localUv = RotateFaceUv(cornerUvs[cornerIndex], faceRotation);
				Vector2 uv = new(
					float.Lerp(sourceUv.X, sourceUv.Z, localUv.X),
					float.Lerp(sourceUv.Y, sourceUv.W, localUv.Y)
				);
				vertices.Add(new FishVertex3(corners[cornerIndex], uv, FishColor.White));
			}
		}

		FishVertex3[] array = vertices.ToArray();
		return new EntityModelPartSource(
			name,
			null,
			pivot,
			baseRotation,
			array,
			CreateTriangles(array)
		);
	}

	private static void ApplyGroupHierarchy(JsonElement root, List<EntityModelPartSource> parts)
	{
		if (!root.TryGetProperty("groups", out JsonElement groups)
			|| groups.ValueKind != JsonValueKind.Array)
		{
			return;
		}

		Dictionary<string, int> elementByName = new(StringComparer.Ordinal);
		for (int index = 0; index < parts.Count; index++)
		{
			if (!elementByName.TryAdd(parts[index].Name, index))
			{
				throw new FormatException($"Model contains duplicate element name '{parts[index].Name}'.");
			}
		}

		foreach (JsonElement group in groups.EnumerateArray())
		{
			VisitGroup(group, null, elementByName, parts);
		}
	}

	private static void VisitGroup(
		JsonElement group,
		string parentPart,
		IReadOnlyDictionary<string, int> elementByName,
		List<EntityModelPartSource> parts
	)
	{
		string groupName = group.TryGetProperty("name", out JsonElement nameElement)
			? nameElement.GetString()
			: null;
		string currentPart = parentPart;
		if (!string.IsNullOrWhiteSpace(groupName)
			&& elementByName.TryGetValue(groupName, out int groupedElement))
		{
			parts[groupedElement] = parts[groupedElement] with { ParentName = parentPart };
			currentPart = groupName;
		}

		if (!group.TryGetProperty("children", out JsonElement children)
			|| children.ValueKind != JsonValueKind.Array)
		{
			return;
		}

		foreach (JsonElement child in children.EnumerateArray())
		{
			if (child.ValueKind == JsonValueKind.Number && child.TryGetInt32(out int elementIndex))
			{
				if (elementIndex < 0 || elementIndex >= parts.Count)
				{
					throw new FormatException("Model group references an invalid element index.");
				}
				if (!string.Equals(parts[elementIndex].Name, currentPart, StringComparison.Ordinal))
				{
					parts[elementIndex] = parts[elementIndex] with { ParentName = currentPart };
				}
			}
			else if (child.ValueKind == JsonValueKind.Object)
			{
				VisitGroup(child, currentPart, elementByName, parts);
			}
		}
	}

	private static void ValidateHierarchy(IReadOnlyList<EntityModelPartSource> parts)
	{
		Dictionary<string, EntityModelPartSource> byName = parts.ToDictionary(
			part => part.Name,
			StringComparer.Ordinal
		);
		foreach (EntityModelPartSource part in parts)
		{
			HashSet<string> visited = new(StringComparer.Ordinal) { part.Name };
			string parent = part.ParentName;
			while (parent is not null)
			{
				if (!byName.TryGetValue(parent, out EntityModelPartSource parentPart))
				{
					throw new FormatException($"Model part '{part.Name}' references missing parent '{parent}'.");
				}
				if (!visited.Add(parent))
				{
					throw new FormatException($"Model hierarchy contains a cycle at '{parent}'.");
				}
				parent = parentPart.ParentName;
			}
		}
	}

	private static Triangle3[] CreateTriangles(IReadOnlyList<FishVertex3> vertices)
	{
		if (vertices.Count % 3 != 0)
		{
			throw new FormatException("Entity model vertices do not form complete triangles.");
		}

		Triangle3[] triangles = new Triangle3[vertices.Count / 3];
		for (int index = 0; index < triangles.Length; index++)
		{
			int vertex = index * 3;
			triangles[index] = new Triangle3(
				vertices[vertex].Position,
				vertices[vertex + 1].Position,
				vertices[vertex + 2].Position
			);
		}
		return triangles;
	}

	private static void ReadRotation(
		JsonElement element,
		out Vector3 pivot,
		out Vector3 degrees
	)
	{
		pivot = new Vector3(0.5f, 0, 0.5f);
		degrees = Vector3.Zero;
		if (!element.TryGetProperty("rotation", out JsonElement rotation)
			|| rotation.ValueKind == JsonValueKind.Null)
		{
			return;
		}

		if (rotation.TryGetProperty("origin", out _))
		{
			pivot = ReadVector3(rotation, "origin") / 16;
		}

		if (rotation.TryGetProperty("axis", out JsonElement axisElement))
		{
			float angle = ReadSingle(rotation, "angle", required: true);
			degrees = axisElement.GetString()?.ToLowerInvariant() switch
			{
				"x" => new Vector3(angle, 0, 0),
				"y" => new Vector3(0, angle, 0),
				"z" => new Vector3(0, 0, angle),
				_ => throw new FormatException("Model rotation axis must be x, y, or z."),
			};
		}
		else
		{
			degrees = new Vector3(
				ReadSingle(rotation, "x", required: false),
				ReadSingle(rotation, "y", required: false),
				ReadSingle(rotation, "z", required: false)
			);
		}
	}

	private static int ReadFaceRotation(JsonElement face)
	{
		if (!face.TryGetProperty("rotation", out JsonElement value))
		{
			return 0;
		}
		if (!value.TryGetInt32(out int rotation)
			|| (rotation != 0 && rotation != 90 && rotation != 180 && rotation != 270))
		{
			throw new FormatException("Face rotation must be 0, 90, 180, or 270 degrees.");
		}
		return rotation;
	}

	private static Vector2 RotateFaceUv(Vector2 uv, int rotation)
	{
		return rotation switch
		{
			0 => uv,
			90 => new Vector2(uv.Y, 1 - uv.X),
			180 => Vector2.One - uv,
			270 => new Vector2(1 - uv.Y, uv.X),
			_ => throw new ArgumentOutOfRangeException(nameof(rotation)),
		};
	}

	private static void GetFace(
		string name,
		Vector3 min,
		Vector3 max,
		out Vector3[] corners,
		out Vector2[] uvs
	)
	{
		uvs = [new Vector2(1, 0), Vector2.Zero, new Vector2(0, 1), Vector2.One];
		switch (name.ToLowerInvariant())
		{
			case "east":
				corners =
				[
					new(max.X, max.Y, min.Z), new(max.X, max.Y, max.Z),
					new(max.X, min.Y, max.Z), new(max.X, min.Y, min.Z),
				];
				break;
			case "west":
				corners =
				[
					new(min.X, max.Y, max.Z), new(min.X, max.Y, min.Z),
					new(min.X, min.Y, min.Z), new(min.X, min.Y, max.Z),
				];
				break;
			case "up":
				corners =
				[
					new(max.X, max.Y, min.Z), new(min.X, max.Y, min.Z),
					new(min.X, max.Y, max.Z), new(max.X, max.Y, max.Z),
				];
				break;
			case "down":
				corners =
				[
					new(max.X, min.Y, max.Z), new(min.X, min.Y, max.Z),
					new(min.X, min.Y, min.Z), new(max.X, min.Y, min.Z),
				];
				break;
			case "south":
				corners =
				[
					new(max.X, min.Y, max.Z), new(max.X, max.Y, max.Z),
					new(min.X, max.Y, max.Z), new(min.X, min.Y, max.Z),
				];
				uvs = [Vector2.One, new Vector2(1, 0), Vector2.Zero, new Vector2(0, 1)];
				break;
			case "north":
				corners =
				[
					new(max.X, max.Y, min.Z), new(max.X, min.Y, min.Z),
					new(min.X, min.Y, min.Z), new(min.X, max.Y, min.Z),
				];
				uvs = [Vector2.Zero, new Vector2(0, 1), Vector2.One, new Vector2(1, 0)];
				break;
			default:
				throw new FormatException($"Unsupported face direction '{name}'.");
		}
	}

	private static Vector3 ReadVector3(JsonElement owner, string name)
	{
		JsonElement value = ReadArray(owner, name, 3);
		return new Vector3(ReadSingle(value[0]), ReadSingle(value[1]), ReadSingle(value[2]));
	}

	private static Vector4 ReadVector4(JsonElement owner, string name)
	{
		JsonElement value = ReadArray(owner, name, 4);
		return new Vector4(
			ReadSingle(value[0]),
			ReadSingle(value[1]),
			ReadSingle(value[2]),
			ReadSingle(value[3])
		);
	}

	private static JsonElement ReadArray(JsonElement owner, string name, int length)
	{
		if (!owner.TryGetProperty(name, out JsonElement value)
			|| value.ValueKind != JsonValueKind.Array
			|| value.GetArrayLength() != length)
		{
			throw new FormatException($"Model property '{name}' must contain {length} values.");
		}
		return value;
	}

	private static float ReadSingle(JsonElement owner, string name, bool required)
	{
		if (!owner.TryGetProperty(name, out JsonElement value))
		{
			if (required)
			{
				throw new FormatException($"Model property '{name}' is required.");
			}
			return 0;
		}
		return ReadSingle(value);
	}

	private static float ReadSingle(JsonElement value)
	{
		if (!value.TryGetSingle(out float result) || !float.IsFinite(result))
		{
			throw new FormatException("Model numeric values must be finite numbers.");
		}
		return result;
	}
}

internal sealed record EntityModelPartSource(
	string Name,
	string ParentName,
	Vector3 Pivot,
	Vector3 BaseRotationDegrees,
	FishVertex3[] Vertices,
	Triangle3[] Triangles
);
#endif
