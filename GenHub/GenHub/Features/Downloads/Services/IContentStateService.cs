using System;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Results.Content;

namespace GenHub.Features.Downloads.Services;

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

/// <summary>
/// Service to determine the current state of content for UI display.
/// </summary>
public interface IContentStateService
{
    /// <summary>
    /// Gets the state for a content search result.
    /// </summary>
    /// <param name="item">The content search result from discovery.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <summary>
/// Determine the current UI state for a discovered content item.
/// </summary>
/// <param name="item">The content search result from discovery.</param>
/// <param name="cancellationToken">Token to cancel the operation.</param>
/// <returns>A <see cref="ContentState"/> indicating whether the content is not downloaded, has an update available, or is downloaded and up to date.</returns>
    Task<ContentState> GetStateAsync(ContentSearchResult item, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the state by generating a prospective manifest ID from components.
    /// </summary>
    /// <param name="publisher">Publisher identifier.</param>
    /// <param name="contentType">Content type.</param>
    /// <param name="contentName">Content name.</param>
    /// <param name="releaseDate">Release date (used as version).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <summary>
        /// Determine the UI state for content identified by the provided components.
        /// </summary>
        /// <param name="publisher">The publisher identifier of the content.</param>
        /// <param name="contentType">The type/category of the content.</param>
        /// <param name="contentName">The name of the content.</param>
        /// <param name="releaseDate">The release date used as the content version to compare against local installs.</param>
        /// <param name="cancellationToken">Token to cancel the operation.</param>
        /// <returns>The content's <see cref="ContentState"/>: NotDownloaded, UpdateAvailable, or Downloaded.</returns>
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
    /// <summary>
/// Retrieves the local manifest ID for the specified content if it is downloaded locally.
/// </summary>
/// <param name="item">The discovered content search result to check for a local installation.</param>
/// <param name="cancellationToken">Token to cancel the operation.</param>
/// <returns>The local manifest ID when the content is downloaded, or <c>null</c> if it is not downloaded.</returns>
    Task<string?> GetLocalManifestIdAsync(ContentSearchResult item, CancellationToken cancellationToken = default);
}