using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Interfaces.Telemetry;

namespace GenHub.Features.Content.Services.Catalog;

/// <summary>
/// Aggregates content pipeline services required by <see cref="GenericCatalogProfileReconciler"/>.
/// </summary>
public sealed class GenericCatalogContentServices(
    IContentManifestPool manifestPool,
    GenericCatalogDiscoverer catalogDiscoverer,
    IContentStateService contentStateService,
    IContentDownloadCoordinator downloadCoordinator,
    IContentReconciliationService reconciliationService,
    IPublisherSubscriptionStore subscriptionStore,
    ITelemetryService? telemetryService = null)
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

    /// <summary>
    /// Gets the publisher subscription store.
    /// </summary>
    public IPublisherSubscriptionStore SubscriptionStore { get; } = subscriptionStore;

    /// <summary>
    /// Gets the optional telemetry service.
    /// </summary>
    public ITelemetryService? TelemetryService { get; } = telemetryService;
}
