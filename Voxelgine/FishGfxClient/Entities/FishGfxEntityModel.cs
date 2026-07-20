#if WINDOWS
using FishGfx;
using FishGfx.Graphics;
using FishGfx.Graphics.Drawables;
using FishGfx.Voxels;
using System.Numerics;
using Voxelgine.Engine.Geometry;

namespace Voxelgine.FishGfxClient.Entities;

/// <summary>
/// GPU model plus retained CPU triangles. Each Blockbench element remains a
/// separate mesh so animation and attachment transforms are shared by drawing,
/// bounds calculation, and picking.
/// </summary>
internal sealed class FishGfxEntityModel : IDisposable
{
	private const float DegreesToRadians = MathF.PI / 180;
	private readonly List<ModelPart> parts = new();
	private readonly Dictionary<string, ModelPart> partsByName = new(StringComparer.Ordinal);
	private readonly Winding winding;
	private bool disposed;

	public FishGfxEntityModel(GraphicsContext graphics, EntityModelSource source)
	{
		ArgumentNullException.ThrowIfNull(graphics);
		ArgumentNullException.ThrowIfNull(source);
		winding = source.FrontFaceWinding;

		try
		{
			foreach (EntityModelPartSource partSource in source.Parts)
			{
				Mesh3D mesh = graphics.CreateMesh3D(
					partSource.Vertices,
					hasUvs: true,
					hasColors: false
				);
				mesh.SetNormals(CreateFlatNormals(partSource, winding));
				ModelPart part = new(partSource, mesh);
				parts.Add(part);
				partsByName.Add(partSource.Name, part);
			}
		}
		catch
		{
			Dispose();
			throw;
		}
	}

	public IReadOnlyCollection<string> PartNames => partsByName.Keys;

	public void Render(
		RenderPass pass,
		Matrix4x4 rootTransform,
		EntityModelPose pose,
		Texture texture,
		Color tint,
		ShaderProgram shader,
		in EntityLightSample light,
		in EntityWorldLighting worldLighting
	)
	{
		ThrowIfDisposed();
		ArgumentNullException.ThrowIfNull(pass);
		ArgumentNullException.ThrowIfNull(pose);
		ArgumentNullException.ThrowIfNull(texture);
		ArgumentNullException.ThrowIfNull(shader);
		VoxelSunSettings sun = worldLighting.Sun;
		shader.SetUniform("uTexture", 0);
		shader.SetUniform("uAlphaCutoff", 0.1f);
		shader.SetUniform("uBlockLight", light.BlockLight);
		shader.SetUniform("uSkyLight", light.SkyLight);
		shader.SetUniform("LightDirection", sun.Direction);
		shader.SetUniform("AmbientLight", sun.AmbientLight);
		shader.SetUniform("SunColor", ColorSpace.SrgbToLinear(sun.Color));
		shader.SetUniform("SunIntensity", sun.Intensity);
		shader.SetUniform("uShadowEnabled", 0);
		using IDisposable shadowScope = worldLighting.Shadows?.Bind(shader, 1);

		Dictionary<string, Matrix4x4> transforms = BuildTransforms(rootTransform, pose);
		using IDisposable stateScope = pass.PushState(pass.State with
		{
			Winding = winding,
		});
		foreach (ModelPart part in parts)
		{
			part.Mesh.DefaultColor = ColorSpace.SrgbToLinearColor(tint);
			using IDisposable modelScope = pass.PushModel(transforms[part.Source.Name]);
			pass.DrawMesh(part.Mesh, texture, shader);
		}
	}

	public void RenderShadow(
		RenderPass pass,
		Matrix4x4 rootTransform,
		EntityModelPose pose,
		Texture texture,
		ShaderProgram shader)
	{
		ThrowIfDisposed();
		ArgumentNullException.ThrowIfNull(pass);
		ArgumentNullException.ThrowIfNull(pose);
		ArgumentNullException.ThrowIfNull(texture);
		ArgumentNullException.ThrowIfNull(shader);
		shader.SetUniform("uTexture", 0);
		shader.SetUniform("uAlphaCutoff", 0.1f);
		Dictionary<string, Matrix4x4> transforms = BuildTransforms(rootTransform, pose);
		using IDisposable stateScope = pass.PushState(pass.State with
		{
			Winding = winding,
			CullMode = CullMode.Back,
		});

		foreach (ModelPart part in parts)
		{
			using IDisposable modelScope = pass.PushModel(transforms[part.Source.Name]);
			pass.DrawMesh(part.Mesh, texture, shader);
		}
	}

	public Matrix4x4 GetPartTransform(
		string partName,
		Matrix4x4 rootTransform,
		EntityModelPose pose
	)
	{
		ThrowIfDisposed();
		ArgumentException.ThrowIfNullOrWhiteSpace(partName);
		ArgumentNullException.ThrowIfNull(pose);
		if (!partsByName.ContainsKey(partName))
		{
			throw new KeyNotFoundException($"Entity model part '{partName}' was not found.");
		}
		return BuildTransforms(rootTransform, pose)[partName];
	}

	public EntityRenderBounds CalculateBounds(Matrix4x4 rootTransform, EntityModelPose pose)
	{
		ThrowIfDisposed();
		ArgumentNullException.ThrowIfNull(pose);
		Dictionary<string, Matrix4x4> transforms = BuildTransforms(rootTransform, pose);
		EntityRenderBounds bounds = EntityRenderBounds.Empty;
		foreach (ModelPart part in parts)
		{
			Matrix4x4 transform = transforms[part.Source.Name];
			foreach (Triangle3 triangle in part.Source.Triangles)
			{
				bounds = bounds.Include(Vector3.Transform(triangle.A, transform));
				bounds = bounds.Include(Vector3.Transform(triangle.B, transform));
				bounds = bounds.Include(Vector3.Transform(triangle.C, transform));
			}
		}
		return bounds;
	}

	public bool TryIntersect(
		in Ray3 ray,
		Matrix4x4 rootTransform,
		EntityModelPose pose,
		out EntityModelHit hit
	)
	{
		ThrowIfDisposed();
		ArgumentNullException.ThrowIfNull(pose);
		Ray3 worldRay = ray.Normalized();
		Dictionary<string, Matrix4x4> transforms = BuildTransforms(rootTransform, pose);
		bool found = false;
		float closest = float.PositiveInfinity;
		hit = default;

		foreach (ModelPart part in parts)
		{
			Matrix4x4 transform = transforms[part.Source.Name];
			if (!Matrix4x4.Invert(transform, out Matrix4x4 inverse))
			{
				continue;
			}

			Ray3 localRay = new(
				Vector3.Transform(worldRay.Origin, inverse),
				Vector3.TransformNormal(worldRay.Direction, inverse)
			);
			if (!TriangleIntersection.TryIntersectClosest(
				localRay,
				part.Source.Triangles,
				out TriangleHit localHit
			))
			{
				continue;
			}

			Vector3 position = Vector3.Transform(localHit.Position, transform);
			float distance = Vector3.Distance(worldRay.Origin, position);
			if (distance >= closest)
			{
				continue;
			}

			Matrix4x4 normalTransform = Matrix4x4.Transpose(inverse);
			Vector3 normal = Vector3.Normalize(
				Vector3.TransformNormal(localHit.Normal, normalTransform)
			);
			closest = distance;
			hit = EntityModelHit.From(
				part.Source.Name,
				new TriangleHit(distance, position, normal)
			);
			found = true;
		}

		return found;
	}

	public void Dispose()
	{
		if (disposed)
		{
			return;
		}

		disposed = true;
		foreach (ModelPart part in parts)
		{
			part.Mesh.Dispose();
		}
		parts.Clear();
		partsByName.Clear();
	}

	private Dictionary<string, Matrix4x4> BuildTransforms(
		Matrix4x4 rootTransform,
		EntityModelPose pose
	)
	{
		Dictionary<string, Matrix4x4> combined = new(StringComparer.Ordinal);
		Dictionary<string, Matrix4x4> world = new(StringComparer.Ordinal);
		foreach (ModelPart part in parts)
		{
			world[part.Source.Name] = GetCombined(part, pose, combined) * rootTransform;
		}
		return world;
	}

	private Matrix4x4 GetCombined(
		ModelPart part,
		EntityModelPose pose,
		Dictionary<string, Matrix4x4> cache
	)
	{
		if (cache.TryGetValue(part.Source.Name, out Matrix4x4 value))
		{
			return value;
		}

		EntityPartPose animated = pose[part.Source.Name];
		Vector3 baseRotation = part.Source.BaseRotationDegrees;
		Vector3 degrees = new(
			baseRotation.X - animated.RotationDegrees.X,
			baseRotation.Y + animated.RotationDegrees.Y,
			baseRotation.Z + animated.RotationDegrees.Z
		);
		Matrix4x4 rotation = Matrix4x4.CreateRotationX(degrees.X * DegreesToRadians)
			* Matrix4x4.CreateRotationY(degrees.Y * DegreesToRadians)
			* Matrix4x4.CreateRotationZ(degrees.Z * DegreesToRadians);
		Matrix4x4 local = Matrix4x4.CreateTranslation(-part.Source.Pivot)
			* rotation
			* Matrix4x4.CreateTranslation(part.Source.Pivot)
			* Matrix4x4.CreateTranslation(animated.PositionOffset);

		if (part.Source.ParentName is not null)
		{
			local *= GetCombined(partsByName[part.Source.ParentName], pose, cache);
		}

		cache.Add(part.Source.Name, local);
		return local;
	}

	private void ThrowIfDisposed()
	{
		ObjectDisposedException.ThrowIf(disposed, this);
	}

	internal static Vector3[] CreateFlatNormals(
		EntityModelPartSource source,
		Winding winding)
	{
		Vector3[] normals = new Vector3[source.Vertices.Length];

		for (int vertex = 0; vertex < source.Vertices.Length; vertex += 3)
		{
			Vector3 a = source.Vertices[vertex].Position;
			Vector3 b = source.Vertices[vertex + 1].Position;
			Vector3 c = source.Vertices[vertex + 2].Position;
			Vector3 cross = winding == Winding.CounterClockwise
				? Vector3.Cross(b - a, c - a)
				: Vector3.Cross(c - a, b - a);
			Vector3 normal = cross.LengthSquared() > 0.000001f
				? Vector3.Normalize(cross)
				: Vector3.UnitY;
			normals[vertex] = normal;
			normals[vertex + 1] = normal;
			normals[vertex + 2] = normal;
		}

		return normals;
	}

	private sealed record ModelPart(EntityModelPartSource Source, Mesh3D Mesh);
}
#endif
