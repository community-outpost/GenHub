using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Features.Content.Services.Publishers;
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
    private readonly IContentDiscoverer _moddbDiscoverer = ResolveDiscoverer(discoverers, ContentSourceNames.ModDBDiscoverer);

    private readonly IContentResolver _moddbResolver = ResolveResolver(resolvers, ContentSourceNames.ModDBResolverId);

    private readonly IContentDeliverer _httpDeliverer = ResolveDeliverer(deliverers, ContentSourceNames.HttpDeliverer);

    /// <inheritdoc />
    public override string SourceName => ModDBConstants.DiscovererSourceName;

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
        return await SearchManifestByIdAsync(contentId, requireExactIdMatch: false, cancellationToken);
    }

    /// <inheritdoc />
    protected override Task<OperationResult<ContentManifest>> PrepareContentInternalAsync(
        ContentManifest manifest,
        string workingDirectory,
        IProgress<ContentAcquisitionProgress>? progress,
        CancellationToken cancellationToken)
    {
        Logger.LogInformation("Preparing ModDB content: {ManifestId} ({Name})", manifest.Id, manifest.Name);

        return DeliverAndEnrichContentAsync(
            _httpDeliverer,
            manifestFactory,
            manifest,
            workingDirectory,
            progress,
            cancellationToken);
    }
}
