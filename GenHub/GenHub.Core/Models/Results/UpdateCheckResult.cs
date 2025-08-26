using System.Collections.Generic;
using GenHub.Core.Models.GitHub;

namespace GenHub.Core.Models.Results;

/// <summary>
/// Represents the result of an update check operation.
/// </summary>
public class UpdateCheckResult(bool isUpdateAvailable = false, string currentVersion = "", string latestVersion = "", string? updateUrl = null, string? releaseNotes = null, string? releaseTitle = null, List<string>? errorMessages = null, IEnumerable<GitHubReleaseAsset>? assets = null)
{
    /// <summary>
    /// Gets or sets a value indicating whether an update is available.
    /// </summary>
    public bool IsUpdateAvailable { get; set; } = isUpdateAvailable;

    /// <summary>
    /// Gets or sets the current application version.
    /// </summary>
    public string CurrentVersion { get; set; } = currentVersion ?? string.Empty;

    /// <summary>
    /// Gets or sets the latest available version.
    /// </summary>
    public string LatestVersion { get; set; } = latestVersion ?? string.Empty;

    /// <summary>
    /// Gets or sets the URL to download or view the update.
    /// </summary>
    public string? UpdateUrl { get; set; } = updateUrl;

    /// <summary>
    /// Gets or sets the release notes for the update.
    /// </summary>
    public string? ReleaseNotes { get; set; } = releaseNotes;

    /// <summary>
    /// Gets or sets the title of the release for the update.
    /// </summary>
    public string? ReleaseTitle { get; set; } = releaseTitle;

    /// <summary>
    /// Gets or sets the list of error messages encountered during the check.
    /// </summary>
    public List<string> ErrorMessages { get; set; } = errorMessages ?? new List<string>();

    /// <summary>
    /// Gets a value indicating whether any errors occurred during the update check.
    /// </summary>
    public bool HasErrors => ErrorMessages.Count > 0;

    /// <summary>
    /// Gets or sets the list of release assets for the update.
    /// </summary>
    public IEnumerable<GitHubReleaseAsset> Assets { get; set; } = assets ?? new List<GitHubReleaseAsset>();

    /// <summary>
    /// Creates an UpdateCheckResult indicating no update is available.
    /// </summary>
    /// <returns>An UpdateCheckResult with IsUpdateAvailable set to false.</returns>
    public static UpdateCheckResult NoUpdateAvailable()
    {
        return new UpdateCheckResult(false, string.Empty, string.Empty, null, "Your application is up to date.", "No updates available");
    }

    /// <summary>
    /// Creates an UpdateCheckResult indicating an update is available.
    /// </summary>
    /// <param name="release">The available release.</param>
    /// <returns>An UpdateCheckResult with update information.</returns>
    public static UpdateCheckResult UpdateAvailable(GitHubRelease release)
    {
        return new UpdateCheckResult(true, string.Empty, release.TagName ?? string.Empty, release.HtmlUrl ?? string.Empty, release.Body ?? string.Empty, release.Name ?? string.Empty, null, release.Assets ?? new List<GitHubReleaseAsset>());
    }

    /// <summary>
    /// Creates an UpdateCheckResult indicating no update is available.
    /// </summary>
    /// <param name="currentVersion">The current version.</param>
    /// <param name="latestVersion">The latest version.</param>
    /// <param name="updateUrl">The update URL.</param>
    /// <returns>An UpdateCheckResult with no update available.</returns>
    public static UpdateCheckResult NoUpdateAvailable(string currentVersion = "", string latestVersion = "", string updateUrl = "")
    {
        return new UpdateCheckResult(false, currentVersion, latestVersion, updateUrl, "Your application is up to date.", "No updates available");
    }

    /// <summary>
    /// Creates an UpdateCheckResult indicating an error occurred.
    /// </summary>
    /// <param name="errorMessage">The error message.</param>
    /// <returns>An UpdateCheckResult with error information.</returns>
    public static UpdateCheckResult Error(string errorMessage)
    {
        return new UpdateCheckResult(false, string.Empty, string.Empty, null, errorMessage, "Update check failed", new List<string> { errorMessage });
    }
}
