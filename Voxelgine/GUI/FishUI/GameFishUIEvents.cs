using FishUI;
using FishUI.Controls;
using System.Diagnostics;
using Voxelgine.Engine.DI;

namespace Voxelgine.GUI;

internal sealed class GameFishUIEvents : IFishUIEvents
{
	private readonly IFishLogging logging;

	internal GameFishUIEvents(IFishLogging logging)
	{
		this.logging = logging;
	}

	public void Broadcast(
		global::FishUI.FishUI fishUI,
		Control control,
		string name,
		params object[] arguments)
	{
		LogEvent(control, name);
	}

	[Conditional("DEBUG")]
	private void LogEvent(Control control, string name)
	{
		logging.WriteLine($"[FishUI Event] {control?.GetType().Name ?? "null"}: {name}");
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
