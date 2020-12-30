// Gui control state
typedef enum {
	GUI_STATE_NORMAL = 0,
	GUI_STATE_FOCUSED,
	GUI_STATE_PRESSED,
	GUI_STATE_DISABLED,
} GuiControlState;

// Gui control text alignment
typedef enum {
	GUI_TEXT_ALIGN_LEFT = 0,
	GUI_TEXT_ALIGN_CENTER,
	GUI_TEXT_ALIGN_RIGHT,
} GuiTextAlignment;

// Gui controls
typedef enum {
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
} GuiControl;

// Gui base properties for every control
typedef enum {
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
} GuiControlProperty;

// Gui extended properties depend on control
// NOTE: We reserve a fixed size of additional properties per control

// DEFAULT properties
typedef enum {
	TEXT_SIZE = 16,
	TEXT_SPACING,
	LINE_COLOR,
	BACKGROUND_COLOR,
} GuiDefaultProperty;

// Label
//typedef enum { } GuiLabelProperty;

// Button
//typedef enum { } GuiButtonProperty;

// Toggle / ToggleGroup
typedef enum {
	GROUP_PADDING = 16,
} GuiToggleProperty;

// Slider / SliderBar
typedef enum {
	SLIDER_WIDTH = 16,
	TEXT_PADDING
} GuiSliderProperty;

// ProgressBar
//typedef enum { } GuiProgressBarProperty;

// CheckBox
typedef enum {
	CHECK_TEXT_PADDING = 16
} GuiCheckBoxProperty;

// ComboBox
typedef enum {
	SELECTOR_WIDTH = 16,
	SELECTOR_PADDING
} GuiComboBoxProperty;

// DropdownBox
typedef enum {
	ARROW_RIGHT_PADDING = 16,
	ITEMS_PADDING
} GuiDropdownBoxProperty;

// TextBox / TextBoxMulti / ValueBox / Spinner
typedef enum {
	MULTILINE_PADDING = 16,
	COLOR_SELECTED_FG,
	COLOR_SELECTED_BG
} GuiTextBoxProperty;

typedef enum {
	SELECT_BUTTON_WIDTH = 16,
	SELECT_BUTTON_PADDING,
	SELECT_BUTTON_BORDER_WIDTH
} GuiSpinnerProperty;

// ScrollBar
typedef enum {
	ARROWS_SIZE = 16,
	SLIDER_PADDING,
	SLIDER_SIZE,
	SCROLL_SPEED,
	ARROWS_VISIBLE
} GuiScrollBarProperty;

// ScrollBar side
typedef enum {
	SCROLLBAR_LEFT_SIDE = 0,
	SCROLLBAR_RIGHT_SIDE
} GuiScrollBarSide;

// ListView
typedef enum {
	ELEMENTS_HEIGHT = 16,
	ELEMENTS_PADDING,
	SCROLLBAR_WIDTH,
	SCROLLBAR_SIDE,             // This property defines vertical scrollbar side (SCROLLBAR_LEFT_SIDE or SCROLLBAR_RIGHT_SIDE)
} GuiListViewProperty;

// ColorPicker
typedef enum {
	COLOR_SELECTOR_SIZE = 16,
	BAR_WIDTH,                  // Lateral bar width
	BAR_PADDING,                // Lateral bar separation from panel
	BAR_SELECTOR_HEIGHT,        // Lateral bar selector height
	BAR_SELECTOR_PADDING        // Lateral bar selector outer padding
} GuiColorPickerProperty;

//----------------------------------------------------------------------------------
// Global Variables Definition
//----------------------------------------------------------------------------------
// ...

//----------------------------------------------------------------------------------
// Module Functions Declaration
//----------------------------------------------------------------------------------

// Global gui modification functions
 void GuiEnable(void);                                         // Enable gui controls (global state)
 void GuiDisable(void);                                        // Disable gui controls (global state)
 void GuiLock(void);                                           // Lock gui controls (global state)
 void GuiUnlock(void);                                         // Unlock gui controls (global state)
 void GuiState(int state);                                     // Set gui state (global state)
 void GuiFont(Font font);                                      // Set gui custom font (global state)
 void GuiFade(float alpha);                                    // Set gui controls alpha (global state), alpha goes from 0.0f to 1.0f

// Style set/get functions
 void GuiSetStyle(int control, int property, int value);       // Set one style property
 int GuiGetStyle(int control, int property);                   // Get one style property

 
 
// Container/separator controls, useful for controls organization
 bool GuiWindowBox(Rectangle bounds, const char *text);                                        // Window Box control, shows a window that can be closed
 void GuiGroupBox(Rectangle bounds, const char *text);                                         // Group Box control with title name
 void GuiLine(Rectangle bounds, const char *text);                                             // Line separator control, could contain text
 void GuiPanel(Rectangle bounds);                                                              // Panel control, useful to group controls
 Rectangle GuiScrollPanel(Rectangle bounds, Rectangle content, Vector2 *scroll);               // Scroll Panel control

// Basic controls set
 void GuiLabel(Rectangle bounds, const char *text);                                            // Label control, shows text
 bool GuiButton(Rectangle bounds, const char *text);                                           // Button control, returns true when clicked
 bool GuiLabelButton(Rectangle bounds, const char *text);                                      // Label button control, show true when clicked
 bool GuiImageButton(Rectangle bounds, Texture2D texture);                                     // Image button control, returns true when clicked
 bool GuiImageButtonEx(Rectangle bounds, Texture2D texture, Rectangle texSource, const char *text);        // Image button extended control, returns true when clicked
 bool GuiToggle(Rectangle bounds, const char *text, bool active);                              // Toggle Button control, returns true when active
 int GuiToggleGroup(Rectangle bounds, const char *text, int active);                           // Toggle Group control, returns active toggle index
 bool GuiCheckBox(Rectangle bounds, const char *text, bool checked);                           // Check Box control, returns true when active
 int GuiComboBox(Rectangle bounds, const char *text, int active);                              // Combo Box control, returns selected item index
 bool GuiDropdownBox(Rectangle bounds, const char *text, int *active, bool editMode);          // Dropdown Box control, returns selected item
 bool GuiSpinner(Rectangle bounds, int *value, int minValue, int maxValue, bool editMode);     // Spinner control, returns selected value
 bool GuiValueBox(Rectangle bounds, int *value, int minValue, int maxValue, bool editMode);    // Value Box control, updates input text with numbers
 bool GuiTextBox(Rectangle bounds, char *text, int textSize, bool editMode);                   // Text Box control, updates input text
 bool GuiTextBoxMulti(Rectangle bounds, char *text, int textSize, bool editMode);              // Text Box control with multiple lines
 float GuiSlider(Rectangle bounds, const char *text, float value, float minValue, float maxValue, bool showValue);       // Slider control, returns selected value
 float GuiSliderBar(Rectangle bounds, const char *text, float value, float minValue, float maxValue, bool showValue);    // Slider Bar control, returns selected value
 float GuiProgressBar(Rectangle bounds, const char *text, float value, float minValue, float maxValue, bool showValue);  // Progress Bar control, shows current progress value
 void GuiStatusBar(Rectangle bounds, const char *text);                                        // Status Bar control, shows info text
 void GuiDummyRec(Rectangle bounds, const char *text);                                         // Dummy control for placeholders
 int GuiScrollBar(Rectangle bounds, int value, int minValue, int maxValue);                    // Scroll Bar control
 Vector2 GuiGrid(Rectangle bounds, float spacing, int subdivs);                                // Grid control

// Advance controls set
 int GuiListView(Rectangle bounds, const char *text, int *scrollIndex, int active);            // List View control, returns selected list element index
 int GuiListViewEx(Rectangle bounds, const char **text, int count, int *focus, int *scrollIndex, int active);  // List View with extended parameters
 int GuiMessageBox(Rectangle bounds, const char *windowTitle, const char *message, const char *buttons);       // Message Box control, displays a message
 int GuiTextInputBox(Rectangle bounds, const char *windowTitle, char *text, const char *buttons);              // Text Input Box control, ask for text
 Color GuiColorPicker(Rectangle bounds, Color color);                                          // Color Picker control

// Styles loading functions
 void GuiLoadStyle(const char *fileName);              // Load style file (.rgs)
 void GuiLoadStyleProps(const int *props, int count);  // Load style properties from array
 void GuiLoadStyleDefault(void);                       // Load style default over global style
 void GuiUpdateStyleComplete(void);                    // Updates full style properties set with default values

/*
typedef GuiStyle (unsigned int *)
RAYGUIDEF GuiStyle LoadGuiStyle(const char *fileName);          // Load style from file (.rgs)
RAYGUIDEF void UnloadGuiStyle(GuiStyle style);                  // Unload style
*/

 const char *GuiIconText(int iconId, const char *text); // Get text with icon id prepended