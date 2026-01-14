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
/// AODMaps content provider that orchestrates discovery→resolution→delivery pipeline
/// for AODMaps-hosted content.
/// </summary>
public class AODMapsContentProvider(
    IEnumerable<IContentDiscoverer> discoverers,
    IEnumerable<IContentResolver> resolvers,
    IEnumerable<IContentDeliverer> deliverers,
    ILogger<AODMapsContentProvider> logger,
    IContentValidator contentValidator) : BaseContentProvider(contentValidator, logger)
{
    private readonly IContentDiscoverer _aodMapsDiscoverer = discoverers.FirstOrDefault(d =>
        string.Equals(d.SourceName, AODMapsConstants.DiscovererSourceName, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException("AODMaps discoverer not found");

    private readonly IContentResolver _aodMapsResolver = resolvers.FirstOrDefault(r =>
        string.Equals(r.ResolverId, AODMapsConstants.ResolverId, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException("AODMaps resolver not found");

    private readonly IContentDeliverer _httpDeliverer = deliverers.FirstOrDefault(d =>
        string.Equals(d.SourceName, ContentSourceNames.HttpDeliverer, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException("HTTP deliverer not found");

    /// <inheritdoc />
    public override string SourceName => AODMapsConstants.PublisherType;

    /// <inheritdoc />
    public override string Description => "Provides content from AODMaps";

    /// <inheritdoc />
    protected override IContentDiscoverer Discoverer => _aodMapsDiscoverer;

    /// <inheritdoc />
    protected override IContentResolver Resolver => _aodMapsResolver;

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

        if (!searchResult.Success || searchResult.Data == null || !searchResult.Data.Any())
        {
            return OperationResult<ContentManifest>.CreateFailure(
                $"Content not found for ID '{contentId}': {searchResult.FirstError ?? "No matching results"}");
        }

        var result = searchResult.Data.First();
        var manifest = result.GetData<ContentManifest>();

        return manifest != null
            ? OperationResult<ContentManifest>.CreateSuccess(manifest)
            : OperationResult<ContentManifest>.CreateFailure($"Invalid manifest data for content ID '{contentId}'");
    }

    /// <inheritdoc />
    protected override Task<OperationResult<ContentManifest>> PrepareContentInternalAsync(
        ContentManifest manifest,
        string workingDirectory,
        IProgress<ContentAcquisitionProgress>? progress,
        CancellationToken cancellationToken)
    {
        Logger.LogDebug("Preparing AODMaps content for manifest {ManifestId}", manifest.Id);
        return Task.FromResult(OperationResult<ContentManifest>.CreateSuccess(manifest));
    }
}
