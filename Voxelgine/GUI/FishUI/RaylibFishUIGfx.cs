using FishUI;
using Raylib_cs;
using System.Numerics;

namespace Voxelgine.GUI {
    /// <summary>
    /// Raylib graphics backend for FishUI using SimpleFishUIGfx base class.
    /// </summary>
    public class RaylibFishUIGfx : SimpleFishUIGfx {
        private readonly Dictionary<string, Texture2D> _textureCache = new();
        private readonly Dictionary<string, Font> _fontCache = new();

        public override void Init() {
            // Raylib should already be initialized by GameWindow
        }

        public override int GetWindowWidth() => Raylib.GetScreenWidth();
        public override int GetWindowHeight() => Raylib.GetScreenHeight();

        public override void FocusWindow() {
            // Raylib doesn't have a direct focus window method
        }

        public override void BeginScissor(Vector2 pos, Vector2 size) {
            Raylib.BeginScissorMode((int)pos.X, (int)pos.Y, (int)size.X, (int)size.Y);
        }

        public override void EndScissor() {
            Raylib.EndScissorMode();
        }

        public override FontRef LoadFont(string path, float size, float spacing, FishColor color) {
            if (!_fontCache.TryGetValue(path, out var font)) {
                font = Raylib.LoadFontEx(path, (int)size, null, 256);
                Raylib.SetTextureFilter(font.Texture, TextureFilter.Bilinear);
                _fontCache[path] = font;
            }

            return new FontRef {
                Path = path,
                Userdata = font,
                Size = size,
                Spacing = spacing,
                Color = color,
                LineHeight = size
            };
        }

        public override ImageRef LoadImage(string path) {
            if (!_textureCache.TryGetValue(path, out var texture)) {
                texture = Raylib.LoadTexture(path);
                _textureCache[path] = texture;
            }

            return new ImageRef {
                Path = path,
                Userdata = texture,
                Width = texture.Width,
                Height = texture.Height
            };
        }

        public override Vector2 MeasureText(FontRef font, string text) {
            if (font.Userdata is Font raylibFont) {
                return Raylib.MeasureTextEx(raylibFont, text ?? "", font.Size, font.Spacing);
            }
            return Vector2.Zero;
        }

        public override void DrawTextColorScale(FontRef font, string text, Vector2 pos, FishColor color, float scale) {
            if (font.Userdata is Font raylibFont && !string.IsNullOrEmpty(text)) {
                var raylibColor = new Color((byte)color.R, (byte)color.G, (byte)color.B, (byte)color.A);
                Raylib.DrawTextEx(raylibFont, text, pos, font.Size * scale, font.Spacing, raylibColor);
            }
        }

        public override void DrawLine(Vector2 start, Vector2 end, float thickness, FishColor color) {
            var raylibColor = new Color((byte)color.R, (byte)color.G, (byte)color.B, (byte)color.A);
            Raylib.DrawLineEx(start, end, thickness, raylibColor);
        }

        public override void DrawRectangle(Vector2 pos, Vector2 size, FishColor color) {
            var raylibColor = new Color((byte)color.R, (byte)color.G, (byte)color.B, (byte)color.A);
            Raylib.DrawRectangleV(pos, size, raylibColor);
        }

        public override void DrawRectangleOutline(Vector2 pos, Vector2 size, FishColor color) {
            var raylibColor = new Color((byte)color.R, (byte)color.G, (byte)color.B, (byte)color.A);
            Raylib.DrawRectangleLinesEx(new Rectangle(pos, size), 1, raylibColor);
        }

        public override void DrawCircle(Vector2 center, float radius, FishColor color) {
            var raylibColor = new Color((byte)color.R, (byte)color.G, (byte)color.B, (byte)color.A);
            Raylib.DrawCircleV(center, radius, raylibColor);
        }

        public override void DrawCircleOutline(Vector2 center, float radius, FishColor color, float thickness) {
            var raylibColor = new Color((byte)color.R, (byte)color.G, (byte)color.B, (byte)color.A);
            Raylib.DrawCircleLinesV(center, radius, raylibColor);
        }

        public override void DrawImage(ImageRef img, Vector2 pos, float rotation, float scale, FishColor color) {
            if (img.Userdata is Texture2D texture) {
                var raylibColor = new Color((byte)color.R, (byte)color.G, (byte)color.B, (byte)color.A);

                if (img.IsAtlasRegion) {
                    var source = new Rectangle(img.SourceX, img.SourceY, img.SourceW, img.SourceH);
                    var dest = new Rectangle(pos.X, pos.Y, img.SourceW * scale, img.SourceH * scale);
                    Raylib.DrawTexturePro(texture, source, dest, Vector2.Zero, rotation, raylibColor);
                } else {
                    Raylib.DrawTextureEx(texture, pos, rotation, scale, raylibColor);
                }
            } else if (img.AtlasParent?.Userdata is Texture2D atlasTexture) {
                var raylibColor = new Color((byte)color.R, (byte)color.G, (byte)color.B, (byte)color.A);
                var source = new Rectangle(img.SourceX, img.SourceY, img.SourceW, img.SourceH);
                var dest = new Rectangle(pos.X, pos.Y, img.SourceW * scale, img.SourceH * scale);
                Raylib.DrawTexturePro(atlasTexture, source, dest, Vector2.Zero, rotation, raylibColor);
            }
        }

        public override void DrawImage(ImageRef img, Vector2 pos, Vector2 size, float rotation, float scale, FishColor color) {
            if (img.Userdata is Texture2D texture) {
                var raylibColor = new Color((byte)color.R, (byte)color.G, (byte)color.B, (byte)color.A);

                if (img.IsAtlasRegion) {
                    var source = new Rectangle(img.SourceX, img.SourceY, img.SourceW, img.SourceH);
                    var dest = new Rectangle(pos.X, pos.Y, size.X, size.Y);
                    Raylib.DrawTexturePro(texture, source, dest, Vector2.Zero, rotation, raylibColor);
                } else {
                    var source = new Rectangle(0, 0, texture.Width, texture.Height);
                    var dest = new Rectangle(pos.X, pos.Y, size.X, size.Y);
                    Raylib.DrawTexturePro(texture, source, dest, Vector2.Zero, rotation, raylibColor);
                }
            } else if (img.AtlasParent?.Userdata is Texture2D atlasTexture) {
                var raylibColor = new Color((byte)color.R, (byte)color.G, (byte)color.B, (byte)color.A);
                var source = new Rectangle(img.SourceX, img.SourceY, img.SourceW, img.SourceH);
                var dest = new Rectangle(pos.X, pos.Y, size.X, size.Y);
                Raylib.DrawTexturePro(atlasTexture, source, dest, Vector2.Zero, rotation, raylibColor);
            }
        }

        public override void SetImageFilter(ImageRef img, bool pixelated) {
            if (img.Userdata is Texture2D texture) {
                Raylib.SetTextureFilter(texture, pixelated ? TextureFilter.Point : TextureFilter.Bilinear);
            }
        }
    }
}
