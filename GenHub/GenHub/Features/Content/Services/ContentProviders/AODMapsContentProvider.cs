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
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Content.Services.ContentProviders;

/// <summary>
/// AODMaps content provider that orchestrates discovery→resolution→delivery pipeline
/// for AODMaps-hosted content.
/// </summary>
[SuppressMessage("Minor Code Smell", "S101:Types should be named in PascalCase", Justification = "Domain acronym")]
public class AODMapsContentProvider(
    IEnumerable<IContentDiscoverer> discoverers,
    IEnumerable<IContentResolver> resolvers,
    IEnumerable<IContentDeliverer> deliverers,
    AODMapsManifestFactory manifestFactory,
    ILogger<AODMapsContentProvider> logger,
    IContentValidator contentValidator,
    IInstallationInstructionsService installationInstructionsService)
    : BaseContentProvider(contentValidator, installationInstructionsService, logger)
{
    private readonly IContentDiscoverer _aodMapsDiscoverer = ResolveDiscoverer(discoverers, AODMapsConstants.DiscovererSourceName);

    private readonly IContentResolver _aodMapsResolver = ResolveResolver(resolvers, AODMapsConstants.ResolverId);

    private readonly IContentDeliverer _httpDeliverer = ResolveDeliverer(deliverers, ContentSourceNames.HttpDeliverer);

    /// <inheritdoc />
    /// <remarks>
    /// Must match the ProviderName set by AODMapsDiscoverer on search results.
    /// </remarks>
    public override string SourceName => AODMapsConstants.DiscovererSourceName;

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
        return await SearchManifestByIdAsync(contentId, requireExactIdMatch: false, cancellationToken);
    }

    /// <inheritdoc />
    protected override Task<OperationResult<ContentManifest>> PrepareContentInternalAsync(
        ContentManifest manifest,
        string workingDirectory,
        IProgress<ContentAcquisitionProgress>? progress,
        CancellationToken cancellationToken)
    {
        Logger.LogInformation("Preparing AODMaps content: {ManifestId} ({Name})", manifest.Id, manifest.Name);

        return DeliverAndEnrichContentAsync(
            _httpDeliverer,
            manifestFactory,
            manifest,
            workingDirectory,
            progress,
            cancellationToken);
    }
}
