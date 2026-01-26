using GenHub.Core.Models.Results;

namespace GenHub.Core.Interfaces.Providers;

/// <summary>
/// Service for refreshing subscribed publisher catalogs.
/// </summary>
public interface IPublisherCatalogRefreshService
{
    /// <summary>
    /// Refreshes all subscribed catalogs.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <summary>
/// Refreshes all subscribed publisher catalogs.
/// </summary>
/// <param name="cancellationToken">Token to cancel the refresh operation.</param>
/// <returns>An <see cref="OperationResult{T}"/> containing `true` if the refresh succeeded, `false` otherwise.</returns>
    Task<OperationResult<bool>> RefreshAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Refreshes a specific publisher's catalog.
    /// </summary>
    /// <param name="publisherId">The publisher identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <summary>
/// Refreshes the catalog for the specified publisher.
/// </summary>
/// <param name="publisherId">The identifier of the publisher whose catalog to refresh.</param>
/// <returns>OperationResult containing `true` if the publisher's catalog was refreshed, `false` otherwise.</returns>
    Task<OperationResult<bool>> RefreshPublisherAsync(string publisherId, CancellationToken cancellationToken = default);
}