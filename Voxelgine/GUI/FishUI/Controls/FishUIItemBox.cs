using FishUI;
using FishUI.Controls;
using Raylib_cs;
using System.Numerics;
using Voxelgine.Engine;

namespace Voxelgine.GUI {
    /// <summary>
    /// FishUI-based item box control for inventory display.
    /// Displays an icon with optional count text in the corner.
    /// Supports atlas regions via UV coordinates.
    /// </summary>
    public class FishUIItemBox : Control {
        public bool IsSelected { get; set; }
        public FishUIInventory ParentInventory { get; set; }
        public InventoryItem Item { get; set; }
        public bool UpdateTextFromItem { get; set; }
        public string Text { get; set; }

        private ImageRef _icon;
        private ImageRef _backgroundNormal;
        private ImageRef _backgroundSelected;
        private ImageRef _backgroundHover;
        private ImageRef _backgroundPressed;
        private float _iconScale = 2.0f;
        private bool _hasIcon;

        // Atlas region support
        private bool _useAtlasRegion;
        private Vector2 _uvPos;
        private Vector2 _uvSize;
        private Texture2D _atlasTexture;

        public event Action<FishUIItemBox> OnItemClicked;

        public FishUIItemBox() {
            Size = new Vector2(64, 64);
            Focusable = true;
        }

        public void LoadTextures(global::FishUI.FishUI ui) {
            var gfx = ui.Graphics;
            _backgroundNormal = gfx.LoadImage("data/textures/gui/itembox.png");
            _backgroundSelected = gfx.LoadImage("data/textures/gui/itembox_sel.png");
            _backgroundHover = gfx.LoadImage("data/textures/gui/itembox_hover.png");
            _backgroundPressed = gfx.LoadImage("data/textures/gui/itembox_pressed.png");
            gfx.SetImageFilter(_backgroundNormal, true);
            gfx.SetImageFilter(_backgroundSelected, true);
            gfx.SetImageFilter(_backgroundHover, true);
            gfx.SetImageFilter(_backgroundPressed, true);
        }

        public void SetIcon(global::FishUI.FishUI ui, string texturePath, float scale) {
            _icon = ui.Graphics.LoadImage(texturePath);
            ui.Graphics.SetImageFilter(_icon, true);
            _iconScale = scale;
            _hasIcon = true;
            _useAtlasRegion = false;
        }

        public void SetIcon(global::FishUI.FishUI ui, ImageRef icon, float scale) {
            _icon = icon;
            ui.Graphics.SetImageFilter(_icon, true);
            _iconScale = scale;
            _hasIcon = true;
            _useAtlasRegion = false;
        }

        /// <summary>
        /// Sets an icon from an atlas texture with UV coordinates.
        /// </summary>
        /// <param name="atlasTexture">The atlas texture</param>
        /// <param name="scale">Scale factor for drawing</param>
        /// <param name="uvPos">UV position (normalized 0-1)</param>
        /// <param name="uvSize">UV size (normalized 0-1)</param>
        public void SetIcon(Texture2D atlasTexture, float scale, Vector2 uvPos, Vector2 uvSize) {
            _atlasTexture = atlasTexture;
            _iconScale = scale;
            _uvPos = uvPos;
            _uvSize = uvSize;
            _hasIcon = true;
            _useAtlasRegion = true;
            Raylib.SetTextureFilter(atlasTexture, TextureFilter.Point);
        }

        public void SetItem(FishUIInventory parent, InventoryItem item) {
            ParentInventory = parent;
            Item = item;
        }

        public override void DrawControl(global::FishUI.FishUI UI, float Dt, float Time) {
            var pos = GetAbsolutePosition();
            var size = GetAbsoluteSize();
            var gfx = UI.Graphics;

            // Check if mouse is over this control
            var mousePos = UI.Input.GetMousePosition();
            bool isHovered = mousePos.X >= pos.X && mousePos.X <= pos.X + size.X &&
                             mousePos.Y >= pos.Y && mousePos.Y <= pos.Y + size.Y;

            // Determine which background to use based on state
            ImageRef bgImage;
            if (IsMousePressed && _backgroundPressed.Userdata != null) {
                bgImage = _backgroundPressed;
            } else if (IsSelected && _backgroundSelected.Userdata != null) {
                bgImage = _backgroundSelected;
            } else if (isHovered && _backgroundHover.Userdata != null) {
                bgImage = _backgroundHover;
            } else {
                bgImage = _backgroundNormal;
            }

            // Draw background
            if (bgImage.Userdata != null) {
                gfx.DrawImage(bgImage, pos, size, 0, 1, FishColor.White);
            } else {
                // Fallback rectangle
                var color = IsSelected ? new FishColor(100, 150, 200, 255) : new FishColor(60, 60, 60, 255);
                gfx.DrawRectangle(pos, size, color);
                gfx.DrawRectangleOutline(pos, size, FishColor.White);
            }

            // Draw icon
            if (_hasIcon) {
                float tint = IsMousePressed ? 0.7f : 1.0f;
                byte tintByte = (byte)(255 * tint);
                var tintColor = new Color(tintByte, tintByte, tintByte, (byte)255);

                if (_useAtlasRegion && _atlasTexture.Id != 0) {
                    // Draw from atlas using UV coordinates
                    float srcX = _uvPos.X * _atlasTexture.Width;
                    float srcY = _uvPos.Y * _atlasTexture.Height;
                    float srcW = _uvSize.X * _atlasTexture.Width;
                    float srcH = _uvSize.Y * _atlasTexture.Height;

                    float iconDrawSize = Math.Min(size.X, size.Y) * 0.75f;
                    var iconPos = pos + (size - new Vector2(iconDrawSize)) / 2;

                    Rectangle source = new Rectangle(srcX, srcY, srcW, srcH);
                    Rectangle dest = new Rectangle(iconPos.X, iconPos.Y, iconDrawSize, iconDrawSize);
                    Raylib.DrawTexturePro(_atlasTexture, source, dest, Vector2.Zero, 0, tintColor);
                } else if (_icon.Userdata != null) {
                    // Draw regular image
                    float iconDrawSize = Math.Min(size.X, size.Y) * 0.7f;
                    var iconPos = pos + (size - new Vector2(iconDrawSize)) / 2;
                    var fishColor = new FishColor(tintColor.R, tintColor.G, tintColor.B, tintColor.A);
                    gfx.DrawImage(_icon, iconPos, new Vector2(iconDrawSize), 0, 1, fishColor);
                }
            }

            // Update text from item if needed
            if (UpdateTextFromItem) {
                Text = Item?.GetInvText();
            }

            // Draw count text in bottom-right corner
            if (!string.IsNullOrEmpty(Text)) {
                var font = UI.Settings.FontDefault;
                var textSize = gfx.MeasureText(font, Text);
                var textPos = pos + size - textSize - new Vector2(4, 2);

                // Draw outline
                for (int ox = -1; ox <= 1; ox++) {
                    for (int oy = -1; oy <= 1; oy++) {
                        if (ox != 0 || oy != 0) {
                            gfx.DrawTextColor(font, Text, textPos + new Vector2(ox, oy), FishColor.Black);
                        }
                    }
                }
                gfx.DrawTextColor(font, Text, textPos, FishColor.White);
            }
        }

        public override void HandleMouseClick(global::FishUI.FishUI UI, FishInputState InputState, FishMouseButton Btn, Vector2 LocalPos) {
            base.HandleMouseClick(UI, InputState, Btn, LocalPos);
            if (Btn == FishMouseButton.Left) {
                OnItemClicked?.Invoke(this);
            }
        }
    }
}
