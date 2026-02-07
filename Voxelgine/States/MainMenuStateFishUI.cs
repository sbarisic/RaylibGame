using FishUI;
using FishUI.Controls;
using Raylib_cs;
using System.Numerics;
using Voxelgine;
using Voxelgine.Engine;
using Voxelgine.Engine.DI;
using Voxelgine.GUI;

namespace Voxelgine.States
{
	/// <summary>
	/// Main menu state using FishUI for the GUI.
	/// </summary>
	public class MainMenuStateFishUI : GameStateImpl
	{
		private FishUIManager _gui;
		private Window _mainWindow;
		private Window _optionsWindow;
		private Window _connectWindow;
		private ImageBox _titleLogo;
		private float _totalTime;
		private IFishLogging Logging;

		public MainMenuStateFishUI(IGameWindow window, IFishEngineRunner Eng) : base(window, Eng)
		{
			Logging = Eng.DI.GetRequiredService<IFishLogging>();
			_gui = new FishUIManager(window, Logging);

			CreateTitleLogo();
			CreateMainMenu();
			CreateOptionsWindow();
			CreateConnectWindow();
		}

		private void CreateTitleLogo()
		{
			// Load and display game title logo at top of screen
			var logoImage = _gui.UI.Graphics.LoadImage("data/textures/title.png");

			if (logoImage.Userdata != null)
			{
				// Use full image size (no scaling)
				var logoSize = new Vector2(logoImage.Width, logoImage.Height) * 10;
				var logoPos = new Vector2(
					(Window.Width / 2f) - (logoSize.X / 2f),
					Window.Height * 0.05f
				);

				_titleLogo = new ImageBox
				{
					Position = logoPos,
					Size = logoSize,
					ScaleMode = ImageScaleMode.Stretch,
					FilterMode = ImageFilterMode.Pixelated
				};
				_titleLogo.Image = logoImage;
				_gui.AddControl(_titleLogo);
			}
		}

		private void CreateMainMenu()
		{
			// Calculate centered position
			var windowSize = new Vector2(320, 400);
			var windowPos = new Vector2(
				(Window.Width / 2f) - (windowSize.X / 2f),
				(Window.Height / 1.65f) - (windowSize.Y / 2f)
			);

			// Create main menu window
			_mainWindow = new Window
			{
				Title = "Main Menu",
				Position = windowPos,
				Size = windowSize,
				IsResizable = false,
				ShowCloseButton = false
			};
			_gui.AddControl(_mainWindow);

			// Create ScrollablePane for buttons
			var scrollPane = new ScrollablePane();
			scrollPane.Position = new Vector2(0, 0);
			scrollPane.Size = _mainWindow.GetContentSize();
			scrollPane.Anchor = FishUIAnchor.All;
			_mainWindow.AddChild(scrollPane);

			// Button layout settings
			float buttonHeight = 50f;
			float buttonSpacing = 10f;
			float margin = 20f;
			float contentWidth = windowSize.X - 60; // Account for window borders and scrollbar

			// New Game button
			var btnNewGame = new Button();
			btnNewGame.Text = "New Game";
			btnNewGame.Position = new Vector2(margin, margin);
			btnNewGame.Size = new Vector2(contentWidth, buttonHeight);
			btnNewGame.TooltipText = "Start a new game";
			btnNewGame.OnButtonPressed += (sender, mbtn, pos) =>
			{
				Eng.DI.GetRequiredService<IGameWindow>().SetState(Eng.GameState);
			};
			scrollPane.AddChild(btnNewGame);

			// Multiplayer button
			var btnMultiplayer = new Button();
			btnMultiplayer.Text = "Multiplayer";
			btnMultiplayer.Position = new Vector2(margin, margin + (buttonHeight + buttonSpacing) * 1);
			btnMultiplayer.Size = new Vector2(contentWidth, buttonHeight);
			btnMultiplayer.TooltipText = "Connect to a multiplayer server";
			btnMultiplayer.OnButtonPressed += (sender, mbtn, pos) =>
			{
				_connectWindow.Visible = true;
				_connectWindow.BringToFront();
			};
			scrollPane.AddChild(btnMultiplayer);

			// NPC Preview button
			var btnNPCPreview = new Button();
			btnNPCPreview.Text = "NPC Preview";
			btnNPCPreview.Position = new Vector2(margin, margin + (buttonHeight + buttonSpacing) * 2);
			btnNPCPreview.Size = new Vector2(contentWidth, buttonHeight);
			btnNPCPreview.TooltipText = "Preview NPC models and animations";
			btnNPCPreview.OnButtonPressed += (sender, mbtn, pos) =>
			{
				Eng.DI.GetRequiredService<IGameWindow>().SetState(Eng.NPCPreviewState);
			};
			scrollPane.AddChild(btnNPCPreview);

			// Options button
			var btnOptions = new Button();
			btnOptions.Text = "Options";
			btnOptions.Position = new Vector2(margin, margin + (buttonHeight + buttonSpacing) * 3);
			btnOptions.Size = new Vector2(contentWidth, buttonHeight);
			btnOptions.TooltipText = "Configure game settings";
			btnOptions.OnButtonPressed += (sender, mbtn, pos) =>
			{
				_optionsWindow.Visible = true;
				_optionsWindow.BringToFront();
			};
			scrollPane.AddChild(btnOptions);

			// Quit button
			var btnQuit = new Button();
			btnQuit.Text = "Quit";
			btnQuit.Position = new Vector2(margin, margin + (buttonHeight + buttonSpacing) * 4);
			btnQuit.Size = new Vector2(contentWidth, buttonHeight);
			btnQuit.TooltipText = "Exit the game";
			btnQuit.OnButtonPressed += (sender, mbtn, pos) =>
			{
				Eng.DI.GetRequiredService<IGameWindow>().Close();
			};
			scrollPane.AddChild(btnQuit);
		}

		private void CreateOptionsWindow()
		{
			var windowSize = new Vector2(500, 600);
			var windowPos = new Vector2(
				(Window.Width / 2f) - (windowSize.X / 2f),
				(Window.Height / 2f) - (windowSize.Y / 2f)
			);

			_optionsWindow = new Window
			{
				Title = "Options",
				Position = windowPos,
				Size = windowSize,
				IsResizable = true,
				ShowCloseButton = true,
				Visible = false
			};

			_optionsWindow.OnClosed += (window) =>
			{
				_optionsWindow.Visible = false;
			};

			// Create scrollable content for options
			var scrollPane = new ScrollablePane
			{
				Position = new Vector2(10, 10),
				Size = new Vector2(windowSize.X - 40, windowSize.Y - 100),
				AutoContentSize = true
			};

			var optionsStack = new StackLayout
			{
				Orientation = StackOrientation.Vertical,
				Spacing = 10,
				Position = Vector2.Zero,
				Size = new Vector2(windowSize.X - 60, 800),
				IsTransparent = true
			};

			// Add config options
			var configVars = Eng.DI.GetRequiredService<GameConfig>().GetVariables().ToArray();
			foreach (var varRef in configVars)
			{
				var label = new Label
				{
					Text = varRef.FieldName,
					Size = new Vector2(200, 24)
				};

				var textBox = new Textbox
				{
					Text = varRef.GetValueString(),
					Size = new Vector2(windowSize.X - 80, 28),
					ID = $"opt_{varRef.FieldName}"
				};

				string fieldName = varRef.FieldName;
				textBox.OnTextChanged += (sender, newText) =>
				{
					try
					{
						var currentVar = Eng.DI.GetRequiredService<GameConfig>().GetVariables().FirstOrDefault(v => v.FieldName == fieldName);
						if (currentVar != null)
						{
							currentVar.SetValueString(newText);
						}
					}
					catch (Exception ex)
					{
						Logging.WriteLine($"Error setting {fieldName}: {ex.Message}");
					}
				};

				optionsStack.AddChild(label);
				optionsStack.AddChild(textBox);
			}

			// Add buttons at the bottom
			var btnReset = new Button
			{
				Text = "Reset Config",
				Size = new Vector2(150, 40)
			};
			btnReset.Clicked += (sender, args) =>
			{
				Eng.DI.GetRequiredService<GameConfig>().SetDefaults();
				Eng.DI.GetRequiredService<GameConfig>().GenerateDefaultKeybinds();
				Eng.DI.GetRequiredService<GameConfig>().SaveToJson();
				// Refresh text boxes
				RefreshOptionsValues();
			};

			var btnSave = new Button
			{
				Text = "Save & Restart",
				Size = new Vector2(150, 40)
			};
			btnSave.Clicked += (sender, args) =>
			{
				Eng.DI.GetRequiredService<GameConfig>().SaveToJson();
				Voxelgine.Utils.RestartGame();
			};

			var btnClose = new Button
			{
				Text = "Close",
				Size = new Vector2(150, 40)
			};
			btnClose.Clicked += (sender, args) =>
			{
				_optionsWindow.Visible = false;
			};

			optionsStack.AddChild(btnReset);
			optionsStack.AddChild(btnSave);
			optionsStack.AddChild(btnClose);

			scrollPane.AddChild(optionsStack);
			_optionsWindow.AddChild(scrollPane);
			_gui.AddControl(_optionsWindow);
		}

		private void CreateConnectWindow()
		{
			var windowSize = new Vector2(360, 300);
			var windowPos = new Vector2(
				(Window.Width / 2f) - (windowSize.X / 2f),
				(Window.Height / 2f) - (windowSize.Y / 2f)
			);

			_connectWindow = new Window
			{
				Title = "Connect to Server",
				Position = windowPos,
				Size = windowSize,
				IsResizable = false,
				ShowCloseButton = true,
				Visible = false
			};

			_connectWindow.OnClosed += (window) =>
			{
				_connectWindow.Visible = false;
			};

			float labelW = 100f;
			float inputW = windowSize.X - labelW - 50f;
			float rowH = 28f;
			float rowSpacing = 12f;
			float marginX = 15f;
			float startY = 15f;

			// Server IP label + input
			var lblHost = new Label
			{
				Text = "Server IP:",
				Position = new Vector2(marginX, startY),
				Size = new Vector2(labelW, rowH)
			};
			_connectWindow.AddChild(lblHost);

			var txtHost = new Textbox
			{
				Text = "127.0.0.1",
				Position = new Vector2(marginX + labelW, startY),
				Size = new Vector2(inputW, rowH),
				ID = "connect_host"
			};
			_connectWindow.AddChild(txtHost);

			// Port label + input
			float row2Y = startY + rowH + rowSpacing;
			var lblPort = new Label
			{
				Text = "Port:",
				Position = new Vector2(marginX, row2Y),
				Size = new Vector2(labelW, rowH)
			};
			_connectWindow.AddChild(lblPort);

			var txtPort = new Textbox
			{
				Text = "7777",
				Position = new Vector2(marginX + labelW, row2Y),
				Size = new Vector2(inputW, rowH),
				ID = "connect_port"
			};
			_connectWindow.AddChild(txtPort);

			// Player name label + input
			float row3Y = row2Y + rowH + rowSpacing;
			var lblName = new Label
			{
				Text = "Name:",
				Position = new Vector2(marginX, row3Y),
				Size = new Vector2(labelW, rowH)
			};
			_connectWindow.AddChild(lblName);

			var txtName = new Textbox
			{
				Text = "Player",
				Position = new Vector2(marginX + labelW, row3Y),
				Size = new Vector2(inputW, rowH),
				ID = "connect_name"
			};
			_connectWindow.AddChild(txtName);

			// Status label
			float statusY = row3Y + rowH + rowSpacing * 2;
			var lblStatus = new Label
			{
				Text = "",
				Position = new Vector2(marginX, statusY),
				Size = new Vector2(windowSize.X - marginX * 2 - 20, rowH),
				ID = "connect_status"
			};
			_connectWindow.AddChild(lblStatus);

			// Connect button
			float btnY = statusY + rowH + rowSpacing;
			float btnW = 120f;
			float btnH = 40f;
			float btnSpacing = 15f;
			float totalBtnW = btnW * 2 + btnSpacing;
			float btnStartX = (windowSize.X - totalBtnW - 20) / 2f;

			var btnConnect = new Button
			{
				Text = "Connect",
				Position = new Vector2(btnStartX, btnY),
				Size = new Vector2(btnW, btnH),
				ID = "connect_btn"
			};
			btnConnect.OnButtonPressed += (sender, mbtn, pos) =>
			{
				var hostBox = _gui.FindControl<Textbox>("connect_host");
				var portBox = _gui.FindControl<Textbox>("connect_port");
				var nameBox = _gui.FindControl<Textbox>("connect_name");
				var statusLabel = _gui.FindControl<Label>("connect_status");

				string host = hostBox?.Text?.Trim() ?? "127.0.0.1";
				string portStr = portBox?.Text?.Trim() ?? "7777";
				string playerName = nameBox?.Text?.Trim() ?? "Player";

				if (string.IsNullOrEmpty(host))
				{
					if (statusLabel != null) statusLabel.Text = "Error: Server IP is required";
					return;
				}

				if (!int.TryParse(portStr, out int port) || port < 1 || port > 65535)
				{
					if (statusLabel != null) statusLabel.Text = "Error: Invalid port (1-65535)";
					return;
				}

				if (string.IsNullOrEmpty(playerName))
				{
					playerName = "Player";
				}

				_connectWindow.Visible = false;

				var mpState = Eng.MultiplayerGameState;
				Eng.DI.GetRequiredService<IGameWindow>().SetState(mpState);
				mpState.Connect(host, port, playerName);
			};
			_connectWindow.AddChild(btnConnect);

			// Cancel button
			var btnCancel = new Button
			{
				Text = "Cancel",
				Position = new Vector2(btnStartX + btnW + btnSpacing, btnY),
				Size = new Vector2(btnW, btnH)
			};
			btnCancel.OnButtonPressed += (sender, mbtn, pos) =>
			{
				_connectWindow.Visible = false;
			};
			_connectWindow.AddChild(btnCancel);

			_gui.AddControl(_connectWindow);
		}

		private void RefreshOptionsValues()
		{
			var configVars = Eng.DI.GetRequiredService<GameConfig>().GetVariables().ToArray();
			foreach (var varRef in configVars)
			{
				var textBox = _gui.FindControl<Textbox>($"opt_{varRef.FieldName}");
				if (textBox != null)
				{
					textBox.Text = varRef.GetValueString();
				}
			}
		}

		public override void SwapTo()
		{
			Raylib.EnableCursor();
		}

		public override void Tick(float GameTime)
		{
			// FishUI handles input in Tick
		}

		public override void Draw2D()
		{
			float deltaTime = Raylib.GetFrameTime();
			_totalTime += deltaTime;

			Raylib.ClearBackground(new Color(150, 150, 150, 255));
			_gui.Tick(deltaTime, _totalTime);
		}

		public override void OnResize(GameWindow window)
		{
			base.OnResize(window);
			_gui.OnResize(window.Width, window.Height);

			// Recenter windows
			var mainSize = _mainWindow.Size;
			_mainWindow.Position = new Vector2(
				(window.Width / 2f) - (mainSize.X / 2f),
				(window.Height / 1.65f) - (mainSize.Y / 2f)
			);

			var optSize = _optionsWindow.Size;
			_optionsWindow.Position = new Vector2(
				(window.Width / 2f) - (optSize.X / 2f),
				(window.Height / 2f) - (optSize.Y / 2f)
			);

			var conSize = _connectWindow.Size;
			_connectWindow.Position = new Vector2(
				(window.Width / 2f) - (conSize.X / 2f),
				(window.Height / 2f) - (conSize.Y / 2f)
			);
		}
	}
}
