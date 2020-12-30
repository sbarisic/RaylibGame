using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace RaylibSharp {
	// Gui control state
	public enum GuiControlState {
		GUI_STATE_NORMAL = 0,
		GUI_STATE_FOCUSED,
		GUI_STATE_PRESSED,
		GUI_STATE_DISABLED,
	}


	// Gui control text alignment
	public enum GuiTextAlignment {
		GUI_TEXT_ALIGN_LEFT = 0,
		GUI_TEXT_ALIGN_CENTER,
		GUI_TEXT_ALIGN_RIGHT,
	}
	
	// Gui controls
	public enum GuiControl {
		DEFAULT = 0,
		LABEL,          // LABELBUTTON
		BUTTON,         // IMAGEBUTTON
		TOGGLE,         // TOGGLEGROUP
		SLIDER,         // SLIDERBAR
		PROGRESSBAR,
		CHECKBOX,
		COMBOBOX,
		DROPDOWNBOX,
		TEXTBOX,        // TEXTBOXMULTI
		VALUEBOX,
		SPINNER,
		LISTVIEW,
		COLORPICKER,
		SCROLLBAR,
		RESERVED
	}

	// Gui base properties for every control
	public enum GuiControlProperty {
		BORDER_COLOR_NORMAL = 0,
		BASE_COLOR_NORMAL,
		TEXT_COLOR_NORMAL,
		BORDER_COLOR_FOCUSED,
		BASE_COLOR_FOCUSED,
		TEXT_COLOR_FOCUSED,
		BORDER_COLOR_PRESSED,
		BASE_COLOR_PRESSED,
		TEXT_COLOR_PRESSED,
		BORDER_COLOR_DISABLED,
		BASE_COLOR_DISABLED,
		TEXT_COLOR_DISABLED,
		BORDER_WIDTH,
		INNER_PADDING,
		TEXT_ALIGNMENT,
		RESERVED02
	}


	// Gui extended properties depend on control
	// NOTE: We reserve a fixed size of additional properties per control

	// DEFAULT properties
	public enum GuiDefaultProperty {
		TEXT_SIZE = 16,
		TEXT_SPACING,
		LINE_COLOR,
		BACKGROUND_COLOR,
	}

	// Label
	//typedef public enum  { } GuiLabelProperty;

	// Button
	//typedef public enum  { } GuiButtonProperty;

	// Toggle / ToggleGroup
	public enum GuiToggleProperty {
		GROUP_PADDING = 16,
	}


	// Slider / SliderBar
	public enum GuiSliderProperty {
		SLIDER_WIDTH = 16,
		TEXT_PADDING
	}


	// ProgressBar
	//typedef public enum  { } GuiProgressBarProperty;

	// CheckBox
	public enum GuiCheckBoxProperty {
		CHECK_TEXT_PADDING = 16
	}


	// ComboBox
	public enum GuiComboBoxProperty {
		SELECTOR_WIDTH = 16,
		SELECTOR_PADDING
	}


	// DropdownBox
	public enum GuiDropdownBoxProperty {
		ARROW_RIGHT_PADDING = 16,
		ITEMS_PADDING
	}
	   ;

	// TextBox / TextBoxMulti / ValueBox / Spinner
	public enum GuiTextBoxProperty {
		MULTILINE_PADDING = 16,
		COLOR_SELECTED_FG,
		COLOR_SELECTED_BG
	}

	public enum GuiSpinnerProperty {
		SELECT_BUTTON_WIDTH = 16,
		SELECT_BUTTON_PADDING,
		SELECT_BUTTON_BORDER_WIDTH
	}


	// ScrollBar
	public enum GuiScrollBarProperty {
		ARROWS_SIZE = 16,
		SLIDER_PADDING,
		SLIDER_SIZE,
		SCROLL_SPEED,
		ARROWS_VISIBLE
	}

	// ScrollBar side
	public enum GuiScrollBarSide {
		SCROLLBAR_LEFT_SIDE = 0,
		SCROLLBAR_RIGHT_SIDE
	}

	// ListView
	public enum GuiListViewProperty {
		ELEMENTS_HEIGHT = 16,
		ELEMENTS_PADDING,
		SCROLLBAR_WIDTH,
		SCROLLBAR_SIDE,             // This property defines vertical scrollbar side (SCROLLBAR_LEFT_SIDE or SCROLLBAR_RIGHT_SIDE)
	}

	// ColorPicker
	public enum GuiColorPickerProperty {
		COLOR_SELECTOR_SIZE = 16,
		BAR_WIDTH,                  // Lateral bar width
		BAR_PADDING,                // Lateral bar separation from panel
		BAR_SELECTOR_HEIGHT,        // Lateral bar selector height
		BAR_SELECTOR_PADDING        // Lateral bar selector outer padding
	}

	public unsafe static class Raygui {
		const string LibName = "raylib";
		const CallingConvention CConv = CallingConvention.Cdecl;
		const CharSet CSet = CharSet.Ansi;


		// Global gui modification functions

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void GuiEnable();                                         // Enable gui controls (global state)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void GuiDisable();                                        // Disable gui controls (global state)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void GuiLock();                                           // Lock gui controls (global state)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void GuiUnlock();                                         // Unlock gui controls (global state)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void GuiState(int state);                                     // Set gui state (global state)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void GuiFont(Font font);                                      // Set gui custom font (global state)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void GuiFade(float alpha);                                    // Set gui controls alpha (global state), alpha goes from 0.0f to 1.0f

		// Style set/get functions

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void GuiSetStyle(int control, int property, int value);       // Set one style property

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern int GuiGetStyle(int control, int property);                   // Get one style property



		// Container/separator controls, useful for controls organization

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern bool GuiWindowBox(Rectangle bounds, string text);                                        // Window Box control, shows a window that can be closed

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void GuiGroupBox(Rectangle bounds, string text);                                         // Group Box control with title name

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void GuiLine(Rectangle bounds, string text);                                             // Line separator control, could contain text

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void GuiPanel(Rectangle bounds);                                                              // Panel control, useful to group controls

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern Rectangle GuiScrollPanel(Rectangle bounds, Rectangle content, Vector2* scroll);               // Scroll Panel control

		// Basic controls set

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void GuiLabel(Rectangle bounds, string text);                                            // Label control, shows text

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern bool GuiButton(Rectangle bounds, string text);                                           // Button control, returns true when clicked

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern bool GuiLabelButton(Rectangle bounds, string text);                                      // Label button control, show true when clicked

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern bool GuiImageButton(Rectangle bounds, Texture2D texture);                                     // Image button control, returns true when clicked

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern bool GuiImageButtonEx(Rectangle bounds, Texture2D texture, Rectangle texSource, string text);        // Image button extended control, returns true when clicked

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern bool GuiToggle(Rectangle bounds, string text, bool active);                              // Toggle Button control, returns true when active

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern int GuiToggleGroup(Rectangle bounds, string text, int active);                           // Toggle Group control, returns active toggle index

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern bool GuiCheckBox(Rectangle bounds, string text, bool checked_);                           // Check Box control, returns true when active

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern int GuiComboBox(Rectangle bounds, string text, int active);                              // Combo Box control, returns selected item index

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern bool GuiDropdownBox(Rectangle bounds, string text, int* active, bool editMode);          // Dropdown Box control, returns selected item

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern bool GuiSpinner(Rectangle bounds, int* value, int minValue, int maxValue, bool editMode);     // Spinner control, returns selected value

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern bool GuiValueBox(Rectangle bounds, int* value, int minValue, int maxValue, bool editMode);    // Value Box control, updates input text with numbers

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern bool GuiTextBox(Rectangle bounds, char* text, int textSize, bool editMode);                   // Text Box control, updates input text

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern bool GuiTextBoxMulti(Rectangle bounds, char* text, int textSize, bool editMode);              // Text Box control with multiple lines

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern float GuiSlider(Rectangle bounds, string text, float value, float minValue, float maxValue, bool showValue);       // Slider control, returns selected value

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern float GuiSliderBar(Rectangle bounds, string text, float value, float minValue, float maxValue, bool showValue);    // Slider Bar control, returns selected value

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern float GuiProgressBar(Rectangle bounds, string text, float value, float minValue, float maxValue, bool showValue);  // Progress Bar control, shows current progress value

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void GuiStatusBar(Rectangle bounds, string text);                                        // Status Bar control, shows info text

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void GuiDummyRec(Rectangle bounds, string text);                                         // Dummy control for placeholders

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern int GuiScrollBar(Rectangle bounds, int value, int minValue, int maxValue);                    // Scroll Bar control

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern Vector2 GuiGrid(Rectangle bounds, float spacing, int subdivs);                                // Grid control

		// Advance controls set

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern int GuiListView(Rectangle bounds, string text, int* scrollIndex, int active);            // List View control, returns selected list element index

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern int GuiListViewEx(Rectangle bounds, string[] text, int count, int* focus, int* scrollIndex, int active);  // List View with extended parameters

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern int GuiMessageBox(Rectangle bounds, string windowTitle, string message, string buttons);       // Message Box control, displays a message

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern int GuiTextInputBox(Rectangle bounds, string windowTitle, char* text, string buttons);              // Text Input Box control, ask for text

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern Color GuiColorPicker(Rectangle bounds, Color color);                                          // Color Picker control

		// Styles loading functions

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void GuiLoadStyle(string fileName);              // Load style file (.rgs)

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void GuiLoadStyleProps(int* props, int count);  // Load style properties from array

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void GuiLoadStyleDefault();                       // Load style default over global style

		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern void GuiUpdateStyleComplete();                    // Updates full style properties set with default values


		[DllImport(LibName, CallingConvention = CConv, CharSet = CSet)]
		public static extern string GuiIconText(int iconId, string text); // Get text with icon id prepended
	}
}
