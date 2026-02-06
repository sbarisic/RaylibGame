using FishUI;
using FishUI.Controls;
using Voxelgine.Engine.DI;

namespace Voxelgine.GUI {
    /// <summary>
    /// Default event handler for FishUI - logs events to console in debug mode.
    /// </summary>
    public class RaylibFishUIEvents : IFishUIEvents {
        private readonly IFishLogging _logging;

        public RaylibFishUIEvents(IFishLogging logging) {
            _logging = logging;
        }

        public void Broadcast(global::FishUI.FishUI FUI, Control Ctrl, string Name, params object[] Args) {
#if DEBUG
            _logging.WriteLine($"[FishUI Event] {Ctrl?.GetType().Name ?? "null"}: {Name}");
#endif
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
}
