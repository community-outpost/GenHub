namespace GenHub.Core.Interfaces.Content;

using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Results.Content;

/// <summary>
/// Service to determine the current state of content for UI display.
/// </summary>
public interface IContentStateService
{
    /// <summary>
    /// Event raised when content state changes (downloaded, updated, or removed).
    /// </summary>
    event EventHandler<ContentStateChangedEventArgs>? ContentStateChanged;

    /// <summary>
    /// Notifies subscribers that content state has changed.
    /// </summary>
    /// <param name="contentId">The ID of the content that changed.</param>
    /// <param name="newState">The new state of the content.</param>
    /// <param name="manifestId">The manifest ID if available.</param>
    void NotifyStateChanged(string contentId, ContentState newState, string? manifestId = null);

    /// <summary>
    /// Gets the state for a content search result.
    /// </summary>
    /// <param name="item">The content search result from discovery.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The current state of the content.</returns>
    Task<ContentState> GetStateAsync(ContentSearchResult item, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the state by generating a prospective manifest ID from components.
    /// </summary>
    /// <param name="publisher">Publisher identifier.</param>
    /// <param name="contentType">Content type.</param>
    /// <param name="contentName">Content name.</param>
    /// <param name="releaseDate">Release date (used as version).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The current state of the content.</returns>
    Task<ContentState> GetStateAsync(
        string publisher,
        ContentType contentType,
        string contentName,
        DateTime releaseDate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the local manifest ID if content is downloaded.
    /// </summary>
    /// <param name="item">The content search result.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The local manifest ID if downloaded, null otherwise.</returns>
    Task<string?> GetLocalManifestIdAsync(ContentSearchResult item, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the state for a specific manifest ID, without requiring a full
    /// <see cref="ContentSearchResult"/>. Useful for per-variant state lookups where
    /// only the manifest identity is known.
    /// </summary>
    /// <param name="manifestId">The manifest ID to check.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// <see cref="ContentState.Downloaded"/> if the manifest is in the pool;
    /// <see cref="ContentState.NotDownloaded"/> otherwise.
    /// </returns>
    Task<ContentState> GetStateByManifestIdAsync(string manifestId, CancellationToken cancellationToken = default);
}
