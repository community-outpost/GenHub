using GenHub.Core.Interfaces.Content;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Content.Services.ContentProviders;

/// <summary>
/// ModDB content provider that orchestrates discovery→resolution→delivery pipeline
/// for ModDB-hosted content.
/// </summary>
public class ModDBContentProvider : BaseContentProvider
{
    private readonly IContentDiscoverer _moddbDiscoverer;
    private readonly IContentResolver _moddbResolver;
    private readonly IContentDeliverer _httpDeliverer;

    /// <summary>
    /// Initializes a new instance of the <see cref="ModDBContentProvider"/> class.
    /// </summary>
    /// <param name="discoverers">Available content discoverers.</param>
    /// <param name="resolvers">Available content resolvers.</param>
    /// <param name="deliverers">Available content deliverers.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="contentValidator">The content validator.</param>
    public ModDBContentProvider(
        IEnumerable<IContentDiscoverer> discoverers,
        IEnumerable<IContentResolver> resolvers,
        IEnumerable<IContentDeliverer> deliverers,
        ILogger<ModDBContentProvider> logger,
        IContentValidator contentValidator)
        : base(contentValidator, logger)
    {
        _moddbDiscoverer = discoverers?.FirstOrDefault(d =>
            string.Equals(d.SourceName, "ModDB", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("ModDB discoverer not found");

        _moddbResolver = resolvers?.FirstOrDefault(r =>
            string.Equals(r.ResolverId, "ModDB", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("ModDB resolver not found");

        _httpDeliverer = deliverers?.FirstOrDefault(d =>
            string.Equals(d.SourceName, "HTTP", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("HTTP deliverer not found");
    }

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
    public override async Task<ContentOperationResult<ContentManifest>> GetValidatedContentAsync(
        string contentId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(contentId))
        {
            return ContentOperationResult<ContentManifest>.CreateFailure("Content ID cannot be null or empty");
        }

        var query = new ContentSearchQuery { SearchTerm = contentId, Take = 1 };
        var searchResult = await SearchAsync(query, cancellationToken);

        if (!searchResult.Success || !searchResult.Data!.Any())
        {
            return ContentOperationResult<ContentManifest>.CreateFailure(
                $"Content not found for ID '{contentId}': {searchResult.ErrorMessage ?? "No matching results"}");
        }

        var result = searchResult.Data!.First();
        var manifest = result.GetData<ContentManifest>();

        return manifest != null
            ? ContentOperationResult<ContentManifest>.CreateSuccess(manifest)
            : ContentOperationResult<ContentManifest>.CreateFailure($"Invalid manifest data for content ID '{contentId}'");
    }

    /// <inheritdoc />
    protected override Task<ContentOperationResult<ContentManifest>> PrepareContentInternalAsync(
        ContentManifest manifest,
        string workingDirectory,
        IProgress<ContentAcquisitionProgress>? progress,
        CancellationToken cancellationToken)
    {
        // Implementation-specific content preparation for ModDB
        Logger.LogDebug("Preparing ModDB content for manifest {ManifestId}", manifest.Id);

        // For now, return the manifest as-is since ModDB content preparation
        // would be implemented based on ModDB's specific requirements
        return Task.FromResult(ContentOperationResult<ContentManifest>.CreateSuccess(manifest));
    }
}
