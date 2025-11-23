using GenHub.Core.Models.Enums;

namespace GenHub.Core.Constants;

/// <summary>
/// Application-wide constants for GenHub.
/// </summary>
public static class AppConstants
{
    /// <summary>
    /// The name of the application.
    /// </summary>
    public const string AppName = "GenHub";

    /// <summary>
    /// The version of the application.
    /// </summary>
    public const string AppVersion = "1.0";

    /// <summary>
    /// The default UI theme for the application.
    /// </summary>
    public const Theme DefaultTheme = Theme.Dark;

    /// <summary>
    /// The default theme name as a string.
    /// </summary>
    public const string DefaultThemeName = "Dark";

    /// <summary>
    /// The default GitHub token file name.
    /// </summary>
    public const string TokenFileName = ".ghtoken";
}