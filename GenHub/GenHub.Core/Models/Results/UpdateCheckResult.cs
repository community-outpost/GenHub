using System.Collections.Generic;
using GenHub.Core.Models.GitHub;

namespace GenHub.Core.Models.Results;

/// <summary>
/// Represents the result of an update check operation.
/// </summary>
public class UpdateCheckResult
{
    /// <summary>
    /// Gets or sets a value indicating whether an update is available.
    /// </summary>
    public bool IsUpdateAvailable { get; set; }

    /// <summary>
    /// Gets or sets the current application version.
    /// </summary>
    public string CurrentVersion { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the latest available version.
    /// </summary>
    public string LatestVersion { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the URL to download or view the update.
    /// </summary>
    public string? UpdateUrl { get; set; }

    /// <summary>
    /// Gets or sets the release notes for the update.
    /// </summary>
    public string? ReleaseNotes { get; set; }

    /// <summary>
    /// Gets or sets the title of the release for the update.
    /// </summary>
    public string? ReleaseTitle { get; set; }

    /// <summary>
    /// Gets or sets the list of error messages encountered during the check.
    /// </summary>
    public List<string> ErrorMessages { get; set; } = new List<string>();

    /// <summary>
    /// Gets a value indicating whether any errors occurred during the update check.
    /// </summary>
    public bool HasErrors => ErrorMessages.Count > 0;

    /// <summary>
    /// Gets or sets the list of release assets for the update.
    /// </summary>
    public IEnumerable<GitHubReleaseAsset> Assets { get; set; } = new List<GitHubReleaseAsset>();

    /// <summary>
    /// Creates an UpdateCheckResult indicating no update is available.
    /// </summary>
    /// <returns>An UpdateCheckResult with IsUpdateAvailable set to false.</returns>
    public static UpdateCheckResult NoUpdateAvailable()
    {
        return new UpdateCheckResult
        {
            IsUpdateAvailable = false,
        };
    }

    /// <summary>
    /// Creates an UpdateCheckResult indicating an update is available.
    /// </summary>
    /// <param name="release">The available release.</param>
    /// <returns>An UpdateCheckResult with update information.</returns>
    public static UpdateCheckResult UpdateAvailable(GitHubRelease release)
    {
        return new UpdateCheckResult
        {
            IsUpdateAvailable = true,
            LatestVersion = release.TagName,
            UpdateUrl = release.HtmlUrl,
            ReleaseNotes = release.Body,
            ReleaseTitle = release.Name,
            Assets = release.Assets ?? new List<GitHubReleaseAsset>(),
        };
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
        return new UpdateCheckResult
        {
            IsUpdateAvailable = false,
            CurrentVersion = currentVersion,
            LatestVersion = latestVersion,
            UpdateUrl = updateUrl,
        };
    }

    /// <summary>
    /// Creates an UpdateCheckResult indicating an error occurred.
    /// </summary>
    /// <param name="errorMessage">The error message.</param>
    /// <returns>An UpdateCheckResult with error information.</returns>
    public static UpdateCheckResult Error(string errorMessage)
    {
        return new UpdateCheckResult
        {
            IsUpdateAvailable = false,
            ErrorMessages = new List<string> { errorMessage },
        };
    }
}
