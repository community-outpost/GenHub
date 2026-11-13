namespace GenHub.Features.Downloads.ViewModels;

/// <summary>
/// Describes how a gallery media item should be presented.
/// </summary>
public enum MediaDisplayAction
{
    /// <summary>
    /// The item cannot be displayed.
    /// </summary>
    None,

    /// <summary>
    /// Show the item in the full-screen image viewer.
    /// </summary>
    ShowImage,

    /// <summary>
    /// Stream the item in the in-app video player.
    /// </summary>
    PlayVideo,

    /// <summary>
    /// Open the item in the system browser.
    /// </summary>
    OpenExternally,
}
