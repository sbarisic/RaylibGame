using FishUI;
using Voxelgine.Engine.DI;

namespace Voxelgine.GUI;

internal sealed class FishUILoggingAdapter : IFishUILogger
{
	private readonly IFishLogging logging;

	public FishUILoggingAdapter(IFishLogging logging) => this.logging = logging;

	public void Log(string message) => logging.Log(GameLogLevel.Trace, "FishUI", message);

	public void LogControlEvent(string controlType, string controlId, string eventName)
	{
		LogControlEvent(controlType, controlId, eventName, null);
	}

	public void LogControlEvent(string controlType, string controlId, string eventName, string details)
	{
		string identity = string.IsNullOrWhiteSpace(controlId) ? controlType : $"{controlType}#{controlId}";
		string suffix = string.IsNullOrWhiteSpace(details) ? string.Empty : $" {details}";
		logging.Log(GameLogLevel.Trace, "FishUI", $"{identity} {eventName}{suffix}");
	}
}
