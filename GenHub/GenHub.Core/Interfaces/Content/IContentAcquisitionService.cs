using System;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;

namespace GenHub.Core.Interfaces.Content;

/// <summary>
/// Service for acquiring content from providers.
/// Handles discovery, resolution, download, and installation of content.
/// </summary>
public interface IContentAcquisitionService
{
    /// <summary>
    /// Acquires GeneralsOnline content for the specified variant.
    /// </summary>
    /// <param name="variant">The variant (e.g., "30hz", "60hz").</param>
    /// <param name="existingInstallationPath">Optional path to an existing installation to use instead of a clean install.</param>
    /// <param name="progress">Optional progress reporter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Operation result containing the acquired content manifest.</returns>
    Task<OperationResult<ContentManifest>> AcquireGeneralsOnlineContentAsync(
        string variant,
        string? existingInstallationPath,
        IProgress<ContentAcquisitionProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Acquires SuperHackers content for the specified game type.
    /// </summary>
    /// <param name="gameType">The target game type (Generals or ZeroHour).</param>
    /// <param name="existingInstallationPath">Optional path to an existing installation to use instead of a clean install.</param>

    /// <param name="progress">Optional progress reporter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Operation result containing the acquired content manifest.</returns>
    Task<OperationResult<ContentManifest>> AcquireSuperHackersContentAsync(
        GameType gameType,
        string? existingInstallationPath,
        IProgress<ContentAcquisitionProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Acquires content from a search result.
    /// </summary>
    /// <param name="searchResult">The content search result to acquire.</param>
    /// <param name="progress">Optional progress reporter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Operation result containing the acquired content manifest.</returns>
    Task<OperationResult<ContentManifest>> AcquireContentFromSearchResultAsync(
        ContentSearchResult searchResult,
        IProgress<ContentAcquisitionProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
