using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.Content.Services.ContentProviders;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Content.Services.Catalog;

/// <summary>
/// Content provider that acquires content from any subscribed publisher catalog
/// through the generic catalog pipeline (resolution → HTTP delivery → manifest factory).
/// </summary>
public class GenericCatalogContentProvider(
    GenericCatalogDiscoverer discoverer,
    IEnumerable<IContentResolver> resolvers,
    IEnumerable<IContentDeliverer> deliverers,
    GenericCatalogManifestFactory manifestFactory,
    ILogger<GenericCatalogContentProvider> logger,
    IContentValidator contentValidator,
    IInstallationInstructionsService installationInstructionsService)
    : BaseContentProvider(contentValidator, installationInstructionsService, logger)
{
    private readonly IContentResolver _genericCatalogResolver = resolvers.FirstOrDefault(r =>
        string.Equals(r.ResolverId, CatalogConstants.GenericCatalogResolverId, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException("Generic catalog resolver not found");

    private readonly IContentDeliverer _httpDeliverer = deliverers.FirstOrDefault(d =>
        string.Equals(d.SourceName, ContentSourceNames.HttpDeliverer, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException("HTTP deliverer not found");

    /// <inheritdoc />
    public override string SourceName => CatalogConstants.GenericCatalogProviderName;

    /// <inheritdoc />
    public override string Description => "Provides content from subscribed publisher catalogs";

    /// <inheritdoc />
    protected override IContentDiscoverer Discoverer => discoverer;

    /// <inheritdoc />
    protected override IContentResolver Resolver => _genericCatalogResolver;

    /// <inheritdoc />
    protected override IContentDeliverer Deliverer => _httpDeliverer;

    /// <inheritdoc />
    public override Task<OperationResult<IEnumerable<ContentSearchResult>>> SearchAsync(
        ContentSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        // Subscription feeds are browsed through per-subscription discoverers configured
        // with a catalog URL. This provider has no subscription scope, so global search
        // has nothing to return; acquisition still works through resolution metadata.
        return Task.FromResult(OperationResult<IEnumerable<ContentSearchResult>>.CreateSuccess([]));
    }

    /// <inheritdoc />
    public override Task<OperationResult<ContentManifest>> GetValidatedContentAsync(
        string contentId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(contentId))
        {
            return Task.FromResult(OperationResult<ContentManifest>.CreateFailure("Content ID cannot be null or empty"));
        }

        return Task.FromResult(OperationResult<ContentManifest>.CreateFailure(
            $"Generic catalog content '{contentId}' requires resolution metadata and cannot be fetched by ID alone."));
    }

    /// <inheritdoc />
    protected override Task<OperationResult<ContentManifest>> PrepareContentInternalAsync(
        ContentManifest manifest,
        string workingDirectory,
        IProgress<ContentAcquisitionProgress>? progress,
        CancellationToken cancellationToken)
    {
        Logger.LogInformation("Preparing generic catalog content: {ManifestId} ({Name})", manifest.Id, manifest.Name);

        return DeliverAndEnrichContentAsync(
            _httpDeliverer,
            manifestFactory,
            manifest,
            workingDirectory,
            progress,
            cancellationToken);
    }
}
