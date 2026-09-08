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
        var assembly = typeof(AppConstants).Assembly;
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
    /// The folder name for documentation files.
    /// </summary>
    public const string DocsFolderName = "docs";

    /// <summary>
    /// The folder name for the CSV game installation files registry.
    /// </summary>
    public const string GameInstallationFilesRegistryFolderName = "GameInstallationFilesRegistry";

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
    /// Title of the confirmation prompt shown before all application data is deleted.
    /// </summary>
    public const string DeleteAllDataConfirmationTitle = "Delete All Application Data";

    /// <summary>
    /// Body of the confirmation prompt shown before all application data is deleted.
    /// </summary>
    public const string DeleteAllDataConfirmationMessage =
        "This permanently deletes every profile, workspace, manifest, CAS object and tracked user data " +
        "installation. The pristine backups GenHub keeps of your original game data will be discarded " +
        "as part of this, so anything GenHub replaced cannot be recovered afterwards.\n\n" +
        "This action is irreversible. Continue?";

    /// <summary>
    /// Confirm button text for the delete-all-application-data prompt.
    /// </summary>
    public const string DeleteAllDataConfirmText = "Delete Everything";

    /// <summary>
    /// Title of the confirmation prompt shown before GenHub is uninstalled.
    /// </summary>
    public const string UninstallGenHubConfirmationTitle = "Uninstall GenHub";

    /// <summary>
    /// Body of the confirmation prompt shown before GenHub is uninstalled.
    /// </summary>
    public const string UninstallGenHubConfirmationMessage =
        "Are you sure you want to uninstall GenHub? Application data and game installations will be kept.";

    /// <summary>
    /// Confirm button text for the uninstall GenHub prompt.
    /// </summary>
    public const string UninstallGenHubConfirmText = "Uninstall";

    /// <summary>
    /// Title of the confirmation prompt shown before CAS storage is deleted.
    /// </summary>
    public const string DeleteCasStorageConfirmationTitle = "Delete CAS Storage";

    /// <summary>
    /// Body of the confirmation prompt shown before CAS storage is deleted.
    /// </summary>
    public const string DeleteCasStorageConfirmationMessage =
        "Are you sure you want to clear CAS storage? Unreferenced cached game files will be deleted. This action is irreversible.";

    /// <summary>
    /// Confirm button text for the delete CAS storage prompt.
    /// </summary>
    public const string DeleteCasStorageConfirmText = "Delete CAS";

    /// <summary>
    /// Title of the confirmation prompt shown before all workspaces are deleted.
    /// </summary>
    public const string DeleteWorkspacesConfirmationTitle = "Delete Workspaces";

    /// <summary>
    /// Body of the confirmation prompt shown before all workspaces are deleted.
    /// </summary>
    public const string DeleteWorkspacesConfirmationMessage =
        "Are you sure you want to delete all workspaces? All temporary profile workspaces will be deleted. This action is irreversible.";

    /// <summary>
    /// Confirm button text for the delete workspaces prompt.
    /// </summary>
    public const string DeleteWorkspacesConfirmText = "Delete Workspaces";

    /// <summary>
    /// Title of the confirmation prompt shown before all manifests are deleted.
    /// </summary>
    public const string DeleteManifestsConfirmationTitle = "Delete Manifests";

    /// <summary>
    /// Body of the confirmation prompt shown before all manifests are deleted.
    /// </summary>
    public const string DeleteManifestsConfirmationMessage =
        "Are you sure you want to delete all manifests? All content metadata and manifests will be removed. This action is irreversible.";

    /// <summary>
    /// Confirm button text for the delete manifests prompt.
    /// </summary>
    public const string DeleteManifestsConfirmText = "Delete Manifests";

    /// <summary>
    /// Title of the confirmation prompt shown before all profiles are deleted.
    /// </summary>
    public const string DeleteProfilesConfirmationTitle = "Delete Profiles";

    /// <summary>
    /// Body of the confirmation prompt shown before all profiles are deleted.
    /// </summary>
    public const string DeleteProfilesConfirmationMessage =
        "Are you sure you want to delete all profiles? All user profiles will be permanently removed. This action is irreversible.";

    /// <summary>
    /// Confirm button text for the delete profiles prompt.
    /// </summary>
    public const string DeleteProfilesConfirmText = "Delete Profiles";

    /// <summary>
    /// Gets assembly metadata by key.
    /// </summary>
    private static string? GetAssemblyMetadata(string key)
    {
        var assembly = typeof(AppConstants).Assembly;
        return assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == key)
            ?.Value;
    }
}
