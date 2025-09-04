using GenHub.Core.Models.Enums;

namespace GenHub.Core.Constants;

/// <summary>
/// Application-wide constants for GenHub.
/// </summary>
public static class AppConstants
{
    /// <summary>
    /// The current version of GenHub used in user agent strings and API calls.
    /// </summary>
    public const string Version = "1.0";

    /// <summary>
    /// The application name used in user agent strings.
    /// </summary>
    public const string ApplicationName = "GenHub";

    /// <summary>
    /// The default UI theme for the application.
    /// </summary>
    public const Theme DefaultTheme = Theme.Dark;

    /// <summary>
    /// The default theme name as a string (for backward compatibility).
    /// </summary>
    public const string DefaultThemeName = "Dark";

    /// <summary>
    /// The default user agent string for HTTP requests.
    /// </summary>
    public static readonly string DefaultUserAgent = $"{ApplicationName}/{Version}";
}
