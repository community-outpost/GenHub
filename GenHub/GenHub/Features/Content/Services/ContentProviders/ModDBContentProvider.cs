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
/// ModDB content provider that orchestrates discovery→resolution→delivery pipeline
/// for ModDB-hosted content.
/// </summary>
public class ModDBContentProvider(
    IEnumerable<IContentDiscoverer> discoverers,
    IEnumerable<IContentResolver> resolvers,
    IEnumerable<IContentDeliverer> deliverers,
    ModDBManifestFactory manifestFactory,
    ILogger<ModDBContentProvider> logger,
    IContentValidator contentValidator,
    IInstallationInstructionsService installationInstructionsService)
    : BaseContentProvider(contentValidator, installationInstructionsService, logger)
{
    private readonly IContentDiscoverer _moddbDiscoverer = discoverers.FirstOrDefault(d => d.SourceName?.Equals(ContentSourceNames.ModDBDiscoverer, StringComparison.OrdinalIgnoreCase) == true)
        ?? throw new ArgumentException("ModDB discoverer not found", nameof(discoverers));

    private readonly IContentResolver _moddbResolver = resolvers.FirstOrDefault(r => r.ResolverId?.Equals(ContentSourceNames.ModDBResolverId, StringComparison.OrdinalIgnoreCase) == true)
        ?? throw new ArgumentException("ModDB resolver not found", nameof(resolvers));

    private readonly IContentDeliverer _httpDeliverer = deliverers.FirstOrDefault(d => d.SourceName?.Equals(ContentSourceNames.HttpDeliverer, StringComparison.OrdinalIgnoreCase) == true)
        ?? throw new ArgumentException("HTTP deliverer not found", nameof(deliverers));

    private readonly ModDBManifestFactory _manifestFactory = manifestFactory ?? throw new ArgumentNullException(nameof(manifestFactory));

    /// <inheritdoc />
    public override string SourceName => "ModDB";

    /// <inheritdoc />
    public override string Description => "Provides content from ModDB";

    /// <inheritdoc />
    protected override IContentDiscoverer Discoverer => _moddbDiscoverer;

    /// <inheritdoc />
    protected override IContentResolver Resolver => _moddbResolver;

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
        try
        {
            Logger.LogDebug("Preparing ModDB content for manifest {ManifestId}", manifest.Id);

            var allFilesInCas = manifest.Files.Count > 0 && manifest.Files.All(f =>
                f.SourceType == ContentSourceType.ContentAddressable &&
                !string.IsNullOrEmpty(f.Hash));

            if (allFilesInCas)
            {
                Logger.LogInformation(
                    "All {Count} file(s) of {ManifestId} are already stored in CAS; skipping delivery",
                    manifest.Files.Count,
                    manifest.Id);
                return OperationResult<ContentManifest>.CreateSuccess(manifest);
            }

            if (!Deliverer.CanDeliver(manifest))
            {
                return OperationResult<ContentManifest>.CreateFailure($"Cannot deliver content for manifest {manifest.Id}");
            }

            var deliveryResult = await Deliverer.DeliverContentAsync(manifest, workingDirectory, progress, cancellationToken);
            if (!deliveryResult.Success)
            {
                return OperationResult<ContentManifest>.CreateFailure($"ModDB content delivery failed: {deliveryResult.FirstError}");
            }

            var deliveredManifest = deliveryResult.Data ?? manifest;
            Logger.LogInformation("Successfully delivered ModDB content {ManifestId} to {WorkingDirectory}", deliveredManifest.Id, workingDirectory);
            return OperationResult<ContentManifest>.CreateSuccess(deliveredManifest);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to prepare ModDB content for manifest {ManifestId}", manifest.Id);
            return OperationResult<ContentManifest>.CreateFailure($"ModDB content preparation failed: {ex.Message}");
        }
    }
}