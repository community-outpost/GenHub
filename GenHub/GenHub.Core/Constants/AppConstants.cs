using System.Reflection;
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

    private static readonly Lazy<string> _appVersion = new(() =>
    {
        var assembly = Assembly.GetExecutingAssembly();
        return assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
        ?? assembly.GetName().Version?.ToString()
        ?? "0.0.0-dev";
    });

    private static readonly Lazy<string> _gitShortHash = new(() =>
        GetAssemblyMetadata("GitShortHash") ?? string.Empty);

    private static readonly Lazy<string> _pullRequestNumber = new(() =>
        GetAssemblyMetadata("PullRequestNumber") ?? string.Empty);

    private static readonly Lazy<string> _buildChannel = new(() =>
        GetAssemblyMetadata("BuildChannel") ?? "Dev");

    /// <summary>
    /// Gets the full semantic version of the application.
    /// This value is automatically extracted from assembly metadata at runtime.
    /// To change version: Update &lt;Version&gt; in GenHub/Directory.Build.props.
    /// Format: 0.0.X[-prY] (e.g., "0.0.150" or "0.0.150-pr42").
    /// </summary>
    public static string AppVersion => _appVersion.Value;

    /// <summary>
    /// Gets the display version for UI (auto-formatted from AppVersion).
    /// </summary>
    public static string DisplayVersion => $"v{AppVersion}";

    /// <summary>
    /// Gets the short git commit hash (7 chars) for this build, or empty for local builds.
    /// </summary>
    public static string GitShortHash => _gitShortHash.Value;

    /// <summary>
    /// Gets the PR number if this is a PR build, or empty for other builds.
    /// </summary>
    public static string PullRequestNumber => _pullRequestNumber.Value;

    /// <summary>
    /// Gets the build channel (Dev, PR, CI, Release).
    /// </summary>
    public static string BuildChannel => _buildChannel.Value;

    /// <summary>
    /// Gets a value indicating whether this is a CI/CD build (has git hash embedded).
    /// </summary>
    public static bool IsCiBuild => !string.IsNullOrEmpty(GitShortHash);

    /// <summary>
    /// Gets the full display version including hash for dev builds.
    /// Format examples:
    /// Local: "v0.0.1".
    /// CI: "v0.0.150 (abc1234)".
    /// PR: "v0.0.150-pr3 (abc1234)".
    /// </summary>
    public static string FullDisplayVersion
    {
        get
        {
            var version = DisplayVersion;

            if (string.IsNullOrEmpty(GitShortHash))
            {
                return version;
            }

            return $"{version} ({GitShortHash})";
        }
    }

    /// <summary>
    /// The GitHub repository URL for the application.
    /// </summary>
    public const string GitHubRepositoryUrl = "https://github.com/" + GitHubRepositoryOwner + "/" + GitHubRepositoryName;

    /// <summary>
    /// The GitHub repository owner.
    /// </summary>
    public const string GitHubRepositoryOwner = "community-outpost";

    /// <summary>
    /// The GitHub repository name.
    /// </summary>
    public const string GitHubRepositoryName = "GenHub";

    /// <summary>
    /// The default branch name for the GitHub repository.
    /// </summary>
    public const string GitHubDefaultBranch = "main";

    /// <summary>
    /// The folder path where the CSV registry files are stored.
    /// </summary>
    public const string RegistryFolderPath = "docs\\GameInstallationFilesRegistry";

    /// <summary>
    /// Length of the git short hash used in versioning (7 characters).
    /// </summary>
    public const int GitShortHashLength = 7;

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

    /// <summary>
    /// Gets assembly metadata by key.
    /// </summary>
    private static string? GetAssemblyMetadata(string key)
    {
        var assembly = Assembly.GetExecutingAssembly();
        return assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == key)
            ?.Value;
    }
}
