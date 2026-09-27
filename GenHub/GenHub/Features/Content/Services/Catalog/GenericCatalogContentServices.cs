using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Manifest;

namespace GenHub.Features.Content.Services.Catalog;

/// <summary>
/// Aggregates content pipeline services required by <see cref="GenericCatalogProfileReconciler"/>.
/// </summary>
public sealed class GenericCatalogContentServices(
    IContentManifestPool manifestPool,
    GenericCatalogDiscoverer catalogDiscoverer,
    IContentStateService contentStateService,
    IContentDownloadCoordinator downloadCoordinator,
    IContentReconciliationService reconciliationService)
{
    /// <summary>
    /// Gets the content manifest pool.
    /// </summary>
    public IContentManifestPool ManifestPool { get; } = manifestPool;

    /// <summary>
    /// Gets the generic catalog discoverer.
    /// </summary>
    public GenericCatalogDiscoverer CatalogDiscoverer { get; } = catalogDiscoverer;

    /// <summary>
    /// Gets the content state service.
    /// </summary>
    public IContentStateService ContentStateService { get; } = contentStateService;

    /// <summary>
    /// Gets the content download coordinator.
    /// </summary>
    public IContentDownloadCoordinator DownloadCoordinator { get; } = downloadCoordinator;

    /// <summary>
    /// Gets the content reconciliation service.
    /// </summary>
    public IContentReconciliationService ReconciliationService { get; } = reconciliationService;
}
