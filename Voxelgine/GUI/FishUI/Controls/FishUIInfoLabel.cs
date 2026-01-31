using FishUI;
using FishUI.Controls;
using System.Numerics;
using System.Text;

namespace Voxelgine.GUI {
    /// <summary>
    /// FishUI-based debug info label that displays multiple lines of text.
    /// Similar to the old GUILabel used for debug output.
    /// </summary>
    public class FishUIInfoLabel : Control {
        private StringBuilder _textBuilder = new();

        public FishColor TextColor { get; set; } = FishColor.White;
        public FishColor OutlineColor { get; set; } = FishColor.Black;
        public bool DrawOutline { get; set; } = true;

        public FishUIInfoLabel() {
            Size = new Vector2(300, 200);
        }

        public void Clear() {
            _textBuilder.Clear();
        }

        public void WriteLine(string text) {
            _textBuilder.AppendLine(text);
        }

        public void WriteLine(string format, params object[] args) {
            _textBuilder.AppendLine(string.Format(format, args));
        }

        public string GetText() => _textBuilder.ToString();

        public override void DrawControl(global::FishUI.FishUI UI, float Dt, float Time) {
            if (!Visible) return;

            var pos = GetAbsolutePosition();
            var gfx = UI.Graphics;
            var font = UI.Settings.FontDefault;
            var text = _textBuilder.ToString();

            if (string.IsNullOrEmpty(text)) return;

            // Draw with outline for readability over 3D content
            if (DrawOutline) {
                for (int ox = -1; ox <= 1; ox++) {
                    for (int oy = -1; oy <= 1; oy++) {
                        if (ox != 0 || oy != 0) {
                            gfx.DrawTextColor(font, text, pos + new Vector2(ox, oy), OutlineColor);
                        }
                    }
                }
            }

            gfx.DrawTextColor(font, text, pos, TextColor);
        }
    }
}
