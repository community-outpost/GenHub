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
    IContentValidator contentValidator)
    : BaseContentProvider(contentValidator, logger)
{
    private readonly IContentDiscoverer _cncLabsDiscoverer = discoverers.FirstOrDefault(d => d.SourceName?.Equals(ContentSourceNames.CNCLabsDiscoverer, StringComparison.OrdinalIgnoreCase) == true)
        ?? throw new ArgumentException("CNC Labs discoverer not found", nameof(discoverers));

    private readonly IContentResolver _cncLabsResolver = resolvers.FirstOrDefault(r => r.ResolverId?.Equals(ContentSourceNames.CNCLabsResolverId, StringComparison.OrdinalIgnoreCase) == true)
        ?? throw new ArgumentException("CNC Labs resolver not found", nameof(resolvers));

    private readonly IContentDeliverer _httpDeliverer = deliverers.FirstOrDefault(d => d.SourceName?.Equals(ContentSourceNames.HttpDeliverer, StringComparison.OrdinalIgnoreCase) == true)
        ?? throw new ArgumentException("HTTP deliverer not found", nameof(deliverers));

    /// <inheritdoc />
    public override string SourceName => "CNC Labs";

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
    protected override Task<OperationResult<ContentManifest>> PrepareContentInternalAsync(
        ContentManifest manifest,
        string workingDirectory,
        IProgress<ContentAcquisitionProgress>? progress,
        CancellationToken cancellationToken)
    {
        try
        {
            Logger.LogDebug("Preparing CNC Labs content for manifest {ManifestId}", manifest.Id);

            progress?.Report(new ContentAcquisitionProgress
            {
                Phase = ContentAcquisitionPhase.Downloading,
                CurrentOperation = "Preparing CNC Labs content...",
            });

            // For CNC Labs content, typically just return the manifest as content preparation
            // is handled by the delivery pipeline
            return Task.FromResult(OperationResult<ContentManifest>.CreateSuccess(manifest));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to prepare CNC Labs content for manifest {ManifestId}", manifest.Id);
            return Task.FromResult(OperationResult<ContentManifest>.CreateFailure($"CNC Labs content preparation failed: {ex.Message}"));
        }
    }
}