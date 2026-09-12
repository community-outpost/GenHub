namespace GenHub.Core.Models.Enums;

/// <summary>
/// Content state for UI display - determines which button to show.
/// </summary>
public enum ContentState
{
    /// <summary>
    /// Content has not been downloaded yet. Show "Download" button.
    /// </summary>
    NotDownloaded,

    /// <summary>
    /// Content exists locally but a newer version is available (same publisher+name, newer date).
    /// Show "Update" button.
    /// </summary>
    UpdateAvailable,

    /// <summary>
    /// Content is downloaded and up-to-date. Show "Add to Profile" dropdown.
    /// </summary>
    Downloaded,
}
