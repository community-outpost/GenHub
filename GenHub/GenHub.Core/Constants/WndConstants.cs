namespace GenHub.Core.Constants;

/// <summary>
/// Constants for window definition (.wnd) menu layout files used by Generals and Zero Hour.
/// </summary>
public static class WndConstants
{
    /// <summary>
    /// Block tag literals that delimit layout blocks, windows, and child sections.
    /// </summary>
    public static class BlockTags
    {
        /// <summary>Opens the file layout metadata block.</summary>
        public const string StartLayoutBlock = "STARTLAYOUTBLOCK";

        /// <summary>Closes the file layout metadata block.</summary>
        public const string EndLayoutBlock = "ENDLAYOUTBLOCK";

        /// <summary>Opens a window definition.</summary>
        public const string Window = "WINDOW";

        /// <summary>Closes a window definition.</summary>
        public const string End = "END";

        /// <summary>Opens a single child window slot.</summary>
        public const string Child = "CHILD";

        /// <summary>Closes the child window section of a window.</summary>
        public const string EndAllChildren = "ENDALLCHILDREN";
    }

    /// <summary>
    /// Property keys parsed structurally. All other keys pass through generically.
    /// </summary>
    public static class PropertyKeys
    {
        /// <summary>File format version key.</summary>
        public const string FileVersion = "FILE_VERSION";

        /// <summary>Control type key present on every window.</summary>
        public const string WindowType = "WINDOWTYPE";

        /// <summary>Geometry and creation resolution key.</summary>
        public const string ScreenRect = "SCREENRECT";

        /// <summary>Display name key.</summary>
        public const string Name = "NAME";

        /// <summary>Status bit flags key.</summary>
        public const string Status = "STATUS";

        /// <summary>Gadget style key.</summary>
        public const string Style = "STYLE";

        /// <summary>System callback key.</summary>
        public const string SystemCallback = "SYSTEMCALLBACK";

        /// <summary>Input callback key.</summary>
        public const string InputCallback = "INPUTCALLBACK";

        /// <summary>Tooltip callback key.</summary>
        public const string TooltipCallback = "TOOLTIPCALLBACK";

        /// <summary>Draw callback key.</summary>
        public const string DrawCallback = "DRAWCALLBACK";

        /// <summary>Font key.</summary>
        public const string Font = "FONT";

        /// <summary>Header template key.</summary>
        public const string HeaderTemplate = "HEADERTEMPLATE";

        /// <summary>List box data key.</summary>
        public const string ListboxData = "LISTBOXDATA";

        /// <summary>Combo box data key.</summary>
        public const string ComboBoxData = "COMBOBOXDATA";

        /// <summary>Slider data key.</summary>
        public const string SliderData = "SLIDERDATA";

        /// <summary>Radio button data key.</summary>
        public const string RadioButtonData = "RADIOBUTTONDATA";

        /// <summary>Tooltip text key.</summary>
        public const string TooltipText = "TOOLTIPTEXT";

        /// <summary>Tooltip delay key.</summary>
        public const string TooltipDelay = "TOOLTIPDELAY";

        /// <summary>Text label key.</summary>
        public const string Text = "TEXT";

        /// <summary>Text color key.</summary>
        public const string TextColor = "TEXTCOLOR";

        /// <summary>Static text data key.</summary>
        public const string StaticTextData = "STATICTEXTDATA";

        /// <summary>Text entry data key.</summary>
        public const string TextEntryData = "TEXTENTRYDATA";

        /// <summary>Tab control data key.</summary>
        public const string TabControlData = "TABCONTROLDATA";

        /// <summary>Enabled draw data key.</summary>
        public const string EnabledDrawData = "ENABLEDDRAWDATA";

        /// <summary>Disabled draw data key.</summary>
        public const string DisabledDrawData = "DISABLEDDRAWDATA";

        /// <summary>Hilite draw data key.</summary>
        public const string HiliteDrawData = "HILITEDRAWDATA";

        /// <summary>Image offset key.</summary>
        public const string ImageOffset = "IMAGEOFFSET";

        /// <summary>Tooltip key.</summary>
        public const string Tooltip = "TOOLTIP";

        /// <summary>Legacy positional gadget data key.</summary>
        public const string Data = "DATA";
    }

    /// <summary>
    /// Window status flag names exactly as parsed by the engine
    /// (<c>WindowStatusNames</c> in GameWindowManagerScript.cpp). Order is canonical.
    /// </summary>
    public static class StatusFlags
    {
        /// <summary>Window is at the top of the window list.</summary>
        public const string Active = "ACTIVE";

        /// <summary>Click to toggle.</summary>
        public const string Toggle = "TOGGLE";

        /// <summary>Window can be dragged. Note the engine spelling with one G.</summary>
        public const string Dragable = "DRAGABLE";

        /// <summary>Window can receive input.</summary>
        public const string Enabled = "ENABLED";

        /// <summary>Window is hidden and takes no input.</summary>
        public const string Hidden = "HIDDEN";

        /// <summary>Window is always above others.</summary>
        public const string Above = "ABOVE";

        /// <summary>Window is always below others.</summary>
        public const string Below = "BELOW";

        /// <summary>Window is drawn with images.</summary>
        public const string Image = "IMAGE";

        /// <summary>Window is a tab stop.</summary>
        public const string TabStop = "TABSTOP";

        /// <summary>Window does not take input.</summary>
        public const string NoInput = "NOINPUT";

        /// <summary>Window does not take focus.</summary>
        public const string NoFocus = "NOFOCUS";

        /// <summary>Window has been destroyed.</summary>
        public const string Destroyed = "DESTROYED";

        /// <summary>Window is drawn with borders and corners.</summary>
        public const string Border = "BORDER";

        /// <summary>Window text is drawn with smoothing.</summary>
        public const string SmoothText = "SMOOTH_TEXT";

        /// <summary>Window text is drawn on only one line.</summary>
        public const string OneLine = "ONE_LINE";

        /// <summary>Window images are not unloaded when hidden.</summary>
        public const string NoFlush = "NO_FLUSH";

        /// <summary>Window does not draw but is not hidden.</summary>
        public const string SeeThru = "SEE_THRU";

        /// <summary>Window pays attention to right clicks.</summary>
        public const string RightClick = "RIGHT_CLICK";

        /// <summary>Text is centered on each word wrap.</summary>
        public const string WrapCentered = "WRAP_CENTERED";

        /// <summary>Push buttons behave check-like with dual state.</summary>
        public const string CheckLike = "CHECK_LIKE";

        /// <summary>Hotkey text flag.</summary>
        public const string HotkeyText = "HOTKEY_TEXT";

        /// <summary>Push buttons use the global overlay renderer for states.</summary>
        public const string UseOverlayStates = "USE_OVERLAY_STATES";

        /// <summary>Disabled but available button, not yet ready.</summary>
        public const string NotReady = "NOT_READY";

        /// <summary>Button used for cameo flashes.</summary>
        public const string Flashing = "FLASHING";

        /// <summary>Never render using the greyscale renderer when disabled.</summary>
        public const string AlwaysColor = "ALWAYS_COLOR";

        /// <summary>Push button triggers on mouse down.</summary>
        public const string OnMouseDown = "ON_MOUSE_DOWN";

        /// <summary>
        /// All status flags in engine canonical order.
        /// </summary>
        public static readonly string[] All =
        [
            Active, Toggle, Dragable, Enabled, Hidden, Above, Below, Image, TabStop,
            NoInput, NoFocus, Destroyed, Border, SmoothText, OneLine, NoFlush, SeeThru,
            RightClick, WrapCentered, CheckLike, HotkeyText, UseOverlayStates, NotReady,
            Flashing, AlwaysColor, OnMouseDown,
        ];

        /// <summary>
        /// Everyday visibility and input flags shown in the basic group.
        /// </summary>
        public static readonly string[] Basic =
        [
            Enabled, Hidden, Image, Border, SeeThru, Active, Toggle, TabStop,
        ];

        /// <summary>
        /// Pointer and focus interaction flags shown in the interaction group.
        /// </summary>
        public static readonly string[] Interaction =
        [
            NoInput, NoFocus, RightClick, OnMouseDown, CheckLike, Dragable,
        ];

        /// <summary>
        /// Text rendering and advanced flags shown in the miscellaneous group.
        /// </summary>
        public static readonly string[] Misc =
        [
            Above, Below, Destroyed, SmoothText, OneLine, NoFlush, WrapCentered,
            HotkeyText, UseOverlayStates, NotReady, Flashing, AlwaysColor,
        ];
    }

    /// <summary>
    /// Gadget style names exactly as parsed by the engine
    /// (<c>WindowStyleNames</c> in GameWindowManagerScript.cpp). Order is canonical.
    /// </summary>
    public static class StyleTypes
    {
        /// <summary>Push button style.</summary>
        public const string PushButton = "PUSHBUTTON";

        /// <summary>Radio button style.</summary>
        public const string RadioButton = "RADIOBUTTON";

        /// <summary>Check box style.</summary>
        public const string CheckBox = "CHECKBOX";

        /// <summary>Vertical slider style.</summary>
        public const string VertSlider = "VERTSLIDER";

        /// <summary>Horizontal slider style.</summary>
        public const string HorzSlider = "HORZSLIDER";

        /// <summary>Scrollable list box style.</summary>
        public const string ScrollListBox = "SCROLLLISTBOX";

        /// <summary>Text entry field style.</summary>
        public const string EntryField = "ENTRYFIELD";

        /// <summary>Static text style.</summary>
        public const string StaticText = "STATICTEXT";

        /// <summary>Progress bar style.</summary>
        public const string ProgressBar = "PROGRESSBAR";

        /// <summary>Generic user window style.</summary>
        public const string User = "USER";

        /// <summary>Mouse tracking style.</summary>
        public const string MouseTrack = "MOUSETRACK";

        /// <summary>Animated style.</summary>
        public const string Animated = "ANIMATED";

        /// <summary>Tab stop style.</summary>
        public const string TabStop = "TABSTOP";

        /// <summary>Tab control style.</summary>
        public const string TabControl = "TABCONTROL";

        /// <summary>Tab pane style.</summary>
        public const string TabPane = "TABPANE";

        /// <summary>Combo box style.</summary>
        public const string ComboBox = "COMBOBOX";

        /// <summary>
        /// All style names in engine canonical order.
        /// </summary>
        public static readonly string[] All =
        [
            PushButton, RadioButton, CheckBox, VertSlider, HorzSlider, ScrollListBox,
            EntryField, StaticText, ProgressBar, User, MouseTrack, Animated,
            TabStop, TabControl, TabPane, ComboBox,
        ];
    }

    /// <summary>
    /// Sub-component draw data keys for list boxes, sliders, and combo boxes.
    /// </summary>
    public static class SubDrawDataKeys
    {
        /// <summary>List box enabled up button draw data.</summary>
        public const string ListboxEnabledUpButton = "LISTBOXENABLEDUPBUTTONDRAWDATA";

        /// <summary>List box enabled down button draw data.</summary>
        public const string ListboxEnabledDownButton = "LISTBOXENABLEDDOWNBUTTONDRAWDATA";

        /// <summary>List box enabled slider draw data.</summary>
        public const string ListboxEnabledSlider = "LISTBOXENABLEDSLIDERDRAWDATA";

        /// <summary>List box disabled up button draw data.</summary>
        public const string ListboxDisabledUpButton = "LISTBOXDISABLEDUPBUTTONDRAWDATA";

        /// <summary>List box disabled down button draw data.</summary>
        public const string ListboxDisabledDownButton = "LISTBOXDISABLEDDOWNBUTTONDRAWDATA";

        /// <summary>List box disabled slider draw data.</summary>
        public const string ListboxDisabledSlider = "LISTBOXDISABLEDSLIDERDRAWDATA";

        /// <summary>List box hilite up button draw data.</summary>
        public const string ListboxHiliteUpButton = "LISTBOXHILITEUPBUTTONDRAWDATA";

        /// <summary>List box hilite down button draw data.</summary>
        public const string ListboxHiliteDownButton = "LISTBOXHILITEDOWNBUTTONDRAWDATA";

        /// <summary>List box hilite slider draw data.</summary>
        public const string ListboxHiliteSlider = "LISTBOXHILITESLIDERDRAWDATA";

        /// <summary>Slider thumb enabled draw data.</summary>
        public const string SliderThumbEnabled = "SLIDERTHUMBENABLEDDRAWDATA";

        /// <summary>Slider thumb disabled draw data.</summary>
        public const string SliderThumbDisabled = "SLIDERTHUMBDISABLEDDRAWDATA";

        /// <summary>Slider thumb hilite draw data.</summary>
        public const string SliderThumbHilite = "SLIDERTHUMBHILITEDRAWDATA";

        /// <summary>Combo box drop-down button enabled draw data.</summary>
        public const string ComboBoxDropDownButtonEnabled = "COMBOBOXDROPDOWNBUTTONENABLEDDRAWDATA";

        /// <summary>Combo box drop-down button disabled draw data.</summary>
        public const string ComboBoxDropDownButtonDisabled = "COMBOBOXDROPDOWNBUTTONDISABLEDDRAWDATA";

        /// <summary>Combo box drop-down button hilite draw data.</summary>
        public const string ComboBoxDropDownButtonHilite = "COMBOBOXDROPDOWNBUTTONHILITEDRAWDATA";

        /// <summary>Combo box edit box enabled draw data.</summary>
        public const string ComboBoxEditBoxEnabled = "COMBOBOXEDITBOXENABLEDDRAWDATA";

        /// <summary>Combo box edit box disabled draw data.</summary>
        public const string ComboBoxEditBoxDisabled = "COMBOBOXEDITBOXDISABLEDDRAWDATA";

        /// <summary>Combo box edit box hilite draw data.</summary>
        public const string ComboBoxEditBoxHilite = "COMBOBOXEDITBOXHILITEDRAWDATA";

        /// <summary>Combo box list box enabled draw data.</summary>
        public const string ComboBoxListBoxEnabled = "COMBOBOXLISTBOXENABLEDDRAWDATA";

        /// <summary>Combo box list box disabled draw data.</summary>
        public const string ComboBoxListBoxDisabled = "COMBOBOXLISTBOXDISABLEDDRAWDATA";

        /// <summary>Combo box list box hilite draw data.</summary>
        public const string ComboBoxListBoxHilite = "COMBOBOXLISTBOXHILITEDRAWDATA";

        /// <summary>
        /// All sub-component draw data keys.
        /// </summary>
        public static readonly string[] All =
        [
            ListboxEnabledUpButton, ListboxEnabledDownButton, ListboxEnabledSlider,
            ListboxDisabledUpButton, ListboxDisabledDownButton, ListboxDisabledSlider,
            ListboxHiliteUpButton, ListboxHiliteDownButton, ListboxHiliteSlider,
            SliderThumbEnabled, SliderThumbDisabled, SliderThumbHilite,
            ComboBoxDropDownButtonEnabled, ComboBoxDropDownButtonDisabled, ComboBoxDropDownButtonHilite,
            ComboBoxEditBoxEnabled, ComboBoxEditBoxDisabled, ComboBoxEditBoxHilite,
            ComboBoxListBoxEnabled, ComboBoxListBoxDisabled, ComboBoxListBoxHilite,
        ];
    }

    /// <summary>
    /// Layout block property keys.
    /// </summary>
    public static class LayoutKeys
    {
        /// <summary>Layout init callback key.</summary>
        public const string LayoutInit = "LAYOUTINIT";

        /// <summary>Layout update callback key.</summary>
        public const string LayoutUpdate = "LAYOUTUPDATE";

        /// <summary>Layout shutdown callback key.</summary>
        public const string LayoutShutdown = "LAYOUTSHUTDOWN";
    }

    /// <summary>
    /// Known window control type names. Unknown values are preserved verbatim.
    /// </summary>
    public static class ControlTypes
    {
        /// <summary>Generic container control.</summary>
        public const string User = "USER";

        /// <summary>Push button control.</summary>
        public const string PushButton = "PUSHBUTTON";

        /// <summary>Static text label control.</summary>
        public const string StaticText = "STATICTEXT";

        /// <summary>Text entry field control.</summary>
        public const string EntryField = "ENTRYFIELD";

        /// <summary>Check box control.</summary>
        public const string CheckBox = "CHECKBOX";

        /// <summary>Radio button control.</summary>
        public const string RadioButton = "RADIOBUTTON";

        /// <summary>Progress bar control.</summary>
        public const string ProgressBar = "PROGRESSBAR";

        /// <summary>Horizontal slider control.</summary>
        public const string HorzSlider = "HORZSLIDER";

        /// <summary>Vertical slider control.</summary>
        public const string VertSlider = "VERTSLIDER";

        /// <summary>Scrollable list box control.</summary>
        public const string ScrollListBox = "SCROLLLISTBOX";

        /// <summary>Combo box control.</summary>
        public const string ComboBox = "COMBOBOX";

        /// <summary>Command button control.</summary>
        public const string CommandButton = "COMMANDBUTTON";

        /// <summary>Tab control.</summary>
        public const string TabControl = "TABCONTROL";

        /// <summary>Tab pane control.</summary>
        public const string TabPane = "TABPANE";
    }

    /// <summary>
    /// Screen rectangle component keys.
    /// </summary>
    public static class ScreenRectKeys
    {
        /// <summary>Upper-left corner key.</summary>
        public const string UpperLeft = "UPPERLEFT";

        /// <summary>Bottom-right corner key.</summary>
        public const string BottomRight = "BOTTOMRIGHT";

        /// <summary>Creation resolution key.</summary>
        public const string CreationResolution = "CREATIONRESOLUTION";
    }

    /// <summary>
    /// File level constants.
    /// </summary>
    public static class File
    {
        /// <summary>File extension for window layout files.</summary>
        public const string Extension = ".wnd";

        /// <summary>Known file format version emitted by the game and editors.</summary>
        public const string KnownVersion = "2";
    }

    /// <summary>
    /// Statement syntax constants.
    /// </summary>
    public static class Syntax
    {
        /// <summary>Terminates a key/value statement.</summary>
        public const char StatementTerminator = ';';

        /// <summary>Separates a key from its value.</summary>
        public const char KeyValueSeparator = '=';

        /// <summary>Separates screen rectangle components.</summary>
        public const char ComponentSeparator = ',';

        /// <summary>Separates components in canonical multi-line values.</summary>
        public const string ComponentListSeparator = ", ";

        /// <summary>Separates a screen rectangle key from its coordinates.</summary>
        public const char CoordinateSeparator = ':';

        /// <summary>Separates status and style flag tokens.</summary>
        public const char FlagSeparator = '+';

        /// <summary>Separates the file name from the window name in decorated names.</summary>
        public const char DecoratedNameSeparator = ':';

        /// <summary>Indentation unit for canonical output.</summary>
        public const string Indent = "  ";

        /// <summary>Double quote delimiting string literals preserved verbatim.</summary>
        public const char Quote = '"';

        /// <summary>True boolean literal accepted by the engine.</summary>
        public const string BoolTrue = "1";

        /// <summary>False boolean literal accepted by the engine.</summary>
        public const string BoolFalse = "0";

        /// <summary>Flag literal meaning no flags are set.</summary>
        public const string NullFlags = "NULL";

        /// <summary>Flag literal meaning no flags are set (GUIEdit dialect).</summary>
        public const string NoneFlags = "NONE";
    }

    /// <summary>
    /// Draw data value constants. The engine reads exactly nine entries per draw data block.
    /// </summary>
    public static class DrawData
    {
        /// <summary>Number of entries per draw data block (MAX_DRAW_DATA).</summary>
        public const int EntryCount = 9;

        /// <summary>Number of tokens per draw data entry (IMAGE + name + COLOR + 4 RGBA + BORDERCOLOR + 4 RGBA = 12).</summary>
        public const int TokensPerEntry = 12;

        /// <summary>Image name marking an empty draw data entry.</summary>
        public const string NoImage = "NoImage";

        /// <summary>Image label.</summary>
        public const string ImageLabel = "IMAGE";

        /// <summary>Color label.</summary>
        public const string ColorLabel = "COLOR";

        /// <summary>Border color label.</summary>
        public const string BorderColorLabel = "BORDERCOLOR";
    }

    /// <summary>
    /// Mapped image lookup constants mirroring the engine asset chain:
    /// DrawData IMAGE names resolve through Data\INI\MappedImages definitions
    /// to texture pages under Art\Textures.
    /// </summary>
    public static class MappedImages
    {
        /// <summary>Mapped image definition block tag.</summary>
        public const string BlockTag = "MappedImage";

        /// <summary>Block terminator tag.</summary>
        public const string EndTag = "End";

        /// <summary>Texture file field.</summary>
        public const string TextureField = "Texture";

        /// <summary>Source rectangle field.</summary>
        public const string CoordsField = "Coords";

        /// <summary>Status flags field.</summary>
        public const string StatusField = "Status";

        /// <summary>Status flag marking 90 degree clockwise packed content.</summary>
        public const string RotatedStatus = "ROTATED_90_CLOCKWISE";

        /// <summary>Left coordinate attribute.</summary>
        public const string LeftAttribute = "Left";

        /// <summary>Top coordinate attribute.</summary>
        public const string TopAttribute = "Top";

        /// <summary>Right coordinate attribute.</summary>
        public const string RightAttribute = "Right";

        /// <summary>Bottom coordinate attribute.</summary>
        public const string BottomAttribute = "Bottom";

        /// <summary>INI comment prefix.</summary>
        public const char CommentPrefix = ';';

        /// <summary>Key/value separator.</summary>
        public const char KeySeparator = '=';

        /// <summary>Coordinate attribute separator.</summary>
        public const char AttributeSeparator = ':';

        /// <summary>Virtual directory holding mapped image definitions.</summary>
        public const string DefinitionsDirectory = "Data\\INI\\MappedImages";

        /// <summary>Texture detail folder name prefix.</summary>
        public const string TextureSizePrefix = "TextureSize_";

        /// <summary>Virtual directory holding GUI texture pages.</summary>
        public const string TexturesDirectory = "Art\\Textures";

        /// <summary>Default language folder for localized texture lookup.</summary>
        public const string DefaultLanguage = "english";

        /// <summary>DirectDraw Surface texture extension.</summary>
        public const string TextureExtensionDds = ".dds";

        /// <summary>Truevision TGA texture extension.</summary>
        public const string TextureExtensionTga = ".tga";

        /// <summary>JPEG texture extension.</summary>
        public const string TextureExtensionJpg = ".jpg";

        /// <summary>Texture page extensions probed in order.</summary>
        public static readonly string[] TextureExtensions = [TextureExtensionDds, TextureExtensionTga, TextureExtensionJpg];

        /// <summary>Language folders probed for localized texture pages, in order.</summary>
        public static readonly string[] TextureLanguages = ["english", "german", "french", "spanish", "italian", "russian", "polish", "brazilian", "japanese", "korean", "chinese"];
    }

    /// <summary>
    /// Text color component labels.
    /// </summary>
    public static class TextColorKeys
    {
        /// <summary>Enabled text color label.</summary>
        public const string Enabled = "ENABLED";

        /// <summary>Enabled text border color label.</summary>
        public const string EnabledBorder = "ENABLEDBORDER";

        /// <summary>Disabled text color label.</summary>
        public const string Disabled = "DISABLED";

        /// <summary>Disabled text border color label.</summary>
        public const string DisabledBorder = "DISABLEDBORDER";

        /// <summary>Hilite text color label.</summary>
        public const string Hilite = "HILITE";

        /// <summary>Hilite text border color label.</summary>
        public const string HiliteBorder = "HILITEBORDER";
    }

    /// <summary>
    /// Font component labels.
    /// </summary>
    public static class FontKeys
    {
        /// <summary>Font name label.</summary>
        public const string Name = "NAME";

        /// <summary>Font size label.</summary>
        public const string Size = "SIZE";

        /// <summary>Font bold label.</summary>
        public const string Bold = "BOLD";
    }

    /// <summary>
    /// Gadget data component labels.
    /// </summary>
    public static class GadgetDataKeys
    {
        /// <summary>Static text centered label.</summary>
        public const string Centered = "CENTERED";

        /// <summary>Text entry maximum length label.</summary>
        public const string MaxLen = "MAXLEN";

        /// <summary>Text entry secret text label.</summary>
        public const string SecretText = "SECRETTEXT";

        /// <summary>Text entry numerical only label.</summary>
        public const string NumericalOnly = "NUMERICALONLY";

        /// <summary>Text entry alphanumeric only label.</summary>
        public const string AlphaNumericalOnly = "ALPHANUMERICALONLY";

        /// <summary>Text entry ASCII only label.</summary>
        public const string AsciiOnly = "ASCIIONLY";

        /// <summary>Slider minimum value label.</summary>
        public const string MinValue = "MINVALUE";

        /// <summary>Slider maximum value label.</summary>
        public const string MaxValue = "MAXVALUE";

        /// <summary>List box length label.</summary>
        public const string Length = "LENGTH";

        /// <summary>List box auto scroll label.</summary>
        public const string AutoScroll = "AUTOSCROLL";

        /// <summary>List box optional scroll-if-at-end label.</summary>
        public const string ScrollIfAtEnd = "ScrollIfAtEnd";

        /// <summary>List box auto purge label.</summary>
        public const string AutoPurge = "AUTOPURGE";

        /// <summary>List box scroll bar label.</summary>
        public const string ScrollBar = "SCROLLBAR";

        /// <summary>List box multi select label.</summary>
        public const string MultiSelect = "MULTISELECT";

        /// <summary>List box columns label.</summary>
        public const string Columns = "COLUMNS";

        /// <summary>List box force select label.</summary>
        public const string ForceSelect = "FORCESELECT";

        /// <summary>Combo box editable label.</summary>
        public const string IsEditable = "ISEDITABLE";

        /// <summary>Combo box maximum characters label.</summary>
        public const string MaxChars = "MAXCHARS";

        /// <summary>Combo box maximum displayed entries label.</summary>
        public const string MaxDisplay = "MAXDISPLAY";

        /// <summary>Combo box letters and numbers only label.</summary>
        public const string LettersAndNumbersOnly = "LETTERSANDNUMBERS";

        /// <summary>List box column width percentage label emitted by GUIEdit.</summary>
        public const string ColumnsWidthPercent = "COLUMNSWIDTH%";

        /// <summary>Radio button group label.</summary>
        public const string Group = "GROUP";

        /// <summary>Tab control orientation label.</summary>
        public const string TabOrientation = "TABORIENTATION";

        /// <summary>Tab control edge label.</summary>
        public const string TabEdge = "TABEDGE";

        /// <summary>Tab control width label.</summary>
        public const string TabWidth = "TABWIDTH";

        /// <summary>Tab control height label.</summary>
        public const string TabHeight = "TABHEIGHT";

        /// <summary>Tab control count label.</summary>
        public const string TabCount = "TABCOUNT";

        /// <summary>Tab control pane border label.</summary>
        public const string PaneBorder = "PANEBORDER";

        /// <summary>Tab control pane disabled label.</summary>
        public const string PaneDisabled = "PANEDISABLED";
    }

    /// <summary>
    /// Editor canvas and default content constants.
    /// </summary>
    public static class Editor
    {
        /// <summary>Minimum canvas zoom factor.</summary>
        public const double MinZoom = 0.25;

        /// <summary>Maximum canvas zoom factor.</summary>
        public const double MaxZoom = 2.0;

        /// <summary>Default canvas zoom factor.</summary>
        public const double DefaultZoom = 1.0;

        /// <summary>Multiplicative zoom step for wheel, button, and keyboard zoom.</summary>
        public const double ZoomStepFactor = 1.2;

        /// <summary>Minimum canvas width in game units.</summary>
        public const double MinCanvasWidth = 800.0;

        /// <summary>Minimum canvas height in game units.</summary>
        public const double MinCanvasHeight = 600.0;

        /// <summary>Default width for newly created windows.</summary>
        public const int DefaultNewWindowWidth = 100;

        /// <summary>Default height for newly created windows.</summary>
        public const int DefaultNewWindowHeight = 100;

        /// <summary>Default control type for newly created windows.</summary>
        public const string DefaultNewWindowType = ControlTypes.User;

        /// <summary>Default name for newly created windows.</summary>
        public const string DefaultNewWindowName = "NewWindow";

        /// <summary>Default font size shown when a window declares no font.</summary>
        public const int DefaultFontSize = 12;

        /// <summary>Empty margin around canvas content in game units, keeping every document pannable.</summary>
        public const double CanvasPadding = 400.0;

        /// <summary>Minimum width or height when resizing a window on the canvas.</summary>
        public const int MinResizeDimension = 8;
    }

    /// <summary>
    /// Canvas preview constants mirroring engine gadget draw-data layouts
    /// (GadgetPushButton.h and GadgetTextEntry.h in GeneralsGameCode).
    /// </summary>
    public static class Preview
    {
        /// <summary>Generic single-image draw-data index.</summary>
        public const int DefaultImageIndex = 0;

        /// <summary>Push button whole-image or left-cap index.</summary>
        public const int ButtonImageIndex = 0;

        /// <summary>Push button tiled middle-bar index.</summary>
        public const int ButtonMiddleImageIndex = 5;

        /// <summary>Push button right-cap index.</summary>
        public const int ButtonRightImageIndex = 6;

        /// <summary>Text entry left-cap index.</summary>
        public const int TextEntryLeftImageIndex = 0;

        /// <summary>Text entry right-cap index.</summary>
        public const int TextEntryRightImageIndex = 1;

        /// <summary>Text entry tiled middle-bar index.</summary>
        public const int TextEntryCenterImageIndex = 2;

        /// <summary>Check box and radio button unchecked-glyph index.</summary>
        public const int BoxGlyphImageIndex = 1;

        /// <summary>Slider trough left-cap or top-cap index.</summary>
        public const int SliderLeftImageIndex = 0;

        /// <summary>Slider trough right-cap or bottom-cap index.</summary>
        public const int SliderRightImageIndex = 1;

        /// <summary>Slider trough tiled middle-bar index.</summary>
        public const int SliderCenterImageIndex = 2;

        /// <summary>Glyph left margin in device-independent pixels.</summary>
        public const double GlyphMargin = 4.0;

        /// <summary>Gap between a glyph and its text in device-independent pixels.</summary>
        public const double GlyphTextGap = 4.0;

        /// <summary>Texture detail size preferred for previews, matching typical creation resolutions.</summary>
        public const int PreferredTextureSize = 800;

        /// <summary>Hand-created mapped images directory name, winning over size-specific definitions.</summary>
        public const string HandCreatedDirectory = "HandCreated";

        /// <summary>Maximum composed preview width or height in pixels.</summary>
        public const int MaxComposedDimension = 2048;

        /// <summary>Maximum missing image names listed in the asset status tooltip.</summary>
        public const int MaxMissingTooltipNames = 12;

        /// <summary>Minimum content text size in device-independent pixels.</summary>
        public const double MinContentFontSize = 9.0;

        /// <summary>Opacity for windows carrying the hidden flag.</summary>
        public const double HiddenOpacity = 0.35;
    }

    /// <summary>
    /// Game string table constants. Both Generals and Zero Hour load
    /// Data/&lt;Language&gt;/Generals.csf through the virtual file system.
    /// </summary>
    public static class StringTables
    {
        /// <summary>Game data directory containing localized string tables.</summary>
        public const string DataDirectory = "Data";

        /// <summary>Game string table file name shared by Generals and Zero Hour.</summary>
        public const string FileName = "Generals.csf";

        /// <summary>Languages probed for string tables, in order.</summary>
        public static readonly string[] Languages = MappedImages.TextureLanguages;
    }

    /// <summary>
    /// Game ControlBarScheme INI constants.
    /// </summary>
    public static class ControlBarScheme
    {
        /// <summary>Data directory containing game INI files.</summary>
        public const string DataDirectory = "Data";

        /// <summary>INI directory name.</summary>
        public const string IniDirectory = "INI";

        /// <summary>Control bar scheme file name.</summary>
        public const string FileName = "ControlBarScheme.ini";

        /// <summary>Default America scheme name in retail Zero Hour.</summary>
        public const string AmericaSchemeName = "ControlBarSchemeAmerica";

        /// <summary>Fallback faction match token for America.</summary>
        public const string AmericaFaction = "America";

        /// <summary>Block header keyword for control bar schemes.</summary>
        public const string SchemeKeyword = "ControlBarScheme";

        /// <summary>Block keyword for image part definitions.</summary>
        public const string ImagePartKeyword = "ImagePart";

        /// <summary>Block terminator keyword.</summary>
        public const string EndKeyword = "End";

        /// <summary>Image name property keyword.</summary>
        public const string ImageNameKeyword = "ImageName";

        /// <summary>Override key name for background marker image.</summary>
        public const string BackgroundMarkerKey = "BackgroundMarker";

        /// <summary>Override key name for right HUD image.</summary>
        public const string RightHUDKey = "RightHUD";

        /// <summary>Override key name for options button image.</summary>
        public const string ButtonOptionsKey = "ButtonOptions";

        /// <summary>Override key name for idle worker button image.</summary>
        public const string ButtonIdleWorkerKey = "ButtonIdleWorker";

        /// <summary>Override key name for chat button image.</summary>
        public const string ButtonChatKey = "ButtonChat";

        /// <summary>Override key name for place beacon button image.</summary>
        public const string ButtonPlaceBeaconKey = "ButtonPlaceBeacon";

        /// <summary>Override key name for general button image.</summary>
        public const string ButtonGeneralKey = "ButtonGeneral";

        /// <summary>Override key name for under-attack button image.</summary>
        public const string ButtonUAttackKey = "ButtonUAttack";

        /// <summary>Override key name for experience bar foreground image.</summary>
        public const string ExpBarForegroundKey = "ExpBarForeground";

        /// <summary>Override key name for queue button image.</summary>
        public const string QueueButtonImageKey = "QueueButtonImage";

        /// <summary>Default America HUD base image name for Generals.</summary>
        public const string DefaultAmericaBaseGenerals = "InGameUIAmericaBase";

        /// <summary>Default America HUD base image name for Zero Hour.</summary>
        public const string DefaultAmericaBaseZeroHour = "InGameUIAmericaBaseZH";

        /// <summary>Standard virtual path to ControlBarScheme.ini under Data\\INI.</summary>
        public static readonly string DataIniPath = $"{DataDirectory}\\{IniDirectory}\\{FileName}";

        /// <summary>Alternative virtual path to ControlBarScheme.ini under INI.</summary>
        public static readonly string IniPath = $"{IniDirectory}\\{FileName}";
    }
}
