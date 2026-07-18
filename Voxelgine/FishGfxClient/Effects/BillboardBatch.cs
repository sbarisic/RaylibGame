#if WINDOWS
using FishGfx;
using FishGfx.Graphics;
using FishGfx.Graphics.Drawables;
using System.Numerics;

namespace Voxelgine.FishGfxClient.Effects;

public enum BillboardBlendMode
{
	Alpha,
	PremultipliedAlpha,
	Additive,
	Multiply,
}

public sealed class BillboardBatch : IDisposable
{
	private readonly GraphicsContext graphics;
	private readonly List<Billboard> items = new();
	private readonly List<DrawRun> drawRuns = new();
	private Vector3 cameraPosition;
	private Vector3 cameraRight;
	private Vector3 cameraUp;
	private Vector3 cameraForward;
	private int sequence;
	private bool disposed;

	public BillboardBatch(GraphicsContext graphics)
	{
		this.graphics = graphics ?? throw new ArgumentNullException(nameof(graphics));
	}

	public int Count { get; private set; }

	public void Begin(Vector3 viewPosition, Vector3 viewRight, Vector3 viewUp)
	{
		ObjectDisposedException.ThrowIf(disposed, this);
		cameraPosition = viewPosition;
		cameraRight = NormalizeViewAxis(viewRight, nameof(viewRight));
		cameraUp = NormalizeViewAxis(viewUp, nameof(viewUp));
		cameraForward = Vector3.Cross(cameraUp, cameraRight);
		if (cameraForward.LengthSquared() < 0.000001f)
		{
			throw new ArgumentException("Billboard view axes must not be parallel.");
		}
		cameraForward = Vector3.Normalize(cameraForward);
		Count = 0;
		sequence = 0;
		items.Clear();
	}

	public void Add(
		Texture texture,
		Vector3 position,
		Vector2 size,
		Color color,
		BillboardBlendMode blendMode = BillboardBlendMode.Alpha,
		Vector3? axisUp = null
	)
	{
		ObjectDisposedException.ThrowIf(disposed, this);
		ArgumentNullException.ThrowIfNull(texture);
		if (size.X <= 0 || size.Y <= 0)
		{
			return;
		}

		items.Add(new Billboard(
			new GroupKey(texture, blendMode),
			position,
			size,
			color,
			Vector3.DistanceSquared(position, cameraPosition),
			sequence++,
			axisUp.GetValueOrDefault(),
			axisUp.HasValue));
		Count++;
	}

	public void Flush(RenderPass pass)
	{
		ObjectDisposedException.ThrowIf(disposed, this);
		ArgumentNullException.ThrowIfNull(pass);
		items.Sort(static (left, right) =>
		{
			int depthOrder = right.DistanceSquared.CompareTo(left.DistanceSquared);
			return depthOrder != 0 ? depthOrder : left.Sequence.CompareTo(right.Sequence);
		});

		int runIndex = 0;
		int itemIndex = 0;
		while (itemIndex < items.Count)
		{
			GroupKey key = items[itemIndex].Key;
			int runEnd = itemIndex + 1;
			while (runEnd < items.Count && items[runEnd].Key == key)
			{
				runEnd++;
			}

			DrawRun run = GetDrawRun(runIndex++);
			int vertexCount = (runEnd - itemIndex) * 6;
			if (run.Vertices.Length < vertexCount)
			{
				run.Vertices = new FishGfx.Vertex3[Math.Max(vertexCount, run.Vertices.Length * 2)];
			}
			int destination = 0;
			for (int index = itemIndex; index < runEnd; index++)
			{
				Billboard billboard = items[index];
				GetAxes(billboard, out Vector3 rightAxis, out Vector3 upAxis);
				Vector3 right = rightAxis * billboard.Size.X * 0.5f;
				Vector3 up = upAxis * billboard.Size.Y * 0.5f;
				Vector3 bottomLeft = billboard.Position - right - up;
				Vector3 bottomRight = billboard.Position + right - up;
				Vector3 topRight = billboard.Position + right + up;
				Vector3 topLeft = billboard.Position - right + up;
				run.Vertices[destination++] = new FishGfx.Vertex3(bottomLeft, new Vector2(0, 0), billboard.Color);
				run.Vertices[destination++] = new FishGfx.Vertex3(bottomRight, new Vector2(1, 0), billboard.Color);
				run.Vertices[destination++] = new FishGfx.Vertex3(topRight, new Vector2(1, 1), billboard.Color);
				run.Vertices[destination++] = new FishGfx.Vertex3(bottomLeft, new Vector2(0, 0), billboard.Color);
				run.Vertices[destination++] = new FishGfx.Vertex3(topRight, new Vector2(1, 1), billboard.Color);
				run.Vertices[destination++] = new FishGfx.Vertex3(topLeft, new Vector2(0, 1), billboard.Color);
			}

			run.Mesh.SetVertices(run.Vertices, vertexCount);
			using IDisposable stateScope = pass.PushState(CreateState(pass.State, key.BlendMode));
			pass.DrawMesh(run.Mesh, key.Texture);
			itemIndex = runEnd;
		}
	}

	public void Dispose()
	{
		if (disposed)
		{
			return;
		}

		foreach (DrawRun run in drawRuns)
		{
			run.Mesh.Dispose();
		}
		drawRuns.Clear();
		items.Clear();
		disposed = true;
	}

	private DrawRun GetDrawRun(int index)
	{
		while (drawRuns.Count <= index)
		{
			drawRuns.Add(new DrawRun(graphics.CreateMesh3D(BufferUsage.Stream)));
		}

		return drawRuns[index];
	}

	private void GetAxes(in Billboard billboard, out Vector3 right, out Vector3 up)
	{
		right = cameraRight;
		up = cameraUp;
		if (!billboard.HasAxisUp || billboard.AxisUp.LengthSquared() < 0.000001f)
		{
			return;
		}

		Vector3 projected = billboard.AxisUp
			- cameraForward * Vector3.Dot(billboard.AxisUp, cameraForward);
		if (projected.LengthSquared() < 0.000001f)
		{
			return;
		}

		up = Vector3.Normalize(projected);
		right = Vector3.Normalize(Vector3.Cross(cameraForward, up));
	}

	private static Vector3 NormalizeViewAxis(Vector3 value, string parameterName)
	{
		if (!float.IsFinite(value.X)
			|| !float.IsFinite(value.Y)
			|| !float.IsFinite(value.Z)
			|| value.LengthSquared() < 0.000001f)
		{
			throw new ArgumentException("Billboard view axes must be finite and non-zero.", parameterName);
		}

		return Vector3.Normalize(value);
	}

	private static RenderState CreateState(RenderState source, BillboardBlendMode mode)
	{
		(BlendFactor sourceBlend, BlendFactor destinationBlend) = mode switch
		{
			BillboardBlendMode.Additive => (BlendFactor.SourceAlpha, BlendFactor.One),
			BillboardBlendMode.PremultipliedAlpha => (BlendFactor.One, BlendFactor.OneMinusSourceAlpha),
			BillboardBlendMode.Multiply => (BlendFactor.DestinationColor, BlendFactor.Zero),
			_ => (BlendFactor.SourceAlpha, BlendFactor.OneMinusSourceAlpha),
		};

		return source with
		{
			CullMode = CullMode.None,
			DepthTestEnabled = true,
			DepthWriteEnabled = false,
			BlendEnabled = true,
			SourceBlend = sourceBlend,
			DestinationBlend = destinationBlend,
		};
	}

	private readonly record struct GroupKey(Texture Texture, BillboardBlendMode BlendMode);

	private readonly record struct Billboard(
		GroupKey Key,
		Vector3 Position,
		Vector2 Size,
		Color Color,
		float DistanceSquared,
		int Sequence,
		Vector3 AxisUp,
		bool HasAxisUp
	);

	private sealed class DrawRun
	{
		internal DrawRun(Mesh3D mesh)
		{
			Mesh = mesh;
		}

		internal Mesh3D Mesh { get; }

		internal FishGfx.Vertex3[] Vertices { get; set; } = Array.Empty<FishGfx.Vertex3>();
	}
}
#endif
