using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;

namespace GenHub.Core.Interfaces.Content;

/// <summary>
/// Resolves a discovered content item into a downloadable manifest.
/// </summary>
/// <remarks>
/// <para>
/// Resolvers take a <see cref="ContentSearchResult"/> from the parser and create a
/// <see cref="ContentManifest"/> with download URLs, publisher info, dependencies, and metadata.
/// The manifest is ready for the deliverer to download.
/// </para>
/// <para>
/// Resolvers should not download files (deliverer), compute file hashes (factory), or
/// parse catalogs (parser). They also shouldn't delegate manifest creation to factories -
/// factories are for post-extraction processing only.
/// </para>
/// <para>
/// Pipeline: Discoverer → Parser → Resolver → Deliverer → Factory.
/// </para>
/// </remarks>
public interface IContentResolver
{
    /// <summary>
    /// Gets the unique identifier for this resolver. Should match the ResolverId set in
    /// <see cref="ContentSearchResult"/> by the parser.
    /// </summary>
    string ResolverId { get; }

    /// <summary>
    /// Resolves a discovered content item into a full manifest.
    /// </summary>
    /// <param name="discoveredItem">The content item from parser output.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A manifest with valid ID, publisher info, download URLs in Files[], and dependencies.
    /// </returns>
    Task<OperationResult<ContentManifest>> ResolveAsync(
        ContentSearchResult discoveredItem,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves a discovered content item using provider configuration.
    /// </summary>
    /// <param name="provider">Provider definition with endpoint configuration.</param>
    /// <param name="discoveredItem">The content item from parser output.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A manifest ready for the deliverer.</returns>
    Task<OperationResult<ContentManifest>> ResolveAsync(
        ProviderDefinition? provider,
        ContentSearchResult discoveredItem,
        CancellationToken cancellationToken = default)
    {
        return ResolveAsync(discoveredItem, cancellationToken);
    }
}