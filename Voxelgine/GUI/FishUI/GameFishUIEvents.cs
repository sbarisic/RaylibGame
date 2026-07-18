using FishUI;
using FishUI.Controls;

namespace Voxelgine.GUI;

internal sealed class GameFishUIEvents : IFishUIEvents
{
	public void Broadcast(
		global::FishUI.FishUI fishUI,
		Control control,
		string name,
		params object[] arguments)
	{
		// FishUIDebug emits structured control diagnostics through the game adapter.
	}

	public void OnControlClicked(FishUIClickEventArgs args) { }
	public void OnControlDoubleClicked(FishUIClickEventArgs args) { }
	public void OnControlMouseEnter(FishUIMouseEventArgs args) { }
	public void OnControlMouseLeave(FishUIMouseEventArgs args) { }
	public void OnControlValueChanged(FishUIValueChangedEventArgs args) { }
	public void OnControlSelectionChanged(FishUISelectionChangedEventArgs args) { }
	public void OnControlTextChanged(FishUITextChangedEventArgs args) { }
	public void OnControlCheckedChanged(FishUICheckedChangedEventArgs args) { }
	public void OnLayoutLoaded(FishUILayoutLoadedEventArgs args) { }
}
