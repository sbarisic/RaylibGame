#if WINDOWS
using FishGfx.Formats;
using FishGfx.Graphics;
using FishGfx.Graphics.Drawables;
using System.Collections.Concurrent;

namespace Voxelgine.FishGfxClient.Assets;

public sealed class GameAssetStore : IDisposable
{
	private readonly GraphicsContext graphics;
	private readonly string root;
	private readonly Action<string> log;
	private readonly Dictionary<string, IAssetSlot> assets = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, HashSet<string>> assetsByFile = new(StringComparer.OrdinalIgnoreCase);
	private readonly object assetIndexLock = new();
	private readonly ConcurrentQueue<string> reloadQueue = new();
	private readonly FileSystemWatcher watcher;
	private int graphicsThreadId;
	private bool disposed;

	public GameAssetStore(GraphicsContext graphics, string resourceRoot, Action<string> log = null)
	{
		this.graphics = graphics ?? throw new ArgumentNullException(nameof(graphics));
		root = Path.GetFullPath(resourceRoot ?? AppContext.BaseDirectory);
		this.log = log ?? (_ => { });
		graphicsThreadId = Environment.CurrentManagedThreadId;

		watcher = new FileSystemWatcher(root)
		{
			IncludeSubdirectories = true,
			NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
			EnableRaisingEvents = Directory.Exists(root),
		};
		watcher.Changed += QueueFile;
		watcher.Created += QueueFile;
		watcher.Renamed += QueueRenamedFile;
	}

	public AssetHandle<Texture> LoadTexture(string id, string path, TextureLoadOptions options = null)
	{
		string fullPath = Resolve(path);
		return Register(id, () => graphics.LoadTexture(fullPath, options), fullPath);
	}

	public AssetHandle<TrueTypeFont> LoadFont(string id, string path, TrueTypeFontOptions options = null)
	{
		string fullPath = Resolve(path);
		return Register(id, () => new TrueTypeFont(fullPath, options), fullPath);
	}

	public AssetHandle<ShaderProgram> LoadShader(string id, string vertexPath, string fragmentPath)
	{
		string vertex = Resolve(vertexPath);
		string fragment = Resolve(fragmentPath);

		return Register(
			id,
			() =>
			{
				using ShaderStage vertexStage = graphics.LoadShaderStage(ShaderStageType.Vertex, vertex);
				using ShaderStage fragmentStage = graphics.LoadShaderStage(ShaderStageType.Fragment, fragment);
				return graphics.CreateShaderProgram(vertexStage, fragmentStage);
			},
			vertex,
			fragment
		);
	}

	public AssetHandle<Mesh3D> RegisterMesh(string id, Func<Mesh3D> loader, params string[] sourcePaths)
	{
		return Register(id, loader, sourcePaths.Select(Resolve).ToArray());
	}

	public AssetHandle<T> Register<T>(string id, Func<T> loader, params string[] sourcePaths)
		where T : class
	{
		ThrowIfDisposed();
		EnsureGraphicsThread();
		ArgumentException.ThrowIfNullOrWhiteSpace(id);
		ArgumentNullException.ThrowIfNull(loader);

		if (assets.ContainsKey(id))
		{
			throw new InvalidOperationException($"Asset '{id}' is already registered.");
		}

		T value = loader();
		AssetSlot<T> slot = new(id, value, loader);
		assets.Add(id, slot);

		lock (assetIndexLock)
		{
			foreach (string sourcePath in sourcePaths)
			{
				string fullPath = Path.GetFullPath(sourcePath);
				if (!assetsByFile.TryGetValue(fullPath, out HashSet<string> ids))
				{
					ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
					assetsByFile.Add(fullPath, ids);
				}
				ids.Add(id);
			}
		}

		return new AssetHandle<T>(slot);
	}

	public AssetHandle<T> GetOrRegister<T>(
		string id,
		Func<T> loader,
		params string[] sourcePaths)
		where T : class
	{
		ThrowIfDisposed();
		EnsureGraphicsThread();
		ArgumentException.ThrowIfNullOrWhiteSpace(id);
		ArgumentNullException.ThrowIfNull(loader);

		if (!assets.TryGetValue(id, out IAssetSlot existing))
		{
			return Register(id, loader, sourcePaths);
		}

		if (existing is AssetSlot<T> typed)
		{
			return new AssetHandle<T>(typed);
		}

		throw new InvalidOperationException(
			$"Asset '{id}' is already registered with a different resource type."
		);
	}

	public int ProcessQueuedReloads()
	{
		ThrowIfDisposed();
		EnsureGraphicsThread();
		HashSet<string> pending = new(StringComparer.OrdinalIgnoreCase);
		while (reloadQueue.TryDequeue(out string id))
		{
			pending.Add(id);
		}

		int successful = 0;
		foreach (string id in pending)
		{
			if (!assets.TryGetValue(id, out IAssetSlot slot))
			{
				continue;
			}

			try
			{
				slot.ReloadAndSwap();
				successful++;
			}
			catch (Exception ex)
			{
				log($"[Assets] Reload of '{id}' failed; retaining the previous resource: {ex.Message}");
			}
		}

		return successful;
	}

	public void Dispose()
	{
		if (disposed)
		{
			return;
		}

		EnsureGraphicsThread();
		watcher.EnableRaisingEvents = false;
		watcher.Dispose();
		foreach (IAssetSlot asset in assets.Values.Reverse())
		{
			asset.Dispose();
		}
		assets.Clear();
		lock (assetIndexLock)
		{
			assetsByFile.Clear();
		}
		disposed = true;
	}

	private void QueueFile(object sender, FileSystemEventArgs args)
	{
		QueuePath(args.FullPath);
	}

	private void QueueRenamedFile(object sender, RenamedEventArgs args)
	{
		QueuePath(args.OldFullPath);
		QueuePath(args.FullPath);
	}

	private void QueuePath(string path)
	{
		string fullPath = Path.GetFullPath(path);
		string[] ids;
		lock (assetIndexLock)
		{
			if (!assetsByFile.TryGetValue(fullPath, out HashSet<string> registeredIds))
			{
				return;
			}
			ids = registeredIds.ToArray();
		}

		foreach (string id in ids)
		{
			reloadQueue.Enqueue(id);
		}
	}

	private string Resolve(string path)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);
		return Path.IsPathRooted(path) ? Path.GetFullPath(path) : Path.GetFullPath(path, root);
	}

	private void EnsureGraphicsThread()
	{
		if (Environment.CurrentManagedThreadId != graphicsThreadId)
		{
			throw new InvalidOperationException("GPU assets must be created, reloaded, and disposed on the graphics thread.");
		}
	}

	private void ThrowIfDisposed()
	{
		ObjectDisposedException.ThrowIf(disposed, this);
	}
}
#endif
