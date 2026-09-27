using System.Collections.Generic;

namespace GenHub.Features.Tools.Interfaces;

/// <summary>
/// Factory for creating and managing hosting provider instances.
/// </summary>
public interface IHostingProviderFactory
{
    /// <summary>
    /// Gets all available hosting providers.
    /// </summary>
    /// <returns>Collection of available hosting providers.</returns>
    IReadOnlyList<IHostingProvider> GetAllProviders();

    /// <summary>
    /// Gets a hosting provider by its ID.
    /// </summary>
    /// <param name="providerId">The provider ID.</param>
    /// <returns>The hosting provider, or null if not found.</returns>
    IHostingProvider? GetProvider(string providerId);

    /// <summary>
    /// Gets providers that support catalog hosting.
    /// </summary>
    /// <returns>Collection of providers that can host catalogs.</returns>
    IReadOnlyList<IHostingProvider> GetCatalogHostingProviders();

    /// <summary>
    /// Gets providers that support artifact hosting.
    /// </summary>
    /// <returns>Collection of providers that can host artifacts.</returns>
    IReadOnlyList<IHostingProvider> GetArtifactHostingProviders();
}
