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

			MouseButtonDown.Add(new KeyValuePair<InputKey, MouseButton>(InputKey.Click_Left, MouseButton.Left));
			MouseButtonDown.Add(new KeyValuePair<InputKey, MouseButton>(InputKey.Click_Right, MouseButton.Right));
			MouseButtonDown.Add(new KeyValuePair<InputKey, MouseButton>(InputKey.Click_Middle, MouseButton.Middle));
		}
	}
}
