using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Features.Content.Services.ContentDiscoverers;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Content.Services.ContentProviders;

/// <summary>
/// Content provider that orchestrates discovery→resolution→delivery pipeline
/// for base game installations from verified CSV registries.
/// </summary>
public class CsvContentProvider(
    IEnumerable<IContentDiscoverer> discoverers,
    IEnumerable<IContentResolver> resolvers,
    IEnumerable<IContentDeliverer> deliverers,
    ILogger<CsvContentProvider> logger,
    IContentValidator contentValidator,
    IInstallationInstructionsService installationInstructionsService)
    : BaseContentProvider(contentValidator, installationInstructionsService, logger)
{
    private readonly IContentDiscoverer _discoverer = discoverers.OfType<CsvDiscoverer>().FirstOrDefault()
        ?? discoverers.FirstOrDefault(d => string.Equals(d.SourceName, CsvConstants.SourceName, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException("CSV discoverer not found");

    private readonly IContentResolver _resolver = resolvers.FirstOrDefault(r =>
        string.Equals(r.ResolverId, CsvConstants.ResolverId, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException("CSV resolver not found");

    private readonly IContentDeliverer _deliverer = deliverers.FirstOrDefault(d =>
        string.Equals(d.SourceName, ContentSourceNames.HttpDeliverer, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException("HTTP deliverer not found");

    /// <inheritdoc />
    public override string SourceName => PublisherTypeConstants.CsvRegistry;

    /// <inheritdoc />
    public override string Description => CsvConstants.Description;

    /// <inheritdoc />
    protected override IContentDiscoverer Discoverer => _discoverer;

    /// <inheritdoc />
    protected override IContentResolver Resolver => _resolver;

    /// <inheritdoc />
    protected override IContentDeliverer Deliverer => _deliverer;

    /// <inheritdoc />
    public override async Task<OperationResult<ContentManifest>> GetValidatedContentAsync(
        string contentId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(contentId))
        {
            return OperationResult<ContentManifest>.CreateFailure("Content ID cannot be null or empty.");
        }

        var query = new ContentSearchQuery { SearchTerm = contentId, Take = ContentConstants.SingleResultQueryLimit };
        var searchResult = await SearchAsync(query, cancellationToken);

        if (!searchResult.Success || searchResult.Data == null || !searchResult.Data.Any())
        {
            return OperationResult<ContentManifest>.CreateFailure(
                $"Content not found for ID '{contentId}': {searchResult.FirstError ?? "No matching results"}");
        }

        var result = searchResult.Data.FirstOrDefault(r => string.Equals(r.Id, contentId, StringComparison.OrdinalIgnoreCase));
        if (result == null)
        {
            return OperationResult<ContentManifest>.CreateFailure(
                $"Content not found for ID '{contentId}'.");
        }

        var manifest = result.GetData<ContentManifest>();

        return manifest != null
            ? OperationResult<ContentManifest>.CreateSuccess(manifest)
            : OperationResult<ContentManifest>.CreateFailure($"Invalid manifest data for content ID '{contentId}'");
    }

    /// <inheritdoc />
    protected override async Task<OperationResult<ContentManifest>> PrepareContentInternalAsync(
        ContentManifest manifest,
        string workingDirectory,
        IProgress<ContentAcquisitionProgress>? progress,
        CancellationToken cancellationToken)
    {
        Logger.LogDebug("Preparing CSV catalog content for manifest {ManifestId}", manifest.Id);

        if (!Deliverer.CanDeliver(manifest))
        {
            return OperationResult<ContentManifest>.CreateFailure(
                $"Cannot deliver content for manifest {manifest.Id}");
        }

        var deliveryResult = await Deliverer.DeliverContentAsync(
            manifest,
            workingDirectory,
            progress,
            cancellationToken);

        if (!deliveryResult.Success)
        {
            return OperationResult<ContentManifest>.CreateFailure(
                $"Content delivery failed: {deliveryResult.FirstError}");
        }

        return OperationResult<ContentManifest>.CreateSuccess(deliveryResult.Data ?? manifest);
    }
}
