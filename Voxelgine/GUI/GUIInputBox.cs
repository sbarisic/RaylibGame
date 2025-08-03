using Raylib_cs;
using Voxelgine.Engine;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Voxelgine.Graphics;
using Windows.Management.Deployment;

namespace Voxelgine.GUI {
	class GUIInputBox : GUIElement {
		GUIManager Mgr;

		public string Label = "DefaultLabel";
		string Value = "";
		string OriginalValue;
		bool WasEdited = false;

		public bool IsActive = false;
		public Action<string> OnValueChanged;

		GUILabel InputLabel;
		float LabelSpacing = 8f;
		float Padding = 6f;

		public GUIInputBox(GUIManager Mgr, string Label, string Value) {
			this.Mgr = Mgr;
			this.Label = Label;
			this.Value = Value;

			// Create a GUILabel for the input field (single-line, 24 chars by default)
			InputLabel = new GUILabel(Mgr, 24);
			InputLabel.IsInput = true;
			InputLabel.IsReading = false;
			InputLabel.ClearOnEnter = false;
			InputLabel.OnInputFunc = (val) => {
				Value = val;
				OnValueChanged?.Invoke(val);
			};

			InputLabel.DrawTextColor = new Color(130, 172, 209);

			SetValue(Value, Value);

			UpdateLayout();
			WasEdited = false;
		}

		public void SetValue(string Value, string OriginalValue = null) {
			this.Value = Value;
			InputLabel.Clear();
			InputLabel.Input(Value);

			if (OriginalValue != null)
				this.OriginalValue = OriginalValue;

			WasEdited = true;
		}

		private void UpdateLayout() {
			// Arrange label and input in a single row
			Vector2 labelSize = Mgr.MeasureText(Label);
			Vector2 inputSize = InputLabel.Size;
			float width = labelSize.X + LabelSpacing + inputSize.X + Padding * 2;
			float height = MathF.Max(labelSize.Y, inputSize.Y) + Padding * 2;
			Size = new Vector2(width, height);
			// Label at (Padding, center vertically)
			// InputLabel at (Padding + labelSize.X + LabelSpacing, center vertically)
			InputLabel.Pos = new Vector2(Padding + labelSize.X + LabelSpacing, Padding + (height - Padding * 2 - inputSize.Y) / 2);
		}

		public override GUIUpdateResult Update() {
			if (!Enabled)
				return GUIUpdateResult.Disabled;

			// Update input label state
			InputLabel.MousePos = MousePos - InputLabel.Pos;
			InputLabel.Update();

			Value = InputLabel.GetText();

			// Activate input on click
			if (IsInside(MousePos) && Raylib.IsMouseButtonPressed(MouseButton.Left)) {
				if (Raylib.IsKeyDown(KeyboardKey.LeftControl)) {
					if (Value.ToLower() == "true" || Value.ToLower() == "false") {
						bool V = !bool.Parse(Value);
						SetValue(V.ToString());
					}
				}

				IsActive = true;
				InputLabel.IsReading = true;
			} else if (!IsInside(MousePos) && Raylib.IsMouseButtonPressed(MouseButton.Left)) {
				IsActive = false;
				InputLabel.IsReading = false;
			}

			return GUIUpdateResult.OK;
		}

		public override void Draw(bool Hovered, bool MouseClicked, bool MouseDown) {
			// Draw background
			Color bg = IsActive ? new Color(60, 60, 80, 200) : new Color(40, 40, 60, 180);
			Texture2D tex = ResMgr.GetTexture("gui/inputbox.png");

			if (WasEdited) {
				tex = ResMgr.GetTexture("gui/inputbox_edited.png");
				bg = new Color(255, 255, 255, 200); // Highlight color if value changed
			}

			if (Value != OriginalValue) {
				bg = new Color(255, 255, 255, 200); // Highlight color if value changed

				if (WasEdited)
					tex = ResMgr.GetTexture("gui/inputbox_changed_edited.png");
				else
					tex = ResMgr.GetTexture("gui/inputbox_changed.png");
			}

			Mgr.Draw9Patch(tex, new Rectangle(Pos, Size), bg);

			// Enable scissor mode to clip drawing inside the input box
			ScissorManager.BeginScissor(Pos.X, Pos.Y, Size.X, Size.Y);

			// Draw label to the left, vertically centered
			Vector2 labelSize = Mgr.MeasureText(Label);
			float labelY = Pos.Y + Padding + (Size.Y - Padding * 2 - labelSize.Y) / 2;
			Mgr.DrawText(Label, new Vector2(Pos.X + Padding, labelY), Color.White);

			// Draw input field (delegated to GUILabel)
			var oldPos = InputLabel.Pos;
			InputLabel.Pos = Pos + InputLabel.Pos;
			InputLabel.Draw(IsActive, MouseClicked, MouseDown);
			InputLabel.Pos = oldPos;

			ScissorManager.EndScissor();
		}
	}
}
