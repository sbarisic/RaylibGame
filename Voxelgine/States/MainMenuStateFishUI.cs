using FishUI;
using FishUI.Controls;
using Raylib_cs;
using System.Numerics;
using Voxelgine;
using Voxelgine.Engine;
using Voxelgine.GUI;

namespace RaylibGame.States {
    /// <summary>
    /// Main menu state using FishUI for the GUI.
    /// </summary>
    class MainMenuStateFishUI : GameStateImpl {
        private FishUIManager _gui;
        private Window _mainWindow;
        private Window _optionsWindow;
        private float _totalTime;

        public MainMenuStateFishUI(GameWindow window) : base(window) {
            _gui = new FishUIManager(window);
            
            CreateMainMenu();
            CreateOptionsWindow();
        }

        private void CreateMainMenu() {
            // Calculate centered position
            var windowSize = new Vector2(400, 400);
            var windowPos = new Vector2(
                (Window.Width / 2f) - (windowSize.X / 2f),
                (Window.Height / 1.65f) - (windowSize.Y / 2f)
            );

            // Create main menu window
            _mainWindow = new Window {
                Title = "Main Menu",
                Position = windowPos,
                Size = windowSize,
                IsResizable = false,
                ShowCloseButton = false
            };

            // Create buttons using StackLayout for automatic positioning
            var buttonStack = new StackLayout {
                Orientation = StackOrientation.Vertical,
                Spacing = 8,
                Position = new Vector2(20, 10),
                Size = new Vector2(windowSize.X - 60, windowSize.Y - 80),
                IsTransparent = true
            };

            // New Game button
            var btnNewGame = new Button {
                Text = "New Game",
                Size = new Vector2(windowSize.X - 60, 50)
            };
            btnNewGame.Clicked += (sender, args) => {
                Program.Window.SetState(Program.GameState);
            };

            // Options button
            var btnOptions = new Button {
                Text = "Options",
                Size = new Vector2(windowSize.X - 60, 50)
            };
            btnOptions.Clicked += (sender, args) => {
                _optionsWindow.Visible = true;
                _optionsWindow.BringToFront();
            };

            // Quit button
            var btnQuit = new Button {
                Text = "Quit",
                Size = new Vector2(windowSize.X - 60, 50)
            };
            btnQuit.Clicked += (sender, args) => {
                Program.Window.Close();
            };

            // OS Info button
            var btnInfo = new Button {
                Text = "OS: " + Voxelgine.Utils.GetOSName(),
                Size = new Vector2(windowSize.X - 60, 50)
            };
            btnInfo.Clicked += (sender, args) => {
                Console.WriteLine("Running on {0}", Voxelgine.Utils.GetOSName());
            };

            buttonStack.AddChild(btnNewGame);
            buttonStack.AddChild(btnOptions);
            buttonStack.AddChild(btnQuit);
            buttonStack.AddChild(btnInfo);

            _mainWindow.AddChild(buttonStack);
            _gui.AddControl(_mainWindow);
        }

        private void CreateOptionsWindow() {
            var windowSize = new Vector2(500, 600);
            var windowPos = new Vector2(
                (Window.Width / 2f) - (windowSize.X / 2f),
                (Window.Height / 2f) - (windowSize.Y / 2f)
            );

            _optionsWindow = new Window {
                Title = "Options",
                Position = windowPos,
                Size = windowSize,
                IsResizable = true,
                ShowCloseButton = true,
                Visible = false
            };

            _optionsWindow.OnClosed += (window) => {
                _optionsWindow.Visible = false;
            };

            // Create scrollable content for options
            var scrollPane = new ScrollablePane {
                Position = new Vector2(10, 10),
                Size = new Vector2(windowSize.X - 40, windowSize.Y - 100),
                AutoContentSize = true
            };

            var optionsStack = new StackLayout {
                Orientation = StackOrientation.Vertical,
                Spacing = 10,
                Position = Vector2.Zero,
                Size = new Vector2(windowSize.X - 60, 800),
                IsTransparent = true
            };

            // Add config options
            var configVars = Program.Cfg.GetVariables().ToArray();
            foreach (var varRef in configVars) {
                var label = new Label {
                    Text = varRef.FieldName,
                    Size = new Vector2(200, 24)
                };

                var textBox = new Textbox {
                    Text = varRef.GetValueString(),
                    Size = new Vector2(windowSize.X - 80, 28),
                    ID = $"opt_{varRef.FieldName}"
                };

                string fieldName = varRef.FieldName;
                textBox.OnTextChanged += (sender, newText) => {
                    try {
                        var currentVar = Program.Cfg.GetVariables().FirstOrDefault(v => v.FieldName == fieldName);
                        if (currentVar != null) {
                            currentVar.SetValueString(newText);
                        }
                    } catch (Exception ex) {
                        Console.WriteLine($"Error setting {fieldName}: {ex.Message}");
                    }
                };

                optionsStack.AddChild(label);
                optionsStack.AddChild(textBox);
            }

            // Add buttons at the bottom
            var btnReset = new Button {
                Text = "Reset Config",
                Size = new Vector2(150, 40)
            };
            btnReset.Clicked += (sender, args) => {
                Program.Cfg.SetDefaults();
                Program.Cfg.GenerateDefaultKeybinds();
                Program.Cfg.SaveToJson();
                // Refresh text boxes
                RefreshOptionsValues();
            };

            var btnSave = new Button {
                Text = "Save & Restart",
                Size = new Vector2(150, 40)
            };
            btnSave.Clicked += (sender, args) => {
                Program.Cfg.SaveToJson();
                Voxelgine.Utils.RestartGame();
            };

            var btnClose = new Button {
                Text = "Close",
                Size = new Vector2(150, 40)
            };
            btnClose.Clicked += (sender, args) => {
                _optionsWindow.Visible = false;
            };

            optionsStack.AddChild(btnReset);
            optionsStack.AddChild(btnSave);
            optionsStack.AddChild(btnClose);

            scrollPane.AddChild(optionsStack);
            _optionsWindow.AddChild(scrollPane);
            _gui.AddControl(_optionsWindow);
        }

        private void RefreshOptionsValues() {
            var configVars = Program.Cfg.GetVariables().ToArray();
            foreach (var varRef in configVars) {
                var textBox = _gui.FindControl<Textbox>($"opt_{varRef.FieldName}");
                if (textBox != null) {
                    textBox.Text = varRef.GetValueString();
                }
            }
        }

        public override void SwapTo() {
            Raylib.EnableCursor();
        }

        public override void Tick(float GameTime) {
            // FishUI handles input in Tick
        }

        public override void Draw2D() {
            float deltaTime = Raylib.GetFrameTime();
            _totalTime += deltaTime;

            Raylib.ClearBackground(new Color(150, 150, 150, 255));
            _gui.Tick(deltaTime, _totalTime);
        }

        public override void OnResize(GameWindow window) {
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
