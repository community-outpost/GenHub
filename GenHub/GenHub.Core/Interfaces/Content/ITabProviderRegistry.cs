using GenHub.Core.Models.Content;
using GenHub.Core.Models.Results.Content;

namespace GenHub.Core.Interfaces.Content;

/// <summary>
/// Registry for managing custom tab providers.
/// </summary>
public interface ITabProviderRegistry
{
    /// <summary>
    /// Registers a tab provider.
    /// </summary>
    /// <param name="provider">The provider to register.</param>
    void RegisterProvider(ITabProvider provider);

    /// <summary>
    /// Unregisters a tab provider.
    /// </summary>
    /// <param name="providerId">The id of the provider to unregister.</param>
    /// <returns>True if the provider was found and removed.</returns>
    bool UnregisterProvider(string providerId);

    /// <summary>
    /// Gets all registered tab providers.
    /// </summary>
    /// <returns>Read-only list of all registered providers.</returns>
    IReadOnlyList<ITabProvider> GetAllProviders();

    /// <summary>
    /// Gets all custom tabs for the given content from all registered providers.
    /// </summary>
    /// <param name="searchResult">The content to get tabs for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Read-only list of custom tab definitions, sorted by order.</returns>
    Task<IReadOnlyList<CustomTabDefinition>> GetTabsForContentAsync(
        ContentSearchResult searchResult,
        CancellationToken cancellationToken = default);
}
