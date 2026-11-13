using GenHub.Core.Models.Providers;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Content.Services.Catalog;

/// <summary>
/// Service responsible for autonomously querying upstream providers (GitHub Releases, GeneralsOnline, CommunityOutpost)
/// and hydrating dynamic catalog items with releases and variants before presentation.
/// </summary>
public interface ICatalogUpstreamIngestionService
{
    /// <summary>
    /// Ingests upstream releases and hydrations for all applicable items in the given catalog.
    /// </summary>
    /// <param name="catalog">The publisher catalog to process.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task IngestCatalogAsync(PublisherCatalog catalog, CancellationToken cancellationToken = default);
}
