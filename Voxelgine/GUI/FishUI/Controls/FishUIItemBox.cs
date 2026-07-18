using FishUI;
using FishUI.Controls;
using System.Numerics;
using Voxelgine.Engine;

namespace Voxelgine.GUI
{
	/// <summary>
	/// FishUI-based item box control for inventory display.
	/// Displays an icon with optional count text in the corner.
	/// Supports atlas regions via UV coordinates.
	/// </summary>
	public class FishUIItemBox : Control
	{
		public bool IsSelected { get; set; }
		public FishUIInventory ParentInventory { get; set; }
		public InventoryItem Item { get; set; }
		public bool UpdateTextFromItem { get; set; }
		public string Text { get; set; }

		private ImageRef _icon;
		private ImageRef _backgroundNormal;
		private ImageRef _backgroundSelected;
		private ImageRef _backgroundHover;
		private ImageRef _backgroundPressed;
		private float _iconScale = 2.0f;
		private bool _hasIcon;

		public event Action<FishUIItemBox> OnItemClicked;

		public FishUIItemBox()
		{
			Size = new Vector2(64, 64);
			Focusable = true;
		}

		public void LoadTextures(global::FishUI.FishUI ui)
		{
			var gfx = ui.Graphics;
			_backgroundNormal = gfx.LoadImage("data/textures/gui/itembox.png");
			_backgroundSelected = gfx.LoadImage("data/textures/gui/itembox_sel.png");
			_backgroundHover = gfx.LoadImage("data/textures/gui/itembox_hover.png");
			_backgroundPressed = gfx.LoadImage("data/textures/gui/itembox_pressed.png");
			gfx.SetImageFilter(_backgroundNormal, true);
			gfx.SetImageFilter(_backgroundSelected, true);
			gfx.SetImageFilter(_backgroundHover, true);
			gfx.SetImageFilter(_backgroundPressed, true);
		}

		public void SetIcon(global::FishUI.FishUI ui, string texturePath, float scale)
		{
			_icon = ui.Graphics.LoadImage(texturePath);
			ui.Graphics.SetImageFilter(_icon, true);
			_iconScale = scale;
			_hasIcon = true;
		}

		public void SetIcon(global::FishUI.FishUI ui, ImageRef icon, float scale)
		{
			_icon = icon;
			ui.Graphics.SetImageFilter(_icon, true);
			_iconScale = scale;
			_hasIcon = true;
		}

		public void SetItem(FishUIInventory parent, InventoryItem item)
		{
			ParentInventory = parent;
			Item = item;
		}

		public override void DrawControl(global::FishUI.FishUI UI, float Dt, float Time)
		{
			var pos = GetAbsolutePosition();
			var size = GetAbsoluteSize();
			var gfx = UI.Graphics;

			// Check if mouse is over this control
			var mousePos = UI.Input.GetMousePosition();
			bool isHovered = mousePos.X >= pos.X && mousePos.X <= pos.X + size.X &&
							 mousePos.Y >= pos.Y && mousePos.Y <= pos.Y + size.Y;

			// Determine which background to use based on state
			ImageRef bgImage;
			if (IsMousePressed && _backgroundPressed.Userdata != null)
			{
				bgImage = _backgroundPressed;
			}
			else if (IsSelected && _backgroundSelected.Userdata != null)
			{
				bgImage = _backgroundSelected;
			}
			else if (isHovered && _backgroundHover.Userdata != null)
			{
				bgImage = _backgroundHover;
			}
			else
			{
				bgImage = _backgroundNormal;
			}

			// Draw background
			if (bgImage.Userdata != null)
			{
				gfx.DrawImage(bgImage, pos, size, 0, 1, FishColor.White);
			}
			else
			{
				// Fallback rectangle
				var color = IsSelected ? new FishColor(100, 150, 200, 255) : new FishColor(60, 60, 60, 255);
				gfx.DrawRectangle(pos, size, color);
				gfx.DrawRectangleOutline(pos, size, FishColor.White);
			}

			// Draw icon
			if (_hasIcon)
			{
				float tint = IsMousePressed ? 0.7f : 1.0f;
				byte tintByte = (byte)(255 * tint);
				var tintColor = new FishColor(tintByte, tintByte, tintByte, 255);

				if (_icon.Userdata != null)
				{
					float scaledSize = Math.Clamp(_iconScale, 0.1f, 4f);
					float iconDrawSize = Math.Min(size.X, size.Y)
						* Math.Clamp(0.7f * scaledSize, 0.1f, 0.9f);
					var iconPos = pos + (size - new Vector2(iconDrawSize)) / 2;
					gfx.DrawImage(_icon, iconPos, new Vector2(iconDrawSize), 0, 1, tintColor);
				}
			}

			// Update text from item if needed
			if (UpdateTextFromItem)
			{
				Text = Item?.GetInvText();
			}

			// Draw count text in bottom-right corner
			if (!string.IsNullOrEmpty(Text))
			{
				var font = UI.Settings.FontDefault;
				var textSize = gfx.MeasureText(font, Text);
				var textPos = pos + size - textSize - new Vector2(4, 2);

				// Draw outline
				for (int ox = -1; ox <= 1; ox++)
				{
					for (int oy = -1; oy <= 1; oy++)
					{
						if (ox != 0 || oy != 0)
						{
							gfx.DrawTextColor(font, Text, textPos + new Vector2(ox, oy), FishColor.Black);
						}
					}
				}
				gfx.DrawTextColor(font, Text, textPos, FishColor.White);
			}
		}

		public override void HandleMouseClick(global::FishUI.FishUI UI, FishInputState InputState, FishMouseButton Btn, Vector2 LocalPos)
		{
			base.HandleMouseClick(UI, InputState, Btn, LocalPos);
			if (Btn == FishMouseButton.Left)
			{
				OnItemClicked?.Invoke(this);
			}
		}
	}
}
