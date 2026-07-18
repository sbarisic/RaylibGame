#if WINDOWS
using FishGfx;
using FishGfx.Graphics;
using FishGfx.Graphics.Drawables;
using FishGfx.Voxels;
using System.Numerics;
using System.Text.Json;
using Voxelgine.Engine.Geometry;
using Voxelgine.FishGfxClient.Assets;

namespace Voxelgine.FishGfxClient.Models;

public sealed class FishGfxJsonModel
{
	private readonly AssetHandle<Mesh3D> mesh;
	private readonly AssetHandle<Texture> texture;
	private Triangle3[] triangles = Array.Empty<Triangle3>();

	public FishGfxJsonModel(
		IFishGfxGameWindow window,
		string id,
		string modelPath,
		string texturePath,
		int textureWidth,
		int textureHeight
	)
	{
		ArgumentNullException.ThrowIfNull(window);
		string fullModelPath = Resolve(modelPath);
		string fullTexturePath = Resolve(texturePath);
		texture = window.Assets.LoadTexture($"{id}.texture", fullTexturePath);
		mesh = window.Assets.RegisterMesh(
			$"{id}.mesh",
			() => LoadMesh(
				window.RenderWindow.Graphics,
				fullModelPath,
				textureWidth,
				textureHeight
			),
			fullModelPath
		);
	}

	public IReadOnlyList<Triangle3> Triangles => triangles;

	public void Draw(RenderPass pass, Vector3 position, Vector3 scale, Quaternion rotation)
	{
		using IDisposable modelScope = pass.PushModel(Camera.CreateModel(position, scale, rotation));
		pass.DrawMesh(mesh.Value, texture.Value);
	}

	public bool TryIntersect(in Ray3 worldRay, Matrix4x4 modelTransform, out TriangleHit hit)
	{
		if (!Matrix4x4.Invert(modelTransform, out Matrix4x4 inverse))
		{
			hit = default;
			return false;
		}

		Ray3 localRay = new(
			Vector3.Transform(worldRay.Origin, inverse),
			Vector3.TransformNormal(worldRay.Direction, inverse)
		);
		if (!TriangleIntersection.TryIntersectClosest(localRay, triangles, out TriangleHit localHit))
		{
			hit = default;
			return false;
		}

		Vector3 worldPosition = Vector3.Transform(localHit.Position, modelTransform);
		Vector3 worldNormal = Vector3.Normalize(Vector3.TransformNormal(localHit.Normal, modelTransform));
		hit = new TriangleHit(
			Vector3.Distance(worldRay.Origin, worldPosition),
			worldPosition,
			worldNormal
		);
		return true;
	}

	private Mesh3D LoadMesh(
		GraphicsContext graphics,
		string modelPath,
		int textureWidth,
		int textureHeight
	)
	{
		VoxelTextureRegion fullTexture = new(
			0,
			0,
			textureWidth,
			textureHeight,
			textureWidth,
			textureHeight
		);
		Dictionary<string, VoxelTextureRegion> regions = ReadTextureRegions(
			modelPath,
			fullTexture
		);
		VoxelModel model = MinecraftVoxelModelLoader.LoadFile(modelPath, regions);
		FishGfx.Vertex3[] vertices = new FishGfx.Vertex3[model.Vertices.Count];
		Triangle3[] cpuTriangles = new Triangle3[model.Vertices.Count / 3];
		for (int index = 0; index < model.Vertices.Count; index++)
		{
			VoxelVertex source = model.Vertices[index];
			Vector3 position = source.Position - new Vector3(0.5f, 0, 0.5f);
			vertices[index] = new FishGfx.Vertex3(position, source.TextureCoordinates, source.Color);
		}
		for (int index = 0; index < cpuTriangles.Length; index++)
		{
			int vertex = index * 3;
			cpuTriangles[index] = new Triangle3(
				vertices[vertex].Position,
				vertices[vertex + 1].Position,
				vertices[vertex + 2].Position
			);
		}
		Mesh3D mesh = graphics.CreateMesh3D(vertices);
		triangles = cpuTriangles;
		return mesh;
	}

	private static Dictionary<string, VoxelTextureRegion> ReadTextureRegions(
		string modelPath,
		VoxelTextureRegion fullTexture
	)
	{
		Dictionary<string, VoxelTextureRegion> regions = new(StringComparer.Ordinal);
		using JsonDocument document = JsonDocument.Parse(File.ReadAllText(modelPath));
		if (
			document.RootElement.TryGetProperty("textures", out JsonElement textures)
			&& textures.ValueKind == JsonValueKind.Object
		)
		{
			foreach (JsonProperty texture in textures.EnumerateObject())
			{
				regions[texture.Name] = fullTexture;
			}
		}

		// Older files conventionally omit the textures object and use #0 directly.
		regions.TryAdd("0", fullTexture);
		return regions;
	}

	private static string Resolve(string path)
	{
		return Path.IsPathRooted(path)
			? Path.GetFullPath(path)
			: Path.GetFullPath(path, AppContext.BaseDirectory);
	}
}
#endif
