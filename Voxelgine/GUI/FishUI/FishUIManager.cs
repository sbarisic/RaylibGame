using FishGfx.FishUI;
using FishGfx.Graphics;
using FishUI;
using FishUI.Controls;
using System.Numerics;
using Voxelgine.Engine;
using Voxelgine.Engine.DI;
using Voxelgine.FishGfxClient;

namespace Voxelgine.GUI;

/// <summary>
/// Owns the FishGfx-backed FishUI context for one game state. Input update and
/// rendering are deliberately separate so drawing always occurs in an active
/// FishGfx render pass.
/// </summary>
public sealed class FishUIManager : IDisposable
{
	private readonly FishUIGraphicsBackend graphics;
	private readonly FishUIInputAdapter input;
	private bool disposed;

	public FishUIManager(IGameWindow window, IFishLogging logging)
	{
		ArgumentNullException.ThrowIfNull(window);
		ArgumentNullException.ThrowIfNull(logging);
		IFishGfxGameWindow fishWindow = window as IFishGfxGameWindow
			?? throw new ArgumentException("FishUI requires the FishGfx client window.", nameof(window));

		Settings = new FishUISettings();
		FishUIGraphicsBackend graphicsBackend = null;
		FishUIInputAdapter inputAdapter = null;
		global::FishUI.FishUI context;
		try
		{
			graphicsBackend = new FishUIGraphicsBackend(
				fishWindow.RenderWindow,
				AppContext.BaseDirectory
			);
			inputAdapter = new FishUIInputAdapter(fishWindow.RenderWindow);
			context = new global::FishUI.FishUI(
				Settings,
				graphicsBackend,
				inputAdapter,
				new GameFishUIEvents(logging),
				graphicsBackend.FileSystem
			)
			{
				Width = window.Width,
				Height = window.Height,
			};
			context.Init();
		}
		catch
		{
			inputAdapter?.Dispose();
			graphicsBackend?.Dispose();
			throw;
		}

		graphics = graphicsBackend;
		input = inputAdapter;
		UI = context;

		string themePath = Path.Combine(AppContext.BaseDirectory, "data", "themes", "gwen.yaml");
		if (File.Exists(themePath))
		{
			try
			{
				Settings.LoadTheme(themePath, true);
			}
			catch (Exception exception)
			{
				logging.WriteLine($"[FishUIManager] Failed to load theme: {exception.Message}");
			}
		}
	}

	public global::FishUI.FishUI UI { get; }

	public FishUISettings Settings { get; }

	public int Width => UI.Width;

	public int Height => UI.Height;

	public bool InputEnabled
	{
		get => input.Enabled;
		set => input.Enabled = value;
	}

	public void BeginInputFrame()
	{
		ObjectDisposedException.ThrowIf(disposed, this);
		input.BeginFrame();
	}

	public void Update(float deltaTime, float totalTime)
	{
		ObjectDisposedException.ThrowIf(disposed, this);
		UI.TickUpdate(deltaTime, totalTime);
	}

	public void Render(RenderPass pass, float deltaTime, float totalTime)
	{
		ObjectDisposedException.ThrowIf(disposed, this);
		ArgumentNullException.ThrowIfNull(pass);
		RenderState state = RenderState.Default with
		{
			CullMode = CullMode.None,
			DepthTestEnabled = false,
			DepthWriteEnabled = false,
			BlendEnabled = true,
		};

		using (graphics.UseRenderPass(pass, pass.View, state))
		{
			UI.TickDraw(deltaTime, totalTime);
		}
	}

	public void OnResize(int width, int height)
	{
		ObjectDisposedException.ThrowIf(disposed, this);
		UI.Resized(width, height);
	}

	public void AddControl(Control control)
	{
		UI.AddControl(control);
	}

	public bool RemoveControl(Control control)
	{
		return UI.RemoveControl(control);
	}

	public void Clear()
	{
		UI.RemoveAllControls();
	}

	public T FindControl<T>(string id) where T : Control
	{
		return UI.FindControlByID<T>(id);
	}

	public Vector2 GetCenter()
	{
		return new Vector2(Width / 2f, Height / 2f);
	}

	public Vector2 WindowScale(Vector2 relative)
	{
		return relative * new Vector2(Width, Height);
	}

	public Vector2 CenterPosition(Vector2 size)
	{
		return GetCenter() - size / 2;
	}

	public void Dispose()
	{
		if (disposed)
		{
			return;
		}

		input.Dispose();
		graphics.Dispose();
		disposed = true;
	}
}
