namespace GenHub.Core.Models.Results.Content;

using GenHub.Core.Models.Enums;

/// <summary>
/// Event arguments for content state changes.
/// </summary>
public class ContentStateChangedEventArgs : EventArgs
{
    /// <summary>
    /// Gets the ID of the content that changed state.
    /// </summary>
    public string ContentId { get; }

    /// <summary>
    /// Gets the new state of the content.
    /// </summary>
    public ContentState NewState { get; }

    /// <summary>
    /// Gets the manifest ID if available.
    /// </summary>
    public string? ManifestId { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ContentStateChangedEventArgs"/> class.
    /// </summary>
    /// <param name="contentId">The ID of the content that changed.</param>
    /// <param name="newState">The new state of the content.</param>
    /// <param name="manifestId">The manifest ID if available.</param>
    public ContentStateChangedEventArgs(string contentId, ContentState newState, string? manifestId = null)
    {
        ContentId = contentId;
        NewState = newState;
        ManifestId = manifestId;
    }
}
