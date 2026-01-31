using FishUI;
using FishUI.Controls;
using System.Numerics;
using Voxelgine.Engine;

namespace Voxelgine.GUI {
    /// <summary>
    /// Event args for inventory selection changes.
    /// </summary>
    public record struct FishUIInventoryChangeEventArgs(FishUIItemBox ItemBox);

    /// <summary>
    /// FishUI-based inventory control displaying a horizontal row of item boxes.
    /// </summary>
    public class FishUIInventory : Control {
        private List<FishUIItemBox> _itemBoxes = new();
        private int _selectedIndex;
        private int _maxItems;
        private float _itemSpacing = 4f;
        private float _itemBoxSize = 64f;

        public Action<FishUIInventoryChangeEventArgs> OnActiveSelectionChanged;

        public FishUIInventory(global::FishUI.FishUI ui, int maxItems = 10) {
            _maxItems = maxItems;

            // Calculate total size
            Size = new Vector2(
                maxItems * (_itemBoxSize + _itemSpacing) - _itemSpacing,
                _itemBoxSize
            );

            // Create item boxes
            for (int i = 0; i < maxItems; i++) {
                var itemBox = new FishUIItemBox {
                    Size = new Vector2(_itemBoxSize, _itemBoxSize),
                    Position = new Vector2(i * (_itemBoxSize + _itemSpacing), 0)
                };

                itemBox.LoadTextures(ui);
                itemBox.OnItemClicked += OnItemBoxClicked;

                AddChild(itemBox);
                _itemBoxes.Add(itemBox);
            }

            // Select first item
            if (_itemBoxes.Count > 0) {
                _itemBoxes[0].IsSelected = true;
            }
        }

        private void OnItemBoxClicked(FishUIItemBox itemBox) {
            int index = _itemBoxes.IndexOf(itemBox);
            if (index >= 0) {
                SetSelectedIndex(index);
            }
        }

        public FishUIItemBox GetItem(int index) {
            if (index >= 0 && index < _itemBoxes.Count) {
                return _itemBoxes[index];
            }
            return null;
        }

        public void SetSelectedIndex(int index) {
            if (index < 0 || index >= _itemBoxes.Count) return;
            if (index == _selectedIndex) return;

            // Deselect old
            if (_selectedIndex >= 0 && _selectedIndex < _itemBoxes.Count) {
                _itemBoxes[_selectedIndex].IsSelected = false;
            }

            // Select new
            _selectedIndex = index;
            _itemBoxes[_selectedIndex].IsSelected = true;

            OnActiveSelectionChanged?.Invoke(new FishUIInventoryChangeEventArgs(_itemBoxes[_selectedIndex]));
        }

        public void SelectNext() {
            int next = (_selectedIndex + 1) % _itemBoxes.Count;
            SetSelectedIndex(next);
        }

        public void SelectPrevious() {
            int prev = _selectedIndex - 1;
            if (prev < 0) prev = _itemBoxes.Count - 1;
            SetSelectedIndex(prev);
        }

        public int GetSelectedIndex() => _selectedIndex;

        public FishUIItemBox GetSelectedItem() {
            if (_selectedIndex >= 0 && _selectedIndex < _itemBoxes.Count) {
                return _itemBoxes[_selectedIndex];
            }
            return null;
        }

        public override void DrawControl(global::FishUI.FishUI UI, float Dt, float Time) {
            // Item boxes are children and will be drawn automatically
        }
    }
}
