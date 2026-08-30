using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Content.Services.ContentProviders;

/// <summary>
/// CNC Labs content provider that orchestrates discovery→resolution→delivery pipeline
/// for CNC Labs-hosted content.
/// </summary>
public class CNCLabsContentProvider(
    IEnumerable<IContentDiscoverer> discoverers,
    IEnumerable<IContentResolver> resolvers,
    IEnumerable<IContentDeliverer> deliverers,
    ILogger<CNCLabsContentProvider> logger,
    IContentValidator contentValidator,
    IInstallationInstructionsService installationInstructionsService)
    : BaseContentProvider(contentValidator, installationInstructionsService, logger)
{
    private readonly IContentDiscoverer _cncLabsDiscoverer = discoverers.FirstOrDefault(d => d.SourceName?.Equals(ContentSourceNames.CNCLabsDiscoverer, StringComparison.OrdinalIgnoreCase) == true)
        ?? throw new ArgumentException("CNC Labs discoverer not found", nameof(discoverers));

    private readonly IContentResolver _cncLabsResolver = resolvers.FirstOrDefault(r => r.ResolverId?.Equals(ContentSourceNames.CNCLabsResolverId, StringComparison.OrdinalIgnoreCase) == true)
        ?? throw new ArgumentException("CNC Labs resolver not found", nameof(resolvers));

    private readonly IContentDeliverer _httpDeliverer = deliverers.FirstOrDefault(d => d.SourceName?.Equals(ContentSourceNames.HttpDeliverer, StringComparison.OrdinalIgnoreCase) == true)
        ?? throw new ArgumentException("HTTP deliverer not found", nameof(deliverers));

    /// <inheritdoc />
    /// <remarks>
    /// Must match the ProviderName set by CNCLabsMapDiscoverer on search results.
    /// </remarks>
    public override string SourceName => CNCLabsConstants.SourceName;

    /// <inheritdoc />
    public override string Description => "Provides maps and content from CNC Labs";

    /// <inheritdoc />
    protected override IContentDiscoverer Discoverer => _cncLabsDiscoverer;

    /// <inheritdoc />
    protected override IContentResolver Resolver => _cncLabsResolver;

    /// <inheritdoc />
    protected override IContentDeliverer Deliverer => _httpDeliverer;

    /// <inheritdoc />
    public override async Task<OperationResult<ContentManifest>> GetValidatedContentAsync(
        string contentId, CancellationToken cancellationToken = default)
    {
        var query = new ContentSearchQuery { SearchTerm = contentId, Take = ContentConstants.SingleResultQueryLimit };
        var searchResult = await SearchAsync(query, cancellationToken);

        if (!searchResult.Success || !searchResult.Data!.Any())
        {
            return OperationResult<ContentManifest>.CreateFailure($"Content not found: {contentId}");
        }

        var result = searchResult.Data!.First();
        var manifest = result.GetData<ContentManifest>();

        return manifest != null
            ? OperationResult<ContentManifest>.CreateSuccess(manifest)
            : OperationResult<ContentManifest>.CreateFailure("Manifest not available in search result");
    }

    /// <inheritdoc />
    protected override async Task<OperationResult<ContentManifest>> PrepareContentInternalAsync(
        ContentManifest manifest,
        string workingDirectory,
        IProgress<ContentAcquisitionProgress>? progress,
        CancellationToken cancellationToken)
    {
        try
        {
            Logger.LogInformation("Preparing CNC Labs content for manifest {ManifestId}", manifest.Id);

            if (!Deliverer.CanDeliver(manifest))
            {
                return OperationResult<ContentManifest>.CreateFailure($"Cannot deliver content for manifest {manifest.Id}");
            }

            // Use the deliverer to handle content acquisition
            var deliveryResult = await Deliverer.DeliverContentAsync(manifest, workingDirectory, progress, cancellationToken);
            if (!deliveryResult.Success)
            {
                return OperationResult<ContentManifest>.CreateFailure($"CNC Labs content delivery failed: {deliveryResult.FirstError}");
            }

            return OperationResult<ContentManifest>.CreateSuccess(deliveryResult.Data ?? manifest);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to prepare CNC Labs content for manifest {ManifestId}", manifest.Id);
            return OperationResult<ContentManifest>.CreateFailure($"CNC Labs content preparation failed: {ex.Message}");
        }
    }
}
