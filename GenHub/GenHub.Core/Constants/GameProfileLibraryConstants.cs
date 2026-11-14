namespace GenHub.Core.Constants;

/// <summary>
/// Constants for the game profile library view.
/// </summary>
public static class GameProfileLibraryConstants
{
    /// <summary>
    /// Identifier of the library overview tab.
    /// </summary>
    public const string OverviewTab = "Overview";

    /// <summary>
    /// Identifier of the library content tab.
    /// </summary>
    public const string ContentTab = "Content";

    /// <summary>
    /// Identifier of the library settings tab.
    /// </summary>
    public const string SettingsTab = "Settings";

    /// <summary>
    /// Index of the content tab in the profile settings editor.
    /// </summary>
    public const int ContentEditorTabIndex = 0;

    /// <summary>
    /// Index of the profile settings tab in the profile settings editor.
    /// </summary>
    public const int SettingsEditorTabIndex = 1;

    /// <summary>
    /// Separates the game name and version in a compact metadata caption.
    /// </summary>
    public const string MetadataSeparator = " · ";

    /// <summary>
    /// View width in pixels below which the profile pane starts closed.
    /// </summary>
    public const double CompactWidthThreshold = 700.0;
}
