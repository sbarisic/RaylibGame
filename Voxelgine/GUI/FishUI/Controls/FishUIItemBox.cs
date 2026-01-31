using FishUI;
using FishUI.Controls;
using Raylib_cs;
using System.Numerics;
using Voxelgine.Engine;

namespace Voxelgine.GUI {
    /// <summary>
    /// FishUI-based item box control for inventory display.
    /// Displays an icon with optional count text in the corner.
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
        private float _iconScale = 2.0f;
        private bool _hasIcon;

        public event Action<FishUIItemBox> OnItemClicked;

        public FishUIItemBox() {
            Size = new Vector2(64, 64);
            Focusable = true;
        }

        public void LoadTextures(global::FishUI.FishUI ui) {
            var gfx = ui.Graphics;
            _backgroundNormal = gfx.LoadImage("data/gui/itembox.png");
            _backgroundSelected = gfx.LoadImage("data/gui/itembox_sel.png");
            gfx.SetImageFilter(_backgroundNormal, true);
            gfx.SetImageFilter(_backgroundSelected, true);
        }

        public void SetIcon(global::FishUI.FishUI ui, string texturePath, float scale) {
            _icon = ui.Graphics.LoadImage(texturePath);
            ui.Graphics.SetImageFilter(_icon, true);
            _iconScale = scale;
            _hasIcon = true;
        }

        public void SetIcon(global::FishUI.FishUI ui, ImageRef icon, float scale) {
            _icon = icon;
            ui.Graphics.SetImageFilter(_icon, true);
            _iconScale = scale;
            _hasIcon = true;
        }

        public void SetItem(FishUIInventory parent, InventoryItem item) {
            ParentInventory = parent;
            Item = item;
            // Let the item set up this box if it has a method for it
            if (item != null) {
                // Items will need to call SetIcon directly
            }
        }

        public override void DrawControl(global::FishUI.FishUI UI, float Dt, float Time) {
            var pos = GetAbsolutePosition();
            var size = GetAbsoluteSize();
            var gfx = UI.Graphics;

            // Draw background
            var bgImage = IsSelected ? _backgroundSelected : _backgroundNormal;
            if (bgImage.Userdata != null) {
                gfx.DrawImage(bgImage, pos, size, 0, 1, FishColor.White);
            } else {
                // Fallback rectangle
                var color = IsSelected ? new FishColor(100, 150, 200, 255) : new FishColor(60, 60, 60, 255);
                gfx.DrawRectangle(pos, size, color);
                gfx.DrawRectangleOutline(pos, size, FishColor.White);
            }

            // Draw icon
            if (_hasIcon && _icon.Userdata != null) {
                float iconDrawSize = Math.Min(size.X, size.Y) * 0.7f;
                var iconPos = pos + (size - new Vector2(iconDrawSize)) / 2;

                float tint = IsMousePressed ? 0.7f : 1.0f;
                var tintColor = new FishColor((byte)(255 * tint), (byte)(255 * tint), (byte)(255 * tint), 255);
                gfx.DrawImage(_icon, iconPos, new Vector2(iconDrawSize), 0, 1, tintColor);
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
