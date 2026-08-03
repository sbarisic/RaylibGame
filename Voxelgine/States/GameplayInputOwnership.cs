namespace Voxelgine.States;

internal enum GameplayInputMode
{
	Gameplay,
	DebugMenu,
	Chat,
	Inventory,
	DeveloperConsole,
}

/// <summary>
/// Tracks which multiplayer surface owns input independently from FishUI controls.
/// </summary>
internal sealed class GameplayInputOwnership
{
	private GameplayInputMode modeBeforeDeveloperConsole = GameplayInputMode.Gameplay;

	public GameplayInputMode Mode { get; private set; } = GameplayInputMode.Gameplay;

	public bool IsStateActive { get; private set; }

	public bool UiInputEnabled => IsStateActive;

	public bool GameplayInputSuppressed => Mode != GameplayInputMode.Gameplay;

	public bool CursorCaptured => IsStateActive && Mode == GameplayInputMode.Gameplay;

	public void Activate()
	{
		IsStateActive = true;
	}

	public void Deactivate()
	{
		IsStateActive = false;
	}

	public bool OpenChat()
	{
		if (Mode != GameplayInputMode.Gameplay)
		{
			return false;
		}

		Mode = GameplayInputMode.Chat;
		return true;
	}

	public bool ToggleDebugMenu()
	{
		if (Mode is GameplayInputMode.Chat or GameplayInputMode.Inventory or GameplayInputMode.DeveloperConsole)
		{
			return false;
		}

		Mode = Mode == GameplayInputMode.DebugMenu
			? GameplayInputMode.Gameplay
			: GameplayInputMode.DebugMenu;
		return true;
	}

	public bool OpenInventory()
	{
		if (Mode != GameplayInputMode.Gameplay)
			return false;

		Mode = GameplayInputMode.Inventory;
		return true;
	}

	public bool SetDeveloperConsoleOpen(bool open)
	{
		if (open)
		{
			if (Mode == GameplayInputMode.DeveloperConsole)
				return false;
			modeBeforeDeveloperConsole = Mode;
			Mode = GameplayInputMode.DeveloperConsole;
			return true;
		}

		if (Mode != GameplayInputMode.DeveloperConsole)
			return false;
		Mode = modeBeforeDeveloperConsole == GameplayInputMode.DeveloperConsole
			? GameplayInputMode.Gameplay
			: modeBeforeDeveloperConsole;
		modeBeforeDeveloperConsole = GameplayInputMode.Gameplay;
		return true;
	}

	public void CloseOverlay()
	{
		Mode = GameplayInputMode.Gameplay;
	}

	public void ResetMode()
	{
		Mode = GameplayInputMode.Gameplay;
		modeBeforeDeveloperConsole = GameplayInputMode.Gameplay;
	}
}
