namespace Voxelgine.States;

internal enum GameplayInputMode
{
	Gameplay,
	DebugMenu,
	Chat,
}

/// <summary>
/// Tracks which multiplayer surface owns input independently from FishUI controls.
/// </summary>
internal sealed class GameplayInputOwnership
{
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
		if (Mode == GameplayInputMode.Chat)
		{
			return false;
		}

		Mode = Mode == GameplayInputMode.DebugMenu
			? GameplayInputMode.Gameplay
			: GameplayInputMode.DebugMenu;
		return true;
	}

	public void CloseOverlay()
	{
		Mode = GameplayInputMode.Gameplay;
	}

	public void ResetMode()
	{
		Mode = GameplayInputMode.Gameplay;
	}
}
