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
    /// <returns>Summary of the refresh operation.</returns>
    Task<OperationResult<bool>> RefreshAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Refreshes a specific publisher's catalog.
    /// </summary>
    /// <param name="publisherId">The publisher identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if refreshed, false otherwise.</returns>
    Task<OperationResult<bool>> RefreshPublisherAsync(string publisherId, CancellationToken cancellationToken = default);
}
