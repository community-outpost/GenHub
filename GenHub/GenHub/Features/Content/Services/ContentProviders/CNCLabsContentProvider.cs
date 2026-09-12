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
using GenHub.Features.Content.Services.Publishers;
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
    CNCLabsManifestFactory manifestFactory,
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
        if (string.IsNullOrWhiteSpace(contentId))
        {
            return OperationResult<ContentManifest>.CreateFailure("Content ID cannot be null or empty");
        }

        var query = new ContentSearchQuery { SearchTerm = contentId, Take = ContentConstants.SingleResultQueryLimit };
        var searchResult = await SearchAsync(query, cancellationToken);

        if (!searchResult.Success || !searchResult.Data!.Any())
        {
            return OperationResult<ContentManifest>.CreateFailure(
                $"Content not found for ID '{contentId}': {searchResult.FirstError ?? "No matching results"}");
        }

        var result = searchResult.Data!.First();
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
        Logger.LogInformation("Preparing CNC Labs content: {ManifestId} ({Name})", manifest.Id, manifest.Name);

        try
        {
            if (!_httpDeliverer.CanDeliver(manifest))
            {
                return OperationResult<ContentManifest>.CreateFailure(
                    $"Cannot deliver content for manifest {manifest.Id}");
            }

            var deliveryResult = await _httpDeliverer.DeliverContentAsync(
                manifest,
                workingDirectory,
                progress,
                cancellationToken).ConfigureAwait(false);

            if (!deliveryResult.Success)
            {
                return OperationResult<ContentManifest>.CreateFailure(
                    $"Content delivery failed: {deliveryResult.FirstError}");
            }

            var extractedManifests = await manifestFactory.CreateManifestsFromExtractedContentAsync(
                manifest,
                workingDirectory,
                progress,
                cancellationToken).ConfigureAwait(false);

            var resultManifest = extractedManifests.Count > 0 ? extractedManifests[0] : (deliveryResult.Data ?? manifest);

            Logger.LogInformation(
                "Successfully prepared CNC Labs content {ManifestId} with {FileCount} files",
                resultManifest.Id,
                resultManifest.Files.Count);

            return OperationResult<ContentManifest>.CreateSuccess(resultManifest);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to prepare CNC Labs content {ManifestId}", manifest.Id);
            return OperationResult<ContentManifest>.CreateFailure(
                $"Content preparation failed: {ex.Message}");
        }
    }
}
