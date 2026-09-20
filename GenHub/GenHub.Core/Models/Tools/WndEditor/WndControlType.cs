namespace GenHub.Core.Models.Tools.WndEditor;

/// <summary>
/// Control types recognized in window definition (.wnd) files.
/// </summary>
public enum WndControlType
{
    /// <summary>
    /// Unrecognized control type. The original type name is preserved on the window.
    /// </summary>
    Unknown,

    /// <summary>
    /// Generic container control.
    /// </summary>
    User,

    /// <summary>
    /// Push button control.
    /// </summary>
    PushButton,

    /// <summary>
    /// Static text label control.
    /// </summary>
    StaticText,

    /// <summary>
    /// Text entry field control.
    /// </summary>
    EntryField,

    /// <summary>
    /// Check box control.
    /// </summary>
    CheckBox,

    /// <summary>
    /// Radio button control.
    /// </summary>
    RadioButton,

    /// <summary>
    /// Progress bar control.
    /// </summary>
    ProgressBar,

    /// <summary>
    /// Horizontal slider control.
    /// </summary>
    HorzSlider,

    /// <summary>
    /// Vertical slider control.
    /// </summary>
    VertSlider,

    /// <summary>
    /// Scrollable list box control.
    /// </summary>
    ScrollListBox,

    /// <summary>
    /// Combo box control.
    /// </summary>
    ComboBox,
}
