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

        /// <summary>Separates a screen rectangle key from its coordinates.</summary>
        public const char CoordinateSeparator = ':';

        /// <summary>Indentation unit for canonical output.</summary>
        public const string Indent = "  ";

        /// <summary>Double quote delimiting string literals preserved verbatim.</summary>
        public const char Quote = '"';
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
    }
}
