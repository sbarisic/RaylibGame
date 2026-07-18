#if WINDOWS
using FishGfx.Graphics;
using System.Numerics;
using Voxelgine.Engine;
using Voxelgine.Engine.Input;

namespace Voxelgine.FishGfxClient.Input;

public sealed class FishGfxInputSource : IInputSource, IDisposable
{
	private readonly RenderWindow window;
	private readonly GameConfig config;
	private readonly HashSet<PhysicalKey> keysDown = new();
	private readonly HashSet<PhysicalMouseButton> mouseButtonsDown = new();
	private float wheel;
	private bool disposed;

	public FishGfxInputSource(RenderWindow window, GameConfig config)
	{
		this.window = window ?? throw new ArgumentNullException(nameof(window));
		this.config = config ?? throw new ArgumentNullException(nameof(config));
		window.KeyChanged += OnKeyChanged;
		window.MouseButtonChanged += OnMouseButtonChanged;
		window.Scrolled += OnScrolled;
	}

	public unsafe InputState Poll(float gameTime)
	{
		ObjectDisposedException.ThrowIf(disposed, this);
		InputState state = new()
		{
			GameTime = gameTime,
			MousePos = window.MousePosition,
			MouseWheel = wheel,
		};
		wheel = 0;

		foreach (KeyValuePair<InputKey, PhysicalMouseButton> binding in config.MouseButtonDown)
		{
			state.KeysDown[(int)binding.Key] = mouseButtonsDown.Contains(binding.Value);
		}

		foreach (KeyValuePair<InputKey, PhysicalKey> binding in config.KeyDown)
		{
			state.KeysDown[(int)binding.Key] = keysDown.Contains(binding.Value);
		}

		foreach (KeyValuePair<InputKey, KeyValuePair<PhysicalKey, PhysicalKey>> binding in config.TwoKeysDown)
		{
			state.KeysDown[(int)binding.Key] = keysDown.Contains(binding.Value.Key)
				|| keysDown.Contains(binding.Value.Value);
		}

		return state;
	}

	public void Dispose()
	{
		if (disposed)
		{
			return;
		}

		window.KeyChanged -= OnKeyChanged;
		window.MouseButtonChanged -= OnMouseButtonChanged;
		window.Scrolled -= OnScrolled;
		keysDown.Clear();
		mouseButtonsDown.Clear();
		disposed = true;
	}

	private void OnKeyChanged(object sender, KeyEventArgs args)
	{
		PhysicalKey key = (PhysicalKey)(int)args.Key;
		if (args.IsPressed)
		{
			keysDown.Add(key);
		}
		else
		{
			keysDown.Remove(key);
		}
	}

	private void OnMouseButtonChanged(object sender, MouseButtonEventArgs args)
	{
		PhysicalMouseButton button = (PhysicalMouseButton)(int)args.Button;
		if (args.IsPressed)
		{
			mouseButtonsDown.Add(button);
		}
		else
		{
			mouseButtonsDown.Remove(button);
		}
	}

	private void OnScrolled(object sender, ScrollEventArgs args)
	{
		wheel += args.Offset.Y;
	}
}
#endif
