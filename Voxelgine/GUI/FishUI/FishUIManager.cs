using FishUI;
using FishUI.Controls;
using Raylib_cs;
using System.Numerics;
using Voxelgine.Engine;
using Voxelgine.Engine.DI;

namespace Voxelgine.GUI {
    /// <summary>
    /// FishUI-based GUI manager that replaces the old GUIManager.
    /// Provides a unified interface for managing FishUI controls in the game.
    /// </summary>
    public class FishUIManager {
        public FishUI.FishUI UI { get; private set; }
        public FishUISettings Settings { get; private set; }

        private readonly RaylibFishUIGfx _gfx;
        private readonly RaylibFishUIInput _input;
        private readonly RaylibFishUIEvents _events;
        private readonly IFishLogging _logging;

        public int Width => UI.Width;
        public int Height => UI.Height;

        public FishUIManager(IGameWindow window, IFishLogging logging) {
            _logging = logging;
            _gfx = new RaylibFishUIGfx();
            _input = new RaylibFishUIInput();
            _events = new RaylibFishUIEvents(logging);

            Settings = new FishUISettings();
            UI = new FishUI.FishUI(Settings, _gfx, _input, _events, null);
            UI.Width = window.Width;
            UI.Height = window.Height;
            UI.Init();

            // Try to load the GWEN theme if available
            string themePath = "data/themes/gwen.yaml";
            if (File.Exists(themePath)) {
                try {
                    Settings.LoadTheme(themePath, true);
                } catch (Exception ex) {
                    _logging.WriteLine($"[FishUIManager] Failed to load theme: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Updates the UI and processes input. Call once per frame.
        /// </summary>
        public void Tick(float deltaTime, float totalTime) {
            UI.Tick(deltaTime, totalTime);
        }

        /// <summary>
        /// Draws the UI. Should be called in 2D drawing phase.
        /// </summary>
        public void Draw() {
            // FishUI.Tick handles both update and draw
        }

        /// <summary>
        /// Called when window is resized.
        /// </summary>
        public void OnResize(int width, int height) {
            UI.Resized(width, height);
        }

        /// <summary>
        /// Adds a control to the UI.
        /// </summary>
        public void AddControl(Control control) {
            UI.AddControl(control);
        }

        /// <summary>
        /// Removes a control from the UI.
        /// </summary>
        public bool RemoveControl(Control control) {
            return UI.RemoveControl(control);
        }

        /// <summary>
        /// Removes all controls from the UI.
        /// </summary>
        public void Clear() {
            UI.RemoveAllControls();
        }

        /// <summary>
        /// Finds a control by ID.
        /// </summary>
        public T FindControl<T>(string id) where T : Control {
            return UI.FindControlByID<T>(id);
        }

        /// <summary>
        /// Gets the center position of the window.
        /// </summary>
        public Vector2 GetCenter() {
            return new Vector2(Width / 2f, Height / 2f);
        }

        /// <summary>
        /// Converts a relative position (0-1) to window coordinates.
        /// </summary>
        public Vector2 WindowScale(Vector2 relative) {
            return relative * new Vector2(Width, Height);
        }

        /// <summary>
        /// Creates a centered position for a control of the given size.
        /// </summary>
        public Vector2 CenterPosition(Vector2 size) {
            return GetCenter() - size / 2;
        }
    }
}
