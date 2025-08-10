using Raylib_cs;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

using Voxelgine.Engine;

namespace Voxelgine.GUI {
	record struct InventoryChangeEventArgs(GUIItemBox ItmBox);

	class GUIInventory : GUIElement {
		private List<GUIItemBox> ItemBoxes;
		private int SelectedIndex = 0;
		private int ScrollOffset = 0;
		private int VisibleItems = 6; // Number of items visible at once  
		private int MaxItems = 6; // Total inventory slots  
		private float ItemSpacing = 4f;
		private float ItemBoxSize = 64f;

		private bool ScrollLeftPressed = false;
		private bool ScrollRightPressed = false;
		private bool SelectNextPressed = false;
		private bool SelectPrevPressed = false;

		public Action<InventoryChangeEventArgs> OnActiveSelectionChanged;

		public GUIInventory(GUIManager Mgr, GUIElement Parent, int maxItems = 10, int visibleItems = 10) : base(Mgr, Parent) {
			this.MaxItems = maxItems;
			this.VisibleItems = visibleItems;

			// Calculate total size  
			Size = new Vector2(
				VisibleItems * (ItemBoxSize + ItemSpacing) - ItemSpacing,
				ItemBoxSize
			);

			// Initialize item boxes  
			ItemBoxes = new List<GUIItemBox>();

			for (int i = 0; i < MaxItems; i++) {
				GUIItemBox itemBox = new GUIItemBox(Mgr, this);
				itemBox.ParentInv = this;
				itemBox.Size = new Vector2(ItemBoxSize, ItemBoxSize);

				itemBox.OnClickedFunc = (E) => {
					GUIItemBox ths = E as GUIItemBox;
					ths.ParentInv.SetSelectedIndexObject(ths);
				};

				itemBox.FlexNode.nodeStyle.Set("width: 64; height: 64;");
				ItemBoxes.Add(itemBox);
			}

			// Set first item as selected  
			if (ItemBoxes.Count > 0) {
				ItemBoxes[0].IsSelected = true;
			}
		}

		public override GUIUpdateResult Update() {


			base.Update();

			// Handle scrolling input (using A/D keys since mouse wheel isn't in InputMgr)  
			/*bool currentScrollLeft = Raylib.IsKeyDown(KeyboardKey.A) || Raylib.IsKeyDown(KeyboardKey.Left);
			bool currentScrollRight = Raylib.IsKeyDown(KeyboardKey.D) || Raylib.IsKeyDown(KeyboardKey.Right);
			bool currentSelectNext = Raylib.IsKeyDown(KeyboardKey.E);
			bool currentSelectPrev = Raylib.IsKeyDown(KeyboardKey.Q);

			// Scroll left  
			if (currentScrollLeft && !ScrollLeftPressed) {
				ScrollLeft();
			}
			ScrollLeftPressed = currentScrollLeft;

			// Scroll right  
			if (currentScrollRight && !ScrollRightPressed) {
				ScrollRight();
			}
			ScrollRightPressed = currentScrollRight;

			// Select next item  
			if (currentSelectNext && !SelectNextPressed) {
				SelectNext();
			}
			SelectNextPressed = currentSelectNext;

			// Select previous item  
			if (currentSelectPrev && !SelectPrevPressed) {
				SelectPrevious();
			}
			SelectPrevPressed = currentSelectPrev;*/

			// Update positions of visible item boxes  
			//UpdateItemPositions();

			// Update each visible item box  
			for (int i = 0; i < VisibleItems && (i + ScrollOffset) < ItemBoxes.Count; i++) {
				int itemIndex = i + ScrollOffset;
				ItemBoxes[itemIndex].MousePos = MousePos;
				ItemBoxes[itemIndex].Update();
			}

			return GUIUpdateResult.OK;
		}

		private void UpdateItemPositions() {
			for (int i = 0; i < VisibleItems && (i + ScrollOffset) < ItemBoxes.Count; i++) {
				int itemIndex = i + ScrollOffset;
				ItemBoxes[itemIndex].Pos = new Vector2(
					Pos.X + i * (ItemBoxSize + ItemSpacing),
					Pos.Y
				);
			}
		}

		private void ScrollLeft() {
			if (ScrollOffset > 0) {
				ScrollOffset--;
			}
		}

		private void ScrollRight() {
			int maxScroll = Math.Max(0, MaxItems - VisibleItems);
			if (ScrollOffset < maxScroll) {
				ScrollOffset++;
			}
		}

		int LastSelectedIdx = -1;

		void SelectIdx(int Idx) {
			for (int i = 0; i < ItemBoxes.Count; i++) {
				ItemBoxes[i].IsSelected = false;
			}

			ItemBoxes[Idx].IsSelected = true;

			if (LastSelectedIdx != Idx) {
				OnActiveSelectionChanged?.Invoke(new InventoryChangeEventArgs(ItemBoxes[Idx]));
			}

			LastSelectedIdx = Idx;
		}

		public void SelectNext() {
			if (SelectedIndex < MaxItems - 1) {
				SelectedIndex++;
				SelectIdx(SelectedIndex);

				// Auto-scroll if selected item is not visible  
				if (SelectedIndex >= ScrollOffset + VisibleItems) {
					ScrollOffset = SelectedIndex - VisibleItems + 1;
				} else if (SelectedIndex < ScrollOffset) {
					ScrollOffset = SelectedIndex;
				}
			}

			GUIItemBox SelItm = GetSelectedItem();
			SelItm.OnMouseClick();
		}

		public void SelectPrevious() {
			if (SelectedIndex > 0) {
				SelectedIndex--;
				SelectIdx(SelectedIndex);

				// Auto-scroll if selected item is not visible  
				if (SelectedIndex < ScrollOffset) {
					ScrollOffset = SelectedIndex;
				} else if (SelectedIndex >= ScrollOffset + VisibleItems) {
					ScrollOffset = SelectedIndex - VisibleItems + 1;
				}
			}

			GUIItemBox SelItm = GetSelectedItem();
			SelItm.OnMouseClick();
		}

		public void SetItemIcon(int index, Texture2D? icon, float scale = 2.0f) {
			if (index >= 0 && index < ItemBoxes.Count) {
				ItemBoxes[index].SetIcon(icon, scale);
			}
		}

		public void SetItemText(int index, string text) {
			if (index >= 0 && index < ItemBoxes.Count) {
				ItemBoxes[index].Text = text;
			}
		}

		public int GetSelectedIndex() {
			return SelectedIndex;
		}

		public void SetSelectedIndex(int Idx) {
			SelectedIndex = Idx;
			SelectIdx(Idx);
		}

		public void SetSelectedIndexObject(GUIItemBox Obj) {
			if (Obj == null)
				return;

			int Idx = ItemBoxes.IndexOf(Obj);
			if (Idx < 0)
				return;

			SetSelectedIndex(Idx);
		}

		public GUIItemBox GetSelectedItem() {
			return ItemBoxes[SelectedIndex];
		}

		public GUIItemBox GetItem(int Idx) {
			SetSelectedIndex(Idx);
			return GetSelectedItem();
		}

		public override void OnFlexUpdated() {
			base.OnFlexUpdated();

			Pos = new Vector2(FlexNode.layout.x, FlexNode.layout.y);
			Size = new Vector2(FlexNode.layout.width, FlexNode.layout.height);

			for (int i = 0; i < ItemBoxes.Count; i++) {
				ItemBoxes[i].OnFlexUpdated();
			}
		}

		public override void Draw(bool Hovered, bool MouseClicked, bool MouseDown) {
			Raylib.DrawRectangleV(Pos, Size, Color.Pink);

			// Draw only visible item boxes  
			for (int i = 0; i < VisibleItems && (i + ScrollOffset) < ItemBoxes.Count; i++) {
				int itemIndex = i + ScrollOffset;
				var itemBox = ItemBoxes[itemIndex];

				bool itemHovered = itemBox.IsInside(MousePos);
				itemBox.Draw(itemHovered, MouseClicked && itemHovered, MouseDown && itemHovered);
			}

			// Optional: Draw scroll indicators  
			if (ScrollOffset > 0) {
				// Draw left scroll indicator  
				Vector2 leftArrowPos = new Vector2(Pos.X - 20, Pos.Y + Size.Y / 2);
				Mgr.DrawText("<", leftArrowPos, Color.White);
			}

			int maxScroll = Math.Max(0, MaxItems - VisibleItems);
			if (ScrollOffset < maxScroll) {
				// Draw right scroll indicator  
				Vector2 rightArrowPos = new Vector2(Pos.X + Size.X + 10, Pos.Y + Size.Y / 2);
				Mgr.DrawText(">", rightArrowPos, Color.White);
			}
		}
	}
}
