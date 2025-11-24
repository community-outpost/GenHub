using GenHub.Core.Models.Enums;
using System.Reflection;

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

    private static readonly Lazy<string> _appVersion = new Lazy<string>(() =>
    {
        var assembly = Assembly.GetExecutingAssembly();
        return assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
        ?? assembly.GetName().Version?.ToString()
        ?? "0.0.0-dev";
    });

    /// <summary>
    /// Gets the full semantic version of the application.
    /// This value is automatically extracted from assembly metadata at runtime.
    /// To change version: Update &lt;Version&gt; in GenHub/Directory.Build.props.
    /// Format: Major.Minor.Patch[-prerelease] (e.g., "1.0.0-alpha.1").
    /// </summary>
    public static string AppVersion => _appVersion.Value;

    /// <summary>
    /// Gets the display version for UI (auto-formatted from AppVersion).
    /// </summary>
    public static string DisplayVersion => $"v{AppVersion}";

    /// <summary>
    /// The GitHub repository URL for the application.
    /// </summary>
    public const string GitHubRepositoryUrl = "https://github.com/community-outpost/genhub";

    /// <summary>
    /// The GitHub repository owner.
    /// </summary>
    public const string GitHubRepositoryOwner = "community-outpost";

    /// <summary>
    /// The GitHub repository name.
    /// </summary>
    public const string GitHubRepositoryName = "GenHub";

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