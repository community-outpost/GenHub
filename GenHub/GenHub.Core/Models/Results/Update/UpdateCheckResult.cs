namespace GenHub.Core.Models.Results;

using System.Collections.Generic;
using GenHub.Core.Models.GitHub;

/// <summary>Represents the result of an update check operation.</summary>
public class UpdateCheckResult : ResultBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UpdateCheckResult"/> class.
    /// </summary>
    /// <param name="isUpdateAvailable">Whether an update is available.</param>
    /// <param name="currentVersion">The current version.</param>
    /// <param name="latestVersion">The latest version.</param>
    /// <param name="updateUrl">The update URL.</param>
    /// <param name="releaseNotes">The release notes.</param>
    /// <param name="releaseTitle">The release title.</param>
    /// <param name="errorMessages">The error messages.</param>
    /// <param name="assets">The release assets.</param>
    /// <param name="elapsed">The elapsed time.</param>
    internal UpdateCheckResult(
        bool isUpdateAvailable = false,
        string currentVersion = "",
        string latestVersion = "",
        string? updateUrl = null,
        string? releaseNotes = null,
        string? releaseTitle = null,
        List<string>? errorMessages = null,
        IEnumerable<GitHubReleaseAsset>? assets = null,
        TimeSpan elapsed = default)
        : base(errorMessages == null || errorMessages.Count == 0, errorMessages ?? new List<string>(), elapsed)
    {
        IsUpdateAvailable = isUpdateAvailable;
        CurrentVersion = currentVersion;
        LatestVersion = latestVersion;
        UpdateUrl = updateUrl;
        ReleaseNotes = releaseNotes;
        ReleaseTitle = releaseTitle;
        ErrorMessages = errorMessages ?? new List<string>();
        Assets = assets ?? new List<GitHubReleaseAsset>();
    }

    // Properties initialized from primary constructor parameters

    /// <summary>Gets a value indicating whether an update is available.</summary>
    public bool IsUpdateAvailable { get; private set; }

    /// <summary>Gets the current version.</summary>
    public string CurrentVersion { get; private set; }

    /// <summary>Gets the latest version.</summary>
    public string LatestVersion { get; private set; }

    /// <summary>Gets the update URL.</summary>
    public string? UpdateUrl { get; private set; }

    /// <summary>Gets the release notes.</summary>
    public string? ReleaseNotes { get; private set; }

    /// <summary>Gets the release title.</summary>
    public string? ReleaseTitle { get; private set; }

    /// <summary>Gets the error messages.</summary>
    public List<string> ErrorMessages { get; private set; }

    /// <summary>Gets the release assets.</summary>
    public IEnumerable<GitHubReleaseAsset> Assets { get; private set; }

    // -------------------------
    // Factory Methods
    // -------------------------

    /// <summary>
    /// Creates an <see cref="UpdateCheckResult"/> for the initial state before checking.
    /// </summary>
    /// <returns>An <see cref="UpdateCheckResult"/> with default values for initialization.</returns>
    public static UpdateCheckResult CreateInitial() =>
        new(false, string.Empty, string.Empty, string.Empty,
            "Click 'Check For Updates' to begin.",
            "Ready to check for updates");

    /// <summary>
    /// Creates an <see cref="UpdateCheckResult"/> for the checking state.
    /// </summary>
    /// <param name="currentVersion">The current version.</param>
    /// <param name="latestVersion">The latest version.</param>
    /// <returns>An <see cref="UpdateCheckResult"/> indicating checking is in progress.</returns>
    public static UpdateCheckResult CreateChecking(string currentVersion, string latestVersion) =>
        new(false, currentVersion, latestVersion, string.Empty,
            "Please wait while we check for updates.",
            "Checking for updates...");

    /// <summary>
    /// Creates an <see cref="UpdateCheckResult"/> for the dismissed state.
    /// </summary>
    /// <param name="currentVersion">The current version.</param>
    /// <param name="latestVersion">The latest version.</param>
    /// <returns>An <see cref="UpdateCheckResult"/> with cleared update information.</returns>
    public static UpdateCheckResult CreateDismissed(string currentVersion, string latestVersion) =>
        new(false, currentVersion, latestVersion, string.Empty,
            string.Empty, string.Empty);

    /// <summary>
    /// Creates an <see cref="UpdateCheckResult"/> indicating no update is available.
    /// </summary>
    /// <param name="currentVersion">The current version.</param>
    /// <param name="latestVersion">The latest version.</param>
    /// <param name="updateUrl">The update URL.</param>
    /// <returns>An <see cref="UpdateCheckResult"/> with no update available.</returns>
    public static UpdateCheckResult NoUpdateAvailable(string currentVersion = "", string latestVersion = "", string updateUrl = "") =>
        new(false, currentVersion, latestVersion, updateUrl,
            "Your application is up to date.",
            "No updates available");

    /// <summary>
    /// Creates an <see cref="UpdateCheckResult"/> indicating an update is available.
    /// </summary>
    /// <param name="release">The available release.</param>
    /// <param name="currentVersion">The current version.</param>
    /// <returns>An <see cref="UpdateCheckResult"/> with update information.</returns>
    public static UpdateCheckResult UpdateAvailable(GitHubRelease release, string currentVersion) =>
        new(true, currentVersion, release.TagName ?? string.Empty,
            release.HtmlUrl ?? string.Empty,
            release.Body ?? string.Empty,
            release.Name ?? string.Empty,
            null, release.Assets ?? new List<GitHubReleaseAsset>());

    /// <summary>
    /// Creates an <see cref="UpdateCheckResult"/> indicating an error occurred.
    /// </summary>
    /// <param name="errorMessage">The error message.</param>
    /// <returns>An <see cref="UpdateCheckResult"/> with error information.</returns>
    public static UpdateCheckResult Error(string errorMessage) =>
        new(false, string.Empty, string.Empty, null,
            errorMessage, "Update check failed",
            new List<string> { errorMessage });
}
