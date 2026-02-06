using Raylib_cs;
using Voxelgine.Engine.DI;

namespace Voxelgine.Engine
{
	/// <summary>
	/// Input source that polls Raylib for keyboard, mouse, and gamepad state.
	/// Used by the local player in both single-player and multiplayer client.
	/// </summary>
	public unsafe class LocalInputSource : IInputSource
	{
		private readonly IFishEngineRunner _eng;
		GameConfig config;

		public LocalInputSource(IFishEngineRunner eng)
		{
			_eng = eng;
		}

		public InputState Poll(float gameTime)
		{
			InputState state = new InputState();
			state.GameTime = gameTime;

			state.MousePos = Raylib.GetMousePosition();
			state.MouseWheel = Raylib.GetMouseWheelMove();

			if (config == null)
				config = _eng.DI.GetRequiredService<GameConfig>();

			for (int i = 0; i < config.MouseButtonDown.Length; i++)
			{
				var kv = config.MouseButtonDown[i];
				state.KeysDown[(int)kv.Key] = Raylib.IsMouseButtonDown(kv.Value);
			}

			for (int i = 0; i < config.KeyDown.Length; i++)
			{
				var kv = config.KeyDown[i];
				state.KeysDown[(int)kv.Key] = Raylib.IsKeyDown(kv.Value);
			}

			for (int i = 0; i < config.TwoKeysDown.Length; i++)
			{
				var kv = config.TwoKeysDown[i];
				state.KeysDown[(int)kv.Key] = Raylib.IsKeyDown(kv.Value.Key) || Raylib.IsKeyDown(kv.Value.Value);
			}

			return state;
		}
	}
}
