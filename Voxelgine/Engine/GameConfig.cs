using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;

using Newtonsoft.Json;
using Raylib_cs;
using System.Reflection;

namespace Voxelgine.Engine {
	public class ConfigValueRef {
		public string FieldName;
		public FieldInfo Field;

		string LastValueString = null;

		public ConfigValueRef(FieldInfo field, string fieldName) {
			Field = field;
			FieldName = fieldName;
			GetValueString();
		}

		public string GetValueString() {
			LastValueString = Field.GetValue(Program.Cfg)?.ToString() ?? "null";
			return LastValueString;
		}

		public void SetValueString(string value) {
			LastValueString = value;

			if (value == "null") {
				Field.SetValue(Program.Cfg, null);
				return;
			}

			if (Field.FieldType == typeof(int)) {
				Field.SetValue(Program.Cfg, int.Parse(value));
			} else if (Field.FieldType == typeof(float)) {
				Field.SetValue(Program.Cfg, float.Parse(value));
			} else if (Field.FieldType == typeof(bool)) {
				Field.SetValue(Program.Cfg, bool.Parse(value));
			} else if (Field.FieldType == typeof(string)) {
				Field.SetValue(Program.Cfg, value);
			} else if (Field.FieldType.IsEnum) {
				Field.SetValue(Program.Cfg, Enum.Parse(Field.FieldType, value));
			} else {
				throw new NotSupportedException($"Unsupported field type: {Field.FieldType}");
			}
		}

		public override string ToString() {
			return string.Format("{0} = '{1}'", FieldName, LastValueString);
		}
	}

	public class GameConfig {
		const string ConfigFileName = "data/config.json";

		public int Monitor;
		public int WindowWidth;
		public int WindowHeight;
		public bool Fullscreen;
		public bool UseFSDesktopRes;
		public bool Borderless;
		public bool SetFocused;
		public bool Resizable;

		public int TargetFPS;

		[SettingsHidden]
		public bool HighDpiWindow;

		public bool VSync;
		public bool Msaa;

		[SettingsHidden]
		public int LastOptWnd_X;

		[SettingsHidden]
		public int LastOptWnd_Y;

		[SettingsHidden]
		public int LastOptWnd_W;

		[SettingsHidden]
		public int LastOptWnd_H;

		[SettingsHidden]
		public KeyValuePair<InputKey, MouseButton>[] MouseButtonDown;

		[SettingsHidden]
		public KeyValuePair<InputKey, KeyboardKey>[] KeyDown;

		[SettingsHidden]
		public KeyValuePair<InputKey, KeyValuePair<KeyboardKey, KeyboardKey>>[] TwoKeysDown;

		public void SetDefaults() {
			Monitor = -1;
			WindowWidth = 1920;
			WindowHeight = 1080;
			Fullscreen = false;
			UseFSDesktopRes = true;
			Borderless = false;
			SetFocused = true;
			TargetFPS = -1;
			HighDpiWindow = true;
			VSync = true;
			Msaa = false;
			Resizable = true;


			LastOptWnd_X = 0;
			LastOptWnd_Y = 0;
			LastOptWnd_W = 0;
			LastOptWnd_H = 0;
		}

		public GameConfig() {
			SetDefaults();

			MouseButtonDown = new KeyValuePair<InputKey, MouseButton>[] { };
			KeyDown = new KeyValuePair<InputKey, KeyboardKey>[] { };
			TwoKeysDown = new KeyValuePair<InputKey, KeyValuePair<KeyboardKey, KeyboardKey>>[] { };
		}

		public IEnumerable<ConfigValueRef> GetVariables() {
			Type T = GetType();

			FieldInfo[] Fields = T.GetFields(BindingFlags.Public | BindingFlags.Instance);
			foreach (FieldInfo F in Fields) {
				if (F.GetCustomAttribute<SettingsHiddenAttribute>() != null)
					continue;

				yield return new ConfigValueRef(F, F.Name);
			}
		}

		public void SaveToJson() {
			JsonSerializerSettings JSS = new JsonSerializerSettings();
			JSS.Converters.Add(new Newtonsoft.Json.Converters.StringEnumConverter());
			string json = JsonConvert.SerializeObject(this, Formatting.Indented, JSS);
			File.WriteAllText(ConfigFileName, json);
		}

		public void LoadFromJson() {
			if (!File.Exists(ConfigFileName))
				return;
			string json = File.ReadAllText(ConfigFileName);
			JsonConvert.PopulateObject(json, this);

		}

		public void GenerateDefaultKeybinds() {
			List<KeyValuePair<InputKey, MouseButton>> MouseButtonDown = new List<KeyValuePair<InputKey, MouseButton>>();
			List<KeyValuePair<InputKey, KeyboardKey>> KeyDown = new List<KeyValuePair<InputKey, KeyboardKey>>();
			List<KeyValuePair<InputKey, KeyValuePair<KeyboardKey, KeyboardKey>>> TwoKeysDown = new List<KeyValuePair<InputKey, KeyValuePair<KeyboardKey, KeyboardKey>>>();

			//MouseButtonDown.Clear();
			//KeyDown.Clear();
			//TwoKeysDown.Clear();

			// Mouse buttons
			MouseButtonDown.Add(new KeyValuePair<InputKey, MouseButton>(InputKey.Click_Left, MouseButton.Left));
			MouseButtonDown.Add(new KeyValuePair<InputKey, MouseButton>(InputKey.Click_Right, MouseButton.Right));
			MouseButtonDown.Add(new KeyValuePair<InputKey, MouseButton>(InputKey.Click_Middle, MouseButton.Middle));

			// Movement keys
			KeyDown.Add(new KeyValuePair<InputKey, KeyboardKey>(InputKey.W, KeyboardKey.W));
			KeyDown.Add(new KeyValuePair<InputKey, KeyboardKey>(InputKey.A, KeyboardKey.A));
			KeyDown.Add(new KeyValuePair<InputKey, KeyboardKey>(InputKey.S, KeyboardKey.S));
			KeyDown.Add(new KeyValuePair<InputKey, KeyboardKey>(InputKey.D, KeyboardKey.D));

			// Q/E
			KeyDown.Add(new KeyValuePair<InputKey, KeyboardKey>(InputKey.Q, KeyboardKey.Q));
			KeyDown.Add(new KeyValuePair<InputKey, KeyboardKey>(InputKey.E, KeyboardKey.E));

			// Misc keys
			KeyDown.Add(new KeyValuePair<InputKey, KeyboardKey>(InputKey.R, KeyboardKey.R));
			KeyDown.Add(new KeyValuePair<InputKey, KeyboardKey>(InputKey.F, KeyboardKey.F));
			KeyDown.Add(new KeyValuePair<InputKey, KeyboardKey>(InputKey.G, KeyboardKey.G));
			KeyDown.Add(new KeyValuePair<InputKey, KeyboardKey>(InputKey.C, KeyboardKey.C));
			KeyDown.Add(new KeyValuePair<InputKey, KeyboardKey>(InputKey.V, KeyboardKey.V));
			KeyDown.Add(new KeyValuePair<InputKey, KeyboardKey>(InputKey.T, KeyboardKey.T));
			KeyDown.Add(new KeyValuePair<InputKey, KeyboardKey>(InputKey.M, KeyboardKey.M));
			KeyDown.Add(new KeyValuePair<InputKey, KeyboardKey>(InputKey.K, KeyboardKey.K));
			KeyDown.Add(new KeyValuePair<InputKey, KeyboardKey>(InputKey.I, KeyboardKey.I));
			KeyDown.Add(new KeyValuePair<InputKey, KeyboardKey>(InputKey.O, KeyboardKey.O));
			KeyDown.Add(new KeyValuePair<InputKey, KeyboardKey>(InputKey.P, KeyboardKey.P));
			KeyDown.Add(new KeyValuePair<InputKey, KeyboardKey>(InputKey.Tab, KeyboardKey.Tab));
			KeyDown.Add(new KeyValuePair<InputKey, KeyboardKey>(InputKey.Space, KeyboardKey.Space));

			// Function keys
			KeyDown.Add(new KeyValuePair<InputKey, KeyboardKey>(InputKey.F1, KeyboardKey.F1));
			KeyDown.Add(new KeyValuePair<InputKey, KeyboardKey>(InputKey.F2, KeyboardKey.F2));
			KeyDown.Add(new KeyValuePair<InputKey, KeyboardKey>(InputKey.F3, KeyboardKey.F3));
			KeyDown.Add(new KeyValuePair<InputKey, KeyboardKey>(InputKey.F4, KeyboardKey.F4));
			KeyDown.Add(new KeyValuePair<InputKey, KeyboardKey>(InputKey.F5, KeyboardKey.F5));

			// Arrow keys
			KeyDown.Add(new KeyValuePair<InputKey, KeyboardKey>(InputKey.Up, KeyboardKey.Up));
			KeyDown.Add(new KeyValuePair<InputKey, KeyboardKey>(InputKey.Down, KeyboardKey.Down));
			KeyDown.Add(new KeyValuePair<InputKey, KeyboardKey>(InputKey.Left, KeyboardKey.Left));
			KeyDown.Add(new KeyValuePair<InputKey, KeyboardKey>(InputKey.Right, KeyboardKey.Right));

			// Escape
			KeyDown.Add(new KeyValuePair<InputKey, KeyboardKey>(InputKey.Esc, KeyboardKey.Escape));

			// Number keys (main and keypad) and combos
			TwoKeysDown.Add(new KeyValuePair<InputKey, KeyValuePair<KeyboardKey, KeyboardKey>>(InputKey.Num0,
				new KeyValuePair<KeyboardKey, KeyboardKey>(KeyboardKey.Zero, KeyboardKey.Kp0)));

			TwoKeysDown.Add(new KeyValuePair<InputKey, KeyValuePair<KeyboardKey, KeyboardKey>>(InputKey.Num1,
				new KeyValuePair<KeyboardKey, KeyboardKey>(KeyboardKey.One, KeyboardKey.Kp1)));

			TwoKeysDown.Add(new KeyValuePair<InputKey, KeyValuePair<KeyboardKey, KeyboardKey>>(InputKey.Num2,
				new KeyValuePair<KeyboardKey, KeyboardKey>(KeyboardKey.Two, KeyboardKey.Kp2)));

			TwoKeysDown.Add(new KeyValuePair<InputKey, KeyValuePair<KeyboardKey, KeyboardKey>>(InputKey.Num3,
				new KeyValuePair<KeyboardKey, KeyboardKey>(KeyboardKey.Three, KeyboardKey.Kp3)));

			TwoKeysDown.Add(new KeyValuePair<InputKey, KeyValuePair<KeyboardKey, KeyboardKey>>(InputKey.Num4,
				new KeyValuePair<KeyboardKey, KeyboardKey>(KeyboardKey.Four, KeyboardKey.Kp4)));

			TwoKeysDown.Add(new KeyValuePair<InputKey, KeyValuePair<KeyboardKey, KeyboardKey>>(InputKey.Num5,
				new KeyValuePair<KeyboardKey, KeyboardKey>(KeyboardKey.Five, KeyboardKey.Kp5)));

			TwoKeysDown.Add(new KeyValuePair<InputKey, KeyValuePair<KeyboardKey, KeyboardKey>>(InputKey.Num6,
				new KeyValuePair<KeyboardKey, KeyboardKey>(KeyboardKey.Six, KeyboardKey.Kp6)));

			TwoKeysDown.Add(new KeyValuePair<InputKey, KeyValuePair<KeyboardKey, KeyboardKey>>(InputKey.Num7,
				new KeyValuePair<KeyboardKey, KeyboardKey>(KeyboardKey.Seven, KeyboardKey.Kp7)));

			TwoKeysDown.Add(new KeyValuePair<InputKey, KeyValuePair<KeyboardKey, KeyboardKey>>(InputKey.Num8,
				new KeyValuePair<KeyboardKey, KeyboardKey>(KeyboardKey.Eight, KeyboardKey.Kp8)));

			TwoKeysDown.Add(new KeyValuePair<InputKey, KeyValuePair<KeyboardKey, KeyboardKey>>(InputKey.Num9,
				new KeyValuePair<KeyboardKey, KeyboardKey>(KeyboardKey.Nine, KeyboardKey.Kp9)));

			TwoKeysDown.Add(new KeyValuePair<InputKey, KeyValuePair<KeyboardKey, KeyboardKey>>(InputKey.Enter,
				new KeyValuePair<KeyboardKey, KeyboardKey>(KeyboardKey.Enter, KeyboardKey.KpEnter)));

			TwoKeysDown.Add(new KeyValuePair<InputKey, KeyValuePair<KeyboardKey, KeyboardKey>>(InputKey.Ctrl,
				new KeyValuePair<KeyboardKey, KeyboardKey>(KeyboardKey.LeftControl, KeyboardKey.RightControl)));

			TwoKeysDown.Add(new KeyValuePair<InputKey, KeyValuePair<KeyboardKey, KeyboardKey>>(InputKey.Alt,
				new KeyValuePair<KeyboardKey, KeyboardKey>(KeyboardKey.LeftAlt, KeyboardKey.RightAlt)));

			TwoKeysDown.Add(new KeyValuePair<InputKey, KeyValuePair<KeyboardKey, KeyboardKey>>(InputKey.Shift,
				new KeyValuePair<KeyboardKey, KeyboardKey>(KeyboardKey.LeftShift, KeyboardKey.RightShift)));

			this.MouseButtonDown = MouseButtonDown.ToArray();
			this.KeyDown = KeyDown.ToArray();
			this.TwoKeysDown = TwoKeysDown.ToArray();
		}
	}
}
