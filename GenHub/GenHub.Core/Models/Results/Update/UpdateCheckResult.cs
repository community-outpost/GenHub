namespace GenHub.Core.Models.Results;

using System.Collections.Generic;
using GenHub.Core.Models.GitHub;

/// <summary>Represents the result of an update check operation.</summary>
public class UpdateCheckResult(bool isUpdateAvailable = false, string currentVersion = "", string latestVersion = "", string? updateUrl = null, string? releaseNotes = null, string? releaseTitle = null, List<string>? errorMessages = null, IEnumerable<GitHubReleaseAsset>? assets = null, TimeSpan elapsed = default)
: ResultBase(errorMessages == null || errorMessages.Count == 0, errorMessages ?? new List<string>(), elapsed)
    {
    private bool _isUpdateAvailable = isUpdateAvailable;
    private string _currentVersion = currentVersion;
    private string _latestVersion = latestVersion;
    private string? _updateUrl = updateUrl;
    private string? _releaseNotes = releaseNotes;
    private string? _releaseTitle = releaseTitle;
    private List<string> _errorMessages = errorMessages ?? new List<string>();
    private IEnumerable<GitHubReleaseAsset> _assets = assets ?? new List<GitHubReleaseAsset>();

    /// <summary>Gets or sets a value indicating whether an update is available.</summary>
    public bool IsUpdateAvailable { get => _isUpdateAvailable; set => _isUpdateAvailable = value; }

    /// <summary>Gets or sets the current application version.</summary>
    public string CurrentVersion { get => _currentVersion; set => _currentVersion = value; }

    /// <summary>Gets or sets the latest available version.</summary>
    public string LatestVersion { get => _latestVersion; set => _latestVersion = value; }

    /// <summary>Gets or sets the URL to download or view the update.</summary>
    public string? UpdateUrl { get => _updateUrl; set => _updateUrl = value; }

    /// <summary>Gets or sets the release notes for the update.</summary>
    public string? ReleaseNotes { get => _releaseNotes; set => _releaseNotes = value; }

    /// <summary>Gets or sets the title of the release for the update.</summary>
    public string? ReleaseTitle { get => _releaseTitle; set => _releaseTitle = value; }

    /// <summary>Gets or sets the list of error messages encountered during the check.</summary>
    public List<string> ErrorMessages { get => _errorMessages; set => _errorMessages = value; }

    /// <summary>Gets or sets the list of release assets for the update.</summary>
    public IEnumerable<GitHubReleaseAsset> Assets { get => _assets; set => _assets = value; }

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
        return new UpdateCheckResult(
            true,
            string.Empty,
            release.TagName ?? string.Empty,
            release.HtmlUrl ?? string.Empty,
            release.Body ?? string.Empty,
            release.Name ?? string.Empty,
            null,
            release.Assets ?? new List<GitHubReleaseAsset>());
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
        return new UpdateCheckResult(
            false,
            string.Empty,
            string.Empty,
            null,
            errorMessage,
            "Update check failed",
            new List<string> { errorMessage });
    }
}
