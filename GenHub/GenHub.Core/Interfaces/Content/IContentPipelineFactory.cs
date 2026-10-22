using GenHub.Core.Models.Providers;

namespace GenHub.Core.Interfaces.Content;

/// <summary>
/// Factory for obtaining content pipeline components (discoverer, resolver, deliverer)
/// based on provider ID. Matches provider definitions to their implementations.
/// </summary>
public interface IContentPipelineFactory
{
    /// <summary>
    /// Gets a content discoverer that matches the given provider ID.
    /// Matches against the discoverer's SourceName property.
    /// </summary>
    /// <param name="providerId">The provider ID to match (e.g., "communityoutpost", "moddb").</param>
    /// <returns>The matching discoverer, or null if not found.</returns>
    IContentDiscoverer? GetDiscoverer(string providerId);

    /// <summary>
    /// Gets a content resolver that matches the given provider ID.
    /// Matches against <see cref="IContentResolver.ResolverId"/>.
    /// </summary>
    /// <param name="providerId">The provider ID to match.</param>
    /// <returns>The matching resolver, or null if not found.</returns>
    IContentResolver? GetResolver(string providerId);

    /// <summary>
    /// Gets a content deliverer that matches the given provider ID.
    /// Matches against the deliverer's SourceName property.
    /// </summary>
    /// <param name="providerId">The provider ID to match.</param>
    /// <returns>The matching deliverer, or null if not found.</returns>
    IContentDeliverer? GetDeliverer(string providerId);

    /// <summary>
    /// Gets all registered discoverers.
    /// </summary>
    /// <returns>All available content discoverers.</returns>
    IEnumerable<IContentDiscoverer> GetAllDiscoverers();

    /// <summary>
    /// Gets all registered resolvers.
    /// </summary>
    /// <returns>All available content resolvers.</returns>
    IEnumerable<IContentResolver> GetAllResolvers();

    /// <summary>
    /// Gets all registered deliverers.
    /// </summary>
    /// <returns>All available content deliverers.</returns>
    IEnumerable<IContentDeliverer> GetAllDeliverers();

    /// <summary>
    /// Gets the complete pipeline (discoverer, resolver, deliverer) for a provider.
    /// </summary>
    /// <param name="provider">The provider definition.</param>
    /// <returns>A tuple containing the matched components (any may be null if not found).</returns>
    (IContentDiscoverer? Discoverer, IContentResolver? Resolver, IContentDeliverer? Deliverer)
        GetPipeline(ProviderDefinition provider);
}
