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

		public bool IsActive = false;
		public Action<string> OnValueChanged;

		private GUILabel InputLabel;
		private float LabelSpacing = 8f;
		private float Padding = 6f;

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

			SetValue(Value, Value);

			UpdateLayout();
		}

		public void SetValue(string Value, string OriginalValue = null) {
			this.Value = Value;
			InputLabel.Clear();
			InputLabel.Input(Value);

			if (OriginalValue != null)
				this.OriginalValue = OriginalValue;
		}

		private void UpdateLayout() {
			// Set size based only on the larger of label or input, not both in a row
			Vector2 labelSize = Mgr.MeasureText(Label);
			Vector2 inputSize = InputLabel.Size;
			float width = MathF.Max(labelSize.X, inputSize.X) + Padding * 2;
			float height = labelSize.Y + inputSize.Y + LabelSpacing + Padding * 2;
			Size = new Vector2(width, height);
			InputLabel.Pos = new Vector2(Padding, Padding + labelSize.Y + LabelSpacing);
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

			if (Value != OriginalValue) {
				int bright = 30;
				bg = new Color(255, 255, 255, 200); // Highlight color if value changed
				tex = ResMgr.GetTexture("gui/inputbox_changed.png"); // Use a different texture for changed state
			}

			Mgr.Draw9Patch(tex, new Rectangle(Pos, Size), bg);

			// Enable scissor mode to clip drawing inside the input box
			ScissorManager.BeginScissor(Pos.X, Pos.Y, Size.X, Size.Y);

			// Draw label above the input field, inside the box
			Mgr.DrawText(Label, Pos + new Vector2(Padding, Padding), Color.White);

			// Draw input field (delegated to GUILabel)
			var oldPos = InputLabel.Pos;
			InputLabel.Pos = Pos + InputLabel.Pos;
			InputLabel.Draw(IsActive, MouseClicked, MouseDown);
			InputLabel.Pos = oldPos;

			ScissorManager.EndScissor();
		}
	}
}
