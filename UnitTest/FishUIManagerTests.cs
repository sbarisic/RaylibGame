using FishUI;
using FishUI.Controls;
using Voxelgine.GUI;

namespace UnitTest;

public sealed class FishUIManagerTests
{
	[Fact]
	public void DeveloperConsoleUsesTildeHotkeyAndStartsClosed()
	{
		GameConsole console = FishUIManager.CreateDeveloperConsole();

		Assert.Equal("developer_console", console.ID);
		Assert.Equal(FishKey.Grave, console.ToggleKey);
		Assert.Equal(FishKeyModifiers.None, console.ToggleModifiers);
		Assert.False(console.IsOpen);
		Assert.Contains(console.Commands, command => command.Name == "help");
	}
}
