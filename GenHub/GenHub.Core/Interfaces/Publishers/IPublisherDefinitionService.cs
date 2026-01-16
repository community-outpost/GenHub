using System.Collections.Generic;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Results;

namespace GenHub.Core.Interfaces.Publishers;

/// <summary>
/// Service for fetching and processing publisher definitions.
/// Handles the "Tier 3" URL layer - fetching provider.json to discover catalog locations.
/// </summary>
public interface IPublisherDefinitionService
{
    /// <summary>
    /// Fetches and parses a provider definition from a URL.
    /// </summary>
    /// <param name="definitionUrl">The URL to the provider.json file.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result containing the parsed definition.</returns>
    Task<OperationResult<PublisherDefinition>> FetchDefinitionAsync(
        string definitionUrl,
        CancellationToken ct = default);

    /// <summary>
    /// Fetches the catalog using the URLs found in the provider definition.
    /// Will try the primary CatalogUrl first, then fall back to CatalogMirrors.
    /// </summary>
    /// <param name="definition">The provider definition containing catalog URLs.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result containing the parsed catalog.</returns>
    Task<OperationResult<PublisherCatalog>> FetchCatalogFromDefinitionAsync(
        PublisherDefinition definition,
        CancellationToken ct = default);

    /// <summary>
    /// Checks if a subscription needs to be updated based on its definition URL.
    /// </summary>
    /// <param name="subscription">The subscription to check.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Result indicating if an update was found and applied to the subscription object (not saved).</returns>
    Task<OperationResult<bool>> CheckForDefinitionUpdateAsync(
        PublisherSubscription subscription,
        CancellationToken ct = default);

    /// <summary>
    /// Fetches all catalogs defined in a publisher definition.
    /// </summary>
    Task<OperationResult<Dictionary<string, PublisherCatalog>>> FetchAllCatalogsAsync(
        PublisherDefinition definition,
        CancellationToken ct = default);
}
