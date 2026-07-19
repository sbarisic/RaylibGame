#if WINDOWS
using FishGfx.Graphics;

namespace Voxelgine.FishGfxClient.Rendering;

internal sealed class LocalFogGpuTimer : IDisposable
{
	private const int QueryCount = 6;
	private readonly GraphicsQuery[] queries;
	private readonly bool[] pending;
	private int nextQuery;
	private bool disposed;

	internal LocalFogGpuTimer(GraphicsContext graphics)
	{
		ArgumentNullException.ThrowIfNull(graphics);
		queries = new GraphicsQuery[QueryCount];
		pending = new bool[QueryCount];

		for (int index = 0; index < queries.Length; index++)
		{
			queries[index] = graphics.CreateQuery(GraphicsQueryType.TimeElapsed);
		}
	}

	internal bool Enabled { get; set; }

	internal double LastMilliseconds { get; private set; }

	internal IDisposable Begin(RenderPass pass)
	{
		ArgumentNullException.ThrowIfNull(pass);
		Poll();

		if (!Enabled)
		{
			return null;
		}

		for (int attempt = 0; attempt < queries.Length; attempt++)
		{
			int index = (nextQuery + attempt) % queries.Length;

			if (pending[index])
			{
				continue;
			}

			nextQuery = (index + 1) % queries.Length;
			return new QueryScope(pass.BeginQuery(queries[index]), pending, index);
		}

		return null;
	}

	internal void Poll()
	{
		for (int index = 0; index < queries.Length; index++)
		{
			if (!pending[index] || !queries[index].IsResultAvailable)
			{
				continue;
			}

			LastMilliseconds = queries[index].GetResult() / 1_000_000.0;
			pending[index] = false;
		}
	}

	public void Dispose()
	{
		if (disposed)
		{
			return;
		}

		disposed = true;

		foreach (GraphicsQuery query in queries)
		{
			query.Dispose();
		}
	}

	private sealed class QueryScope : IDisposable
	{
		private IDisposable scope;
		private readonly bool[] pending;
		private readonly int index;

		internal QueryScope(IDisposable scope, bool[] pending, int index)
		{
			this.scope = scope;
			this.pending = pending;
			this.index = index;
		}

		public void Dispose()
		{
			IDisposable current = Interlocked.Exchange(ref scope, null);

			if (current is null)
			{
				return;
			}

			current.Dispose();
			pending[index] = true;
		}
	}
}
#endif
