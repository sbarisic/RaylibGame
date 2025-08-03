using Raylib_cs;
using Voxelgine.Engine;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Voxelgine.Graphics;

namespace Voxelgine.GUI {
	class GUIInputBox : GUIElement {
		GUIManager Mgr;

		public string Label = "DefaultLabel";
		public string Value = "";
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
			InputLabel.Input(Value);
			UpdateLayout();
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
			Mgr.Draw9Patch(ResMgr.GetTexture("gui/btn.png"), new Rectangle(Pos, Size), bg);

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
