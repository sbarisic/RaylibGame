using System;
using System.Collections.Generic;
using System.IO;

using Newtonsoft.Json;
using Voxelgine.Engine.DI;
using Voxelgine.Engine.Input;

namespace Voxelgine.Engine
{
	public class GameConfig : IFishConfig
	{
		const string ConfigFileName = "data/config.json";
		public const int DefaultMaxChunkDrawDistance = 108;
		public const int MinimumMaxChunkDrawDistance = 32;
		public const int MaximumMaxChunkDrawDistance = 512;
		public const int DefaultChunkMeshUploadBudget = 24;
		public const int MinimumChunkMeshUploadBudget = 1;
		public const int MaximumChunkMeshUploadBudget = 128;

		public int Monitor = -1;

		public int WindowWidth { get; set; } = 1920;

		public int WindowHeight { get; set; } = 1080;

		public bool Fullscreen = false;

		public bool UseFSDesktopRes = true;

		public bool Borderless = false;

		public bool SetFocused = true;

		public bool Resizable = true;

		public int TargetFPS = -1;

		public float MouseSensitivity = 0.35f;

		public string Title { get; set; } = "Aurora Falls";

		public string LogFolder { get; set; } = "data";

		[SettingsHidden]
		public GameLogLevel LogLevel { get; set; } = GameLogLevel.Debug; // Leave default to Debug, i'll change to Trace when required

		[SettingsHidden]
		public bool HighDpiWindow = true;

		public bool VSync = true;

		public bool Msaa = true;

		public int MaxChunkDrawDistance = DefaultMaxChunkDrawDistance;

		public int ChunkMeshUploadBudget = DefaultChunkMeshUploadBudget;

		public SunShadowQuality SunShadowQuality = SunShadowQuality.Medium;

		[SettingsHidden]
		public int LastOptWnd_X = 0;

		[SettingsHidden]
		public int LastOptWnd_Y = 0;

		[SettingsHidden]
		public int LastOptWnd_W = 0;

		[SettingsHidden]
		public int LastOptWnd_H = 0;

		[SettingsHidden]
		public KeyValuePair<InputKey, PhysicalMouseButton>[] MouseButtonDown;

		[SettingsHidden]
		public KeyValuePair<InputKey, PhysicalKey>[] KeyDown;

		[SettingsHidden]
		public KeyValuePair<InputKey, KeyValuePair<PhysicalKey, PhysicalKey>>[] TwoKeysDown;

		public GameConfig(IFishEngineRunner _)
		{
			MouseButtonDown = Array.Empty<KeyValuePair<InputKey, PhysicalMouseButton>>();
			KeyDown = Array.Empty<KeyValuePair<InputKey, PhysicalKey>>();
			TwoKeysDown = Array.Empty<KeyValuePair<InputKey, KeyValuePair<PhysicalKey, PhysicalKey>>>();
		}

		public void SaveToJson()
		{
			string json = JsonConvert.SerializeObject(this, Formatting.Indented, CreateJsonSettings());
			File.WriteAllText(ConfigFileName, json);
		}

		public void LoadFromJson()
		{
			if (!File.Exists(ConfigFileName))
				return;
			string json = File.ReadAllText(ConfigFileName);
			JsonConvert.PopulateObject(json, this, CreateJsonSettings());
		}

		internal static JsonSerializerSettings CreateJsonSettings()
		{
			JsonSerializerSettings settings = new();
			// The specific compatibility converters must precede StringEnumConverter.
			// Otherwise legacy Raylib names such as Zero and Kp0 are interpreted as
			// raw PhysicalKey enum members and fail before compatibility mapping runs.
			settings.Converters.Add(new PhysicalKeyJsonConverter());
			settings.Converters.Add(new PhysicalMouseButtonJsonConverter());
			settings.Converters.Add(new Newtonsoft.Json.Converters.StringEnumConverter());
			return settings;
		}

		public void GenerateDefaultKeybinds()
		{
			List<KeyValuePair<InputKey, PhysicalMouseButton>> MouseButtonDown = new();
			List<KeyValuePair<InputKey, PhysicalKey>> KeyDown = new();
			List<KeyValuePair<InputKey, KeyValuePair<PhysicalKey, PhysicalKey>>> TwoKeysDown = new();

			//MouseButtonDown.Clear();
			//KeyDown.Clear();
			//TwoKeysDown.Clear();

			// Mouse buttons
			MouseButtonDown.Add(new(InputKey.Click_Left, PhysicalMouseButton.Left));
			MouseButtonDown.Add(new(InputKey.Click_Right, PhysicalMouseButton.Right));
			MouseButtonDown.Add(new(InputKey.Click_Middle, PhysicalMouseButton.Middle));

			// Movement keys
			KeyDown.Add(new(InputKey.W, PhysicalKey.W));
			KeyDown.Add(new(InputKey.A, PhysicalKey.A));
			KeyDown.Add(new(InputKey.S, PhysicalKey.S));
			KeyDown.Add(new(InputKey.D, PhysicalKey.D));

			// Q/E
			KeyDown.Add(new(InputKey.Q, PhysicalKey.Q));
			KeyDown.Add(new(InputKey.E, PhysicalKey.E));

			// Misc keys
			KeyDown.Add(new(InputKey.R, PhysicalKey.R));
			KeyDown.Add(new(InputKey.F, PhysicalKey.F));
			KeyDown.Add(new(InputKey.G, PhysicalKey.G));
			KeyDown.Add(new(InputKey.C, PhysicalKey.C));
			KeyDown.Add(new(InputKey.V, PhysicalKey.V));
			KeyDown.Add(new(InputKey.T, PhysicalKey.T));
			KeyDown.Add(new(InputKey.M, PhysicalKey.M));
			KeyDown.Add(new(InputKey.K, PhysicalKey.K));
			KeyDown.Add(new(InputKey.I, PhysicalKey.I));
			KeyDown.Add(new(InputKey.O, PhysicalKey.O));
			KeyDown.Add(new(InputKey.P, PhysicalKey.P));
			KeyDown.Add(new(InputKey.Tab, PhysicalKey.Tab));
			KeyDown.Add(new(InputKey.Space, PhysicalKey.Space));

			// Function keys
			KeyDown.Add(new(InputKey.F1, PhysicalKey.F1));
			KeyDown.Add(new(InputKey.F2, PhysicalKey.F2));
			KeyDown.Add(new(InputKey.F3, PhysicalKey.F3));
			KeyDown.Add(new(InputKey.F4, PhysicalKey.F4));
			KeyDown.Add(new(InputKey.F5, PhysicalKey.F5));

			// Arrow keys
			KeyDown.Add(new(InputKey.Up, PhysicalKey.Up));
			KeyDown.Add(new(InputKey.Down, PhysicalKey.Down));
			KeyDown.Add(new(InputKey.Left, PhysicalKey.Left));
			KeyDown.Add(new(InputKey.Right, PhysicalKey.Right));

			// Escape
			KeyDown.Add(new(InputKey.Esc, PhysicalKey.Escape));

			// Number keys (main and keypad) and combos
			TwoKeysDown.Add(new(InputKey.Num0, new(PhysicalKey.Alpha0, PhysicalKey.Numpad0)));
			TwoKeysDown.Add(new(InputKey.Num1, new(PhysicalKey.Alpha1, PhysicalKey.Numpad1)));
			TwoKeysDown.Add(new(InputKey.Num2, new(PhysicalKey.Alpha2, PhysicalKey.Numpad2)));
			TwoKeysDown.Add(new(InputKey.Num3, new(PhysicalKey.Alpha3, PhysicalKey.Numpad3)));
			TwoKeysDown.Add(new(InputKey.Num4, new(PhysicalKey.Alpha4, PhysicalKey.Numpad4)));
			TwoKeysDown.Add(new(InputKey.Num5, new(PhysicalKey.Alpha5, PhysicalKey.Numpad5)));
			TwoKeysDown.Add(new(InputKey.Num6, new(PhysicalKey.Alpha6, PhysicalKey.Numpad6)));
			TwoKeysDown.Add(new(InputKey.Num7, new(PhysicalKey.Alpha7, PhysicalKey.Numpad7)));
			TwoKeysDown.Add(new(InputKey.Num8, new(PhysicalKey.Alpha8, PhysicalKey.Numpad8)));
			TwoKeysDown.Add(new(InputKey.Num9, new(PhysicalKey.Alpha9, PhysicalKey.Numpad9)));
			TwoKeysDown.Add(new(InputKey.Enter, new(PhysicalKey.Enter, PhysicalKey.NumpadEnter)));
			TwoKeysDown.Add(new(InputKey.Ctrl, new(PhysicalKey.LeftControl, PhysicalKey.RightControl)));
			TwoKeysDown.Add(new(InputKey.Alt, new(PhysicalKey.LeftAlt, PhysicalKey.RightAlt)));
			TwoKeysDown.Add(new(InputKey.Shift, new(PhysicalKey.LeftShift, PhysicalKey.RightShift)));

			this.MouseButtonDown = MouseButtonDown.ToArray();
			this.KeyDown = KeyDown.ToArray();
			this.TwoKeysDown = TwoKeysDown.ToArray();
		}
	}
}
