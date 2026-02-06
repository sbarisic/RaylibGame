using FishUI;
using FishUI.Controls;
using Raylib_cs;
using System.Numerics;
using Voxelgine;
using Voxelgine.Engine;
using Voxelgine.Engine.DI;
using Voxelgine.GUI;

namespace RaylibGame.States
{
	/// <summary>
	/// Main menu state using FishUI for the GUI.
	/// </summary>
	public class MainMenuStateFishUI : GameStateImpl
	{
		private FishUIManager _gui;
		private Window _mainWindow;
		private Window _optionsWindow;
		private ImageBox _titleLogo;
		private float _totalTime;

		public MainMenuStateFishUI(IGameWindow window, IFishEngineRunner Eng) : base(window, Eng)
		{
			_gui = new FishUIManager(window);

			CreateTitleLogo();
			CreateMainMenu();
			CreateOptionsWindow();
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
			var windowSize = new Vector2(320, 340);
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

			// NPC Preview button
			var btnNPCPreview = new Button();
			btnNPCPreview.Text = "NPC Preview";
			btnNPCPreview.Position = new Vector2(margin, margin + (buttonHeight + buttonSpacing) * 1);
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
			btnOptions.Position = new Vector2(margin, margin + (buttonHeight + buttonSpacing) * 2);
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
			btnQuit.Position = new Vector2(margin, margin + (buttonHeight + buttonSpacing) * 3);
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
						Console.WriteLine($"Error setting {fieldName}: {ex.Message}");
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
		}
	}
}
