using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;

using Newtonsoft.Json;
using Raylib_cs;

namespace Voxelgine.Engine {
	public class GameConfig {
		const string ConfigFileName = "data/config.json";

		public string Version = "1.0.0";
		public string Name = "Default Config";

		public List<KeyValuePair<InputKey, MouseButton>> MouseButtonDown;
		public List<KeyValuePair<InputKey, KeyboardKey>> KeyDown;
		public List<KeyValuePair<InputKey, KeyValuePair<KeyboardKey, KeyboardKey>>> TwoKeysDown;

		public GameConfig() {
			MouseButtonDown = new List<KeyValuePair<InputKey, MouseButton>>();
			KeyDown = new List<KeyValuePair<InputKey, KeyboardKey>>();
			TwoKeysDown = new List<KeyValuePair<InputKey, KeyValuePair<KeyboardKey, KeyboardKey>>>();
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
			MouseButtonDown.Clear();
			KeyDown.Clear();
			TwoKeysDown.Clear();

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

			// Number keys (main and keypad)
			TwoKeysDown.Add(new KeyValuePair<InputKey, KeyValuePair<KeyboardKey, KeyboardKey>>(InputKey.Num0,
				new KeyValuePair<KeyboardKey, KeyboardKey>(KeyboardKey.Zero, KeyboardKey.Kp0)));

		}
	}
}
