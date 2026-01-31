using FishUI;
using FishUI.Controls;

namespace Voxelgine.GUI {
    /// <summary>
    /// Default event handler for FishUI - logs events to console in debug mode.
    /// </summary>
    public class RaylibFishUIEvents : IFishUIEvents {
        public void Broadcast(FishUI.FishUI FUI, Control Ctrl, string Name, params object[] Args) {
#if DEBUG
            Console.WriteLine($"[FishUI Event] {Ctrl?.GetType().Name ?? "null"}: {Name}");
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
