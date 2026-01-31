using FishUI;
using Raylib_cs;
using System.Numerics;

namespace Voxelgine.GUI {
    /// <summary>
    /// Raylib input backend for FishUI.
    /// </summary>
    public class RaylibFishUIInput : IFishUIInput {
        public FishKey GetKeyPressed() {
            var key = Raylib.GetKeyPressed();
            return ConvertKey((KeyboardKey)key);
        }

        public int GetCharPressed() {
            return Raylib.GetCharPressed();
        }

        public bool IsKeyDown(FishKey key) => Raylib.IsKeyDown(ConvertToRaylib(key));
        public bool IsKeyUp(FishKey key) => Raylib.IsKeyUp(ConvertToRaylib(key));
        public bool IsKeyPressed(FishKey key) => Raylib.IsKeyPressed(ConvertToRaylib(key));
        public bool IsKeyReleased(FishKey key) => Raylib.IsKeyReleased(ConvertToRaylib(key));

        public Vector2 GetMousePosition() => Raylib.GetMousePosition();
        public float GetMouseWheelMove() => Raylib.GetMouseWheelMove();

        public FishTouchPoint[] GetTouchPoints() {
            int count = Raylib.GetTouchPointCount();
            var points = new FishTouchPoint[count];
            for (int i = 0; i < count; i++) {
                var pos = Raylib.GetTouchPosition(i);
                points[i] = new FishTouchPoint { Position = pos, Id = i };
            }
            return points;
        }

        public bool IsMouseDown(FishMouseButton button) => Raylib.IsMouseButtonDown(ConvertToRaylib(button));
        public bool IsMouseUp(FishMouseButton button) => Raylib.IsMouseButtonUp(ConvertToRaylib(button));
        public bool IsMousePressed(FishMouseButton button) => Raylib.IsMouseButtonPressed(ConvertToRaylib(button));
        public bool IsMouseReleased(FishMouseButton button) => Raylib.IsMouseButtonReleased(ConvertToRaylib(button));

        public string GetClipboardText() => Raylib.GetClipboardText_() ?? "";
        public void SetClipboardText(string text) => Raylib.SetClipboardText(text);

        private static MouseButton ConvertToRaylib(FishMouseButton button) => button switch {
            FishMouseButton.Left => MouseButton.Left,
            FishMouseButton.Right => MouseButton.Right,
            FishMouseButton.Middle => MouseButton.Middle,
            _ => MouseButton.Left
        };

        private static KeyboardKey ConvertToRaylib(FishKey key) => key switch {
            FishKey.Space => KeyboardKey.Space,
            FishKey.Apostrophe => KeyboardKey.Apostrophe,
            FishKey.Comma => KeyboardKey.Comma,
            FishKey.Minus => KeyboardKey.Minus,
            FishKey.Period => KeyboardKey.Period,
            FishKey.Slash => KeyboardKey.Slash,
            FishKey.Zero => KeyboardKey.Zero,
            FishKey.One => KeyboardKey.One,
            FishKey.Two => KeyboardKey.Two,
            FishKey.Three => KeyboardKey.Three,
            FishKey.Four => KeyboardKey.Four,
            FishKey.Five => KeyboardKey.Five,
            FishKey.Six => KeyboardKey.Six,
            FishKey.Seven => KeyboardKey.Seven,
            FishKey.Eight => KeyboardKey.Eight,
            FishKey.Nine => KeyboardKey.Nine,
            FishKey.Semicolon => KeyboardKey.Semicolon,
            FishKey.Equal => KeyboardKey.Equal,
            FishKey.A => KeyboardKey.A,
            FishKey.B => KeyboardKey.B,
            FishKey.C => KeyboardKey.C,
            FishKey.D => KeyboardKey.D,
            FishKey.E => KeyboardKey.E,
            FishKey.F => KeyboardKey.F,
            FishKey.G => KeyboardKey.G,
            FishKey.H => KeyboardKey.H,
            FishKey.I => KeyboardKey.I,
            FishKey.J => KeyboardKey.J,
            FishKey.K => KeyboardKey.K,
            FishKey.L => KeyboardKey.L,
            FishKey.M => KeyboardKey.M,
            FishKey.N => KeyboardKey.N,
            FishKey.O => KeyboardKey.O,
            FishKey.P => KeyboardKey.P,
            FishKey.Q => KeyboardKey.Q,
            FishKey.R => KeyboardKey.R,
            FishKey.S => KeyboardKey.S,
            FishKey.T => KeyboardKey.T,
            FishKey.U => KeyboardKey.U,
            FishKey.V => KeyboardKey.V,
            FishKey.W => KeyboardKey.W,
            FishKey.X => KeyboardKey.X,
            FishKey.Y => KeyboardKey.Y,
            FishKey.Z => KeyboardKey.Z,
            FishKey.LeftBracket => KeyboardKey.LeftBracket,
            FishKey.Backslash => KeyboardKey.Backslash,
            FishKey.RightBracket => KeyboardKey.RightBracket,
            FishKey.Grave => KeyboardKey.Grave,
            FishKey.Escape => KeyboardKey.Escape,
            FishKey.Enter => KeyboardKey.Enter,
            FishKey.Tab => KeyboardKey.Tab,
            FishKey.Backspace => KeyboardKey.Backspace,
            FishKey.Insert => KeyboardKey.Insert,
            FishKey.Delete => KeyboardKey.Delete,
            FishKey.Right => KeyboardKey.Right,
            FishKey.Left => KeyboardKey.Left,
            FishKey.Down => KeyboardKey.Down,
            FishKey.Up => KeyboardKey.Up,
            FishKey.PageUp => KeyboardKey.PageUp,
            FishKey.PageDown => KeyboardKey.PageDown,
            FishKey.Home => KeyboardKey.Home,
            FishKey.End => KeyboardKey.End,
            FishKey.CapsLock => KeyboardKey.CapsLock,
            FishKey.ScrollLock => KeyboardKey.ScrollLock,
            FishKey.NumLock => KeyboardKey.NumLock,
            FishKey.PrintScreen => KeyboardKey.PrintScreen,
            FishKey.Pause => KeyboardKey.Pause,
            FishKey.F1 => KeyboardKey.F1,
            FishKey.F2 => KeyboardKey.F2,
            FishKey.F3 => KeyboardKey.F3,
            FishKey.F4 => KeyboardKey.F4,
            FishKey.F5 => KeyboardKey.F5,
            FishKey.F6 => KeyboardKey.F6,
            FishKey.F7 => KeyboardKey.F7,
            FishKey.F8 => KeyboardKey.F8,
            FishKey.F9 => KeyboardKey.F9,
            FishKey.F10 => KeyboardKey.F10,
            FishKey.F11 => KeyboardKey.F11,
            FishKey.F12 => KeyboardKey.F12,
            FishKey.LeftShift => KeyboardKey.LeftShift,
            FishKey.LeftControl => KeyboardKey.LeftControl,
            FishKey.LeftAlt => KeyboardKey.LeftAlt,
            FishKey.LeftSuper => KeyboardKey.LeftSuper,
            FishKey.RightShift => KeyboardKey.RightShift,
            FishKey.RightControl => KeyboardKey.RightControl,
            FishKey.RightAlt => KeyboardKey.RightAlt,
            FishKey.RightSuper => KeyboardKey.RightSuper,
            _ => KeyboardKey.Null
        };

        private static FishKey ConvertKey(KeyboardKey key) => key switch {
            KeyboardKey.Space => FishKey.Space,
            KeyboardKey.Apostrophe => FishKey.Apostrophe,
            KeyboardKey.Comma => FishKey.Comma,
            KeyboardKey.Minus => FishKey.Minus,
            KeyboardKey.Period => FishKey.Period,
            KeyboardKey.Slash => FishKey.Slash,
            KeyboardKey.Zero => FishKey.Zero,
            KeyboardKey.One => FishKey.One,
            KeyboardKey.Two => FishKey.Two,
            KeyboardKey.Three => FishKey.Three,
            KeyboardKey.Four => FishKey.Four,
            KeyboardKey.Five => FishKey.Five,
            KeyboardKey.Six => FishKey.Six,
            KeyboardKey.Seven => FishKey.Seven,
            KeyboardKey.Eight => FishKey.Eight,
            KeyboardKey.Nine => FishKey.Nine,
            KeyboardKey.Semicolon => FishKey.Semicolon,
            KeyboardKey.Equal => FishKey.Equal,
            KeyboardKey.A => FishKey.A,
            KeyboardKey.B => FishKey.B,
            KeyboardKey.C => FishKey.C,
            KeyboardKey.D => FishKey.D,
            KeyboardKey.E => FishKey.E,
            KeyboardKey.F => FishKey.F,
            KeyboardKey.G => FishKey.G,
            KeyboardKey.H => FishKey.H,
            KeyboardKey.I => FishKey.I,
            KeyboardKey.J => FishKey.J,
            KeyboardKey.K => FishKey.K,
            KeyboardKey.L => FishKey.L,
            KeyboardKey.M => FishKey.M,
            KeyboardKey.N => FishKey.N,
            KeyboardKey.O => FishKey.O,
            KeyboardKey.P => FishKey.P,
            KeyboardKey.Q => FishKey.Q,
            KeyboardKey.R => FishKey.R,
            KeyboardKey.S => FishKey.S,
            KeyboardKey.T => FishKey.T,
            KeyboardKey.U => FishKey.U,
            KeyboardKey.V => FishKey.V,
            KeyboardKey.W => FishKey.W,
            KeyboardKey.X => FishKey.X,
            KeyboardKey.Y => FishKey.Y,
            KeyboardKey.Z => FishKey.Z,
            KeyboardKey.LeftBracket => FishKey.LeftBracket,
            KeyboardKey.Backslash => FishKey.Backslash,
            KeyboardKey.RightBracket => FishKey.RightBracket,
            KeyboardKey.Grave => FishKey.Grave,
            KeyboardKey.Escape => FishKey.Escape,
            KeyboardKey.Enter => FishKey.Enter,
            KeyboardKey.Tab => FishKey.Tab,
            KeyboardKey.Backspace => FishKey.Backspace,
            KeyboardKey.Insert => FishKey.Insert,
            KeyboardKey.Delete => FishKey.Delete,
            KeyboardKey.Right => FishKey.Right,
            KeyboardKey.Left => FishKey.Left,
            KeyboardKey.Down => FishKey.Down,
            KeyboardKey.Up => FishKey.Up,
            KeyboardKey.PageUp => FishKey.PageUp,
            KeyboardKey.PageDown => FishKey.PageDown,
            KeyboardKey.Home => FishKey.Home,
            KeyboardKey.End => FishKey.End,
            KeyboardKey.CapsLock => FishKey.CapsLock,
            KeyboardKey.ScrollLock => FishKey.ScrollLock,
            KeyboardKey.NumLock => FishKey.NumLock,
            KeyboardKey.PrintScreen => FishKey.PrintScreen,
            KeyboardKey.Pause => FishKey.Pause,
            KeyboardKey.F1 => FishKey.F1,
            KeyboardKey.F2 => FishKey.F2,
            KeyboardKey.F3 => FishKey.F3,
            KeyboardKey.F4 => FishKey.F4,
            KeyboardKey.F5 => FishKey.F5,
            KeyboardKey.F6 => FishKey.F6,
            KeyboardKey.F7 => FishKey.F7,
            KeyboardKey.F8 => FishKey.F8,
            KeyboardKey.F9 => FishKey.F9,
            KeyboardKey.F10 => FishKey.F10,
            KeyboardKey.F11 => FishKey.F11,
            KeyboardKey.F12 => FishKey.F12,
            KeyboardKey.LeftShift => FishKey.LeftShift,
            KeyboardKey.LeftControl => FishKey.LeftControl,
            KeyboardKey.LeftAlt => FishKey.LeftAlt,
            KeyboardKey.LeftSuper => FishKey.LeftSuper,
            KeyboardKey.RightShift => FishKey.RightShift,
            KeyboardKey.RightControl => FishKey.RightControl,
            KeyboardKey.RightAlt => FishKey.RightAlt,
            KeyboardKey.RightSuper => FishKey.RightSuper,
            _ => FishKey.None
        };
    }
}
