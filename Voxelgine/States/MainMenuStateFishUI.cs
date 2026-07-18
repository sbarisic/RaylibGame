using FishGfx.Graphics;
using FishUI.Controls;
using System.Numerics;
using Voxelgine.Engine;
using Voxelgine.Engine.DI;
using Voxelgine.Engine.Server;
using Voxelgine.FishGfxClient;
using Voxelgine.FishGfxClient.Rendering;
using Voxelgine.GUI;

using Thread = System.Threading.Thread;

namespace Voxelgine.States;

/// <summary>
/// FishUI-backed main menu and modal dialog host.
/// </summary>
public partial class MainMenuStateFishUI : GameStateImpl
{
	private readonly FishUIManager gui;
	private readonly IFishLogging logging;
	private readonly List<Window> modalWindows = new();
	private readonly List<Button> mainButtons = new();

	private Panel mainPanel;
	private StackLayout mainStack;
	private Window optionsWindow;
	private Window connectWindow;
	private Window hostWindow;
	private Window developerWindow;
	private ImageBox titleLogo;
	private Vector2 titleImageSize;
	private ServerLoop hostedServer;
	private Thread hostThread;
	private bool hostedServerStartupPending;
	private int pendingHostPort;
	private string pendingHostPlayerName;
	private long hostedServerStartupTimestamp;

	/// <summary>
	/// The hosted server instance, or null when no local server is running.
	/// </summary>
	public ServerLoop HostedServer => hostedServer;

	internal IReadOnlyList<Button> MainButtons => mainButtons;

	internal IReadOnlyList<Window> ModalWindows => modalWindows;

	internal void ShowAutomaticDialog(string[] args)
	{
		if (args.Contains("--fishgfx-auto-menu-options", StringComparer.OrdinalIgnoreCase))
		{
			ShowOptionsWindow();
		}
		else if (args.Contains("--fishgfx-auto-menu-host", StringComparer.OrdinalIgnoreCase))
		{
			ShowHostWindow();
		}
		else if (args.Contains("--fishgfx-auto-menu-join", StringComparer.OrdinalIgnoreCase))
		{
			ShowConnectWindow();
		}
		else if (args.Contains("--fishgfx-auto-menu-developer", StringComparer.OrdinalIgnoreCase))
		{
			ShowDeveloperWindow();
		}
	}

	public MainMenuStateFishUI(IGameWindow window, IFishEngineRunner engine)
		: base(window, engine)
	{
		logging = engine.DI.GetRequiredService<IFishLogging>();
		gui = new FishUIManager(window, logging);

		CreateTitleLogo();
		CreateMainMenu();
		CreateOptionsWindow();
		CreateConnectWindow();
		CreateHostWindow();
		CreateDeveloperWindow();
		LayoutMenu(window.Width, window.Height);
	}

	private void CreateTitleLogo()
	{
		var logoImage = gui.UI.Graphics.LoadImage("data/textures/title.png");
		if (logoImage.Userdata == null)
		{
			return;
		}

		titleImageSize = new Vector2(logoImage.Width, logoImage.Height);
		titleLogo = new ImageBox(logoImage)
		{
			ID = "menu_title_logo",
			ScaleMode = ImageScaleMode.Fit,
			FilterMode = ImageFilterMode.Pixelated,
		};
		gui.AddControl(titleLogo);
	}

	private void CreateMainMenu()
	{
		mainPanel = new Panel
		{
			ID = "main_menu_panel",
			IsTransparent = true,
			Size = new Vector2(320, Eng.DebugMode ? 310 : 250),
		};

		mainStack = new StackLayout
		{
			ID = "main_menu_stack",
			Orientation = StackOrientation.Vertical,
			Spacing = 10,
			LayoutPadding = 10,
			StretchChildren = true,
			IsTransparent = true,
			Size = mainPanel.Size,
		};
		mainPanel.AddChild(mainStack);

		foreach (string entry in GetMainMenuEntries(Eng.DebugMode))
		{
			switch (entry)
			{
				case "Host Game":
					AddMainButton(entry, "Host a game on this computer", ShowHostWindow);
					break;
				case "Join Game":
					AddMainButton(entry, "Connect to a multiplayer server", ShowConnectWindow);
					break;
				case "Options":
					AddMainButton(entry, "Configure display and controls", ShowOptionsWindow);
					break;
				case "Developer Tools":
					AddMainButton(entry, "Open preview and diagnostics tools", ShowDeveloperWindow);
					break;
				case "Quit":
					AddMainButton(entry, "Exit the game", () => Window.Close());
					break;
			}
		}
		gui.AddControl(mainPanel);
	}

	internal static IReadOnlyList<string> GetMainMenuEntries(bool debugMode)
	{
		return debugMode
			? new[] { "Host Game", "Join Game", "Options", "Developer Tools", "Quit" }
			: new[] { "Host Game", "Join Game", "Options", "Quit" };
	}

	private void AddMainButton(string text, string tooltip, Action action)
	{
		var button = new Button
		{
			ID = $"main_{text.Replace(" ", "_", StringComparison.Ordinal).ToLowerInvariant()}",
			Text = text,
			TooltipText = tooltip,
			Size = new Vector2(300, 48),
		};
		button.OnButtonPressed += (_, _, _) => action();
		mainButtons.Add(button);
		mainStack.AddChild(button);
	}

	private void CreateDeveloperWindow()
	{
		developerWindow = CreateModalWindow("Developer Tools", new Vector2(360, 220));
		float width = developerWindow.GetContentSize().X - 40;
		var npcButton = new Button
		{
			ID = "developer_npc_preview",
			Text = "NPC Preview",
			Position = new Vector2(20, 20),
			Size = new Vector2(width, 44),
		};
		npcButton.OnButtonPressed += (_, _, _) =>
		{
			HideModal(developerWindow);
			Window.SetState(Eng.AsClient().NPCPreviewState);
		};
		developerWindow.AddChild(npcButton);

		var effectsButton = new Button
		{
			ID = "developer_effects_preview",
			Text = "Effects Preview",
			Position = new Vector2(20, 78),
			Size = new Vector2(width, 44),
		};
		effectsButton.OnButtonPressed += (_, _, _) =>
		{
			HideModal(developerWindow);
			Window.SetState(Eng.AsClient().EffectsPreviewState);
		};
		developerWindow.AddChild(effectsButton);

		var closeButton = new Button
		{
			Text = "Close",
			Position = new Vector2(110, 136),
			Size = new Vector2(120, 36),
		};
		closeButton.OnButtonPressed += (_, _, _) => HideModal(developerWindow);
		developerWindow.AddChild(closeButton);
	}

	private Window CreateModalWindow(string title, Vector2 size)
	{
		var window = new Window
		{
			Title = title,
			Size = size,
			IsResizable = false,
			IsModal = true,
			CloseButtonEnabled = true,
			ShowCloseButton = true,
			Visible = false,
		};
		window.OnClosed += HideModal;
		modalWindows.Add(window);
		gui.AddControl(window);
		return window;
	}

	private void ShowModal(Window window)
	{
		foreach (Window candidate in modalWindows)
		{
			if (candidate != window)
			{
				if (candidate == optionsWindow)
				{
					CloseOptionDropDowns();
				}
				candidate.Visible = false;
			}
		}

		mainPanel.Disabled = true;
		CenterWindow(window);
		window.Show();
	}

	private void HideModal(Window window)
	{
		if (window == optionsWindow)
		{
			CloseOptionDropDowns();
		}
		window.Visible = false;
		if (gui.UI.ModalControl == window)
		{
			gui.UI.SetModalControl(null);
		}
		mainPanel.Disabled = modalWindows.Any(candidate => candidate.Visible);
	}

	private void HideAllModals()
	{
		CloseOptionDropDowns();
		foreach (Window window in modalWindows)
		{
			window.Visible = false;
		}
		gui.UI.SetModalControl(null);
		mainPanel.Disabled = false;
	}

	private void CenterWindow(Window window)
	{
		window.Position = new Vector2(
			Math.Max(0, (Window.Width - window.Size.X) / 2f),
			Math.Max(0, (Window.Height - window.Size.Y) / 2f)
		);
	}

	private void LayoutMenu(int width, int height)
	{
		const float panelWidth = 320;
		float panelHeight = Eng.DebugMode ? 310 : 250;
		mainPanel.Size = new Vector2(panelWidth, panelHeight);
		mainStack.Size = mainPanel.Size;

		float logoWidth = Math.Clamp(width * 0.34f, 280, 620);
		float logoHeight = titleImageSize.X > 0
			? logoWidth * titleImageSize.Y / titleImageSize.X
			: 0;
		float top = Math.Max(24, height * 0.06f);

		if (titleLogo != null)
		{
			titleLogo.Size = new Vector2(logoWidth, logoHeight);
			titleLogo.Position = new Vector2((width - logoWidth) / 2f, top);
		}

		float minimumPanelY = top + logoHeight + 24;
		float desiredPanelY = Math.Max(minimumPanelY, height * 0.4f);
		float maximumPanelY = Math.Max(minimumPanelY, height - panelHeight - 24);
		mainPanel.Position = new Vector2(
			(width - panelWidth) / 2f,
			Math.Min(desiredPanelY, maximumPanelY)
		);

		foreach (Window modal in modalWindows)
		{
			CenterWindow(modal);
		}
	}

	/// <summary>
	/// Stops the hosted server if one is running.
	/// </summary>
	public void StopHostedServer()
	{
		ServerLoop server = hostedServer;
		Thread serverThread = hostThread;
		hostedServerStartupPending = false;
		SetHostControlsStarting(false);

		if (server == null)
		{
			return;
		}

		server.Stop();
		if (serverThread is { IsAlive: true }
			&& !serverThread.Join(TimeSpan.FromSeconds(30)))
		{
			logging.Log(
				GameLogLevel.Error,
				"HostedServer",
				"Hosted server did not stop within 30 seconds; resources were left intact to avoid disposing a live server thread."
			);
			return;
		}

		server.Dispose();
		hostedServer = null;
		hostThread = null;
	}

	public override void SwapTo()
	{
		gui.InputEnabled = true;
		IFishGfxGameWindow fishWindow = (IFishGfxGameWindow)Window;
		fishWindow.RenderWindow.CaptureCursor = false;
		fishWindow.RenderWindow.ShowCursor = true;
		HideAllModals();
		StopHostedServer();
	}

	public override void SwapFrom()
	{
		gui.InputEnabled = false;
	}

	public override void Tick(float gameTime)
	{
		UpdateHostedServerStartup();
	}

	public override GameStateRenderSettings GetRenderSettings(Vector2 framebufferSize)
	{
		return GameStateRenderSettings.CreateOverlay(new Vector2(Window.Width, Window.Height));
	}

	public override void BeginInputFrame()
	{
		gui.BeginInputFrame();
	}

	public override void BeginFrame(in FrameTiming timing)
	{
		gui.Update(timing.DeltaTime, timing.TotalTime);
	}

	public override void RenderOverlay(RenderPass pass, in FrameTiming timing)
	{
		gui.Render(pass, timing.DeltaTime, timing.TotalTime);
	}

	public override void OnResize(IGameWindow window)
	{
		base.OnResize(window);
		gui.OnResize(window.Width, window.Height);
		LayoutMenu(window.Width, window.Height);
	}

	public override void Dispose()
	{
		StopHostedServer();
		gui.Dispose();
	}
}
