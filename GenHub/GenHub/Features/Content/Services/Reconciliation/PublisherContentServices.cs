using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Manifest;

namespace GenHub.Features.Content.Services.Reconciliation;

/// <summary>
/// Aggregates content pipeline services required by <see cref="PublisherProfileReconcilerBase"/>.
/// Constructed by publisher reconcilers from their publisher-specific services.
/// </summary>
public sealed class PublisherContentServices(
    IContentUpdateService updateService,
    IContentManifestPool manifestPool,
    IContentOrchestrator contentOrchestrator,
    IContentReconciliationService reconciliationService)
{
    /// <summary>
    /// Gets the content update service.
    /// </summary>
    public IContentUpdateService UpdateService { get; } = updateService;

    /// <summary>
    /// Gets the content manifest pool.
    /// </summary>
    public IContentManifestPool ManifestPool { get; } = manifestPool;

    /// <summary>
    /// Gets the content orchestrator.
    /// </summary>
    public IContentOrchestrator ContentOrchestrator { get; } = contentOrchestrator;

    /// <summary>
    /// Gets the content reconciliation service.
    /// </summary>
    public IContentReconciliationService ReconciliationService { get; } = reconciliationService;
}
