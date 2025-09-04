using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Content.Services.ContentProviders;

/// <summary>
/// Local file system provider that uses FileSystemDiscoverer for content discovery.
/// This eliminates duplication with ManifestDiscoveryService.
/// </summary>
public class LocalFileSystemContentProvider : BaseContentProvider
{
    private readonly IContentDiscoverer _fileSystemDiscoverer;
    private readonly IContentResolver _localResolver;
    private readonly IContentDeliverer _fileSystemDeliverer;
    private readonly IConfigurationProviderService _configurationProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="LocalFileSystemContentProvider"/> class.
    /// </summary>
    /// <param name="discoverers">Available content discoverers.</param>
    /// <param name="resolvers">Available content resolvers.</param>
    /// <param name="deliverers">Available content deliverers.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="contentValidator">The content validator.</param>
    /// <param name="configurationProvider">The configuration provider.</param>
    public LocalFileSystemContentProvider(
        IEnumerable<IContentDiscoverer> discoverers,
        IEnumerable<IContentResolver> resolvers,
        IEnumerable<IContentDeliverer> deliverers,
        ILogger<LocalFileSystemContentProvider> logger,
        IContentValidator contentValidator,
        IConfigurationProviderService configurationProvider)
        : base(contentValidator, logger)
    {
        _fileSystemDiscoverer = discoverers.FirstOrDefault(d => d.SourceName.Contains("FileSystem"))
            ?? throw new InvalidOperationException("No FileSystem discoverer found");
        _localResolver = resolvers.FirstOrDefault(r => r.ResolverId.Contains("Local"))
            ?? throw new InvalidOperationException("No Local resolver found");
        _fileSystemDeliverer = deliverers.FirstOrDefault(d => d.SourceName.Contains("FileSystem"))
            ?? throw new InvalidOperationException("No FileSystem deliverer found");
        _configurationProvider = configurationProvider;
    }

    /// <inheritdoc />
    public override string SourceName => "LocalFileSystem";

    /// <inheritdoc />
    public override string Description => "Local file system content provider";

    /// <inheritdoc />
    public override bool IsEnabled => true;

    /// <inheritdoc />
    public override ContentSourceCapabilities Capabilities =>
        ContentSourceCapabilities.LocalFileDelivery |
        ContentSourceCapabilities.SupportsPackageAcquisition | ContentSourceCapabilities.SupportsManifestGeneration;

    /// <inheritdoc />
    protected override IContentDiscoverer Discoverer => _fileSystemDiscoverer;

    /// <inheritdoc />
    protected override IContentResolver Resolver => _localResolver;

    /// <inheritdoc />
    protected override IContentDeliverer Deliverer => _fileSystemDeliverer;

    /// <inheritdoc />
    public override async Task<ContentOperationResult<ContentManifest>> GetValidatedContentAsync(
        string contentId, CancellationToken cancellationToken = default)
    {
        var query = new ContentSearchQuery { SearchTerm = contentId, Take = 1 };
        var searchResult = await SearchAsync(query, cancellationToken);

        if (!searchResult.Success || !searchResult.Data!.Any())
        {
            return ContentOperationResult<ContentManifest>.CreateFailure($"Content not found: {contentId}");
        }

        var result = searchResult.Data!.First();
        var manifest = result.GetData<ContentManifest>();

        return manifest != null
            ? ContentOperationResult<ContentManifest>.CreateSuccess(manifest)
            : ContentOperationResult<ContentManifest>.CreateFailure("Manifest not available in search result");
    }

    /// <inheritdoc />
    protected override async Task<ContentOperationResult<ContentManifest>> PrepareContentInternalAsync(
        ContentManifest manifest,
        string workingDirectory,
        IProgress<ContentAcquisitionProgress>? progress,
        CancellationToken cancellationToken)
    {
        // Implementation-specific content preparation for local file system
        Logger.LogDebug("Preparing local file system content for manifest {ManifestId}", manifest.Id);

        // For local file system, content is already available locally
        // Just return the manifest as-is
        return await Task.FromResult(ContentOperationResult<ContentManifest>.CreateSuccess(manifest));
    }
}
