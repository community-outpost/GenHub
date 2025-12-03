using GenHub.Core.Models.Content;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Results;

namespace GenHub.Core.Interfaces.Providers;

/// <summary>
/// Parses raw catalog content into content search results.
/// </summary>
/// <remarks>
/// <para>
/// Parsers receive pre-fetched catalog data (string) from the discoverer and transform it
/// into structured <see cref="ContentSearchResult"/> objects. Each parser handles a specific
/// catalog format (e.g., GenPatcher dl.dat, JSON API, GitHub releases).
/// </para>
/// <para>
/// Parsers should not make HTTP calls - that's the discoverer's job. They also don't create
/// manifests (resolver) or download files (deliverer).
/// </para>
/// <para>
/// Pipeline: Discoverer → Parser → Resolver → Deliverer → Factory.
/// </para>
/// </remarks>
public interface ICatalogParser
{
    /// <summary>
    /// Gets the catalog format identifier this parser handles.
    /// Examples: "genpatcher-dat", "generalsonline-json-api", "github-releases".
    /// </summary>
    string CatalogFormat { get; }

    /// <summary>
    /// Parses raw catalog content into content search results.
    /// </summary>
    /// <param name="catalogContent">
    /// The raw catalog data already fetched by the discoverer. Format depends on the
    /// catalog type (JSON, XML, custom text format like dl.dat, etc.).
    /// </param>
    /// <param name="provider">
    /// Provider configuration used for metadata enrichment (mirrors, tags, endpoints).
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// Parsed content items with ResolverId and ResolverMetadata populated for downstream resolution.
    /// </returns>
    Task<OperationResult<IEnumerable<ContentSearchResult>>> ParseAsync(
        string catalogContent,
        ProviderDefinition provider,
        CancellationToken cancellationToken = default);
}
