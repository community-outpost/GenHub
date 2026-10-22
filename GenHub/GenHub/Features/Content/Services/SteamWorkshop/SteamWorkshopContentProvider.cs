using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.Content.Services.ContentProviders;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Content.Services.SteamWorkshop;

/// <summary>
/// Steam Workshop content provider that orchestrates discovery→resolution→delivery
/// pipeline for Generals and Zero Hour workshop maps.
/// </summary>
public class SteamWorkshopContentProvider(
    IEnumerable<IContentDiscoverer> discoverers,
    IEnumerable<IContentResolver> resolvers,
    IEnumerable<IContentDeliverer> deliverers,
    SteamWorkshopManifestFactory manifestFactory,
    ILogger<SteamWorkshopContentProvider> logger,
    IContentValidator contentValidator,
    IInstallationInstructionsService installationInstructionsService)
    : BaseContentProvider(contentValidator, installationInstructionsService, logger)
{
    private readonly IContentDiscoverer _steamWorkshopDiscoverer = ResolveDiscoverer(discoverers, ContentSourceNames.SteamWorkshopDiscoverer);

    private readonly IContentResolver _steamWorkshopResolver = ResolveResolver(resolvers, ContentSourceNames.SteamWorkshopResolverId);

    private readonly IContentDeliverer _steamWorkshopDeliverer = ResolveDeliverer(deliverers, ContentSourceNames.SteamWorkshopDeliverer);

    /// <inheritdoc />
    public override string SourceName => SteamWorkshopConstants.DiscovererSourceName;

    /// <inheritdoc />
    public override string Description => SteamWorkshopConstants.DiscovererDescription;

    /// <inheritdoc />
    protected override IContentDiscoverer Discoverer => _steamWorkshopDiscoverer;

    /// <inheritdoc />
    protected override IContentResolver Resolver => _steamWorkshopResolver;

    /// <inheritdoc />
    protected override IContentDeliverer Deliverer => _steamWorkshopDeliverer;

    /// <inheritdoc />
    public override Task<OperationResult<ContentManifest>> GetValidatedContentAsync(
        string contentId, CancellationToken cancellationToken = default)
    {
        if (!SteamWorkshopHelper.TryExtractPublishedFileIdFromContentId(contentId, out var publishedFileId))
        {
            return Task.FromResult(OperationResult<ContentManifest>.CreateFailure(
                $"Content ID '{contentId}' is not a Steam Workshop item."));
        }

        var request = new ContentSearchResult
        {
            Id = contentId,
            Name = contentId,
            ProviderName = SourceName,
            RequiresResolution = true,
            ResolverId = SteamWorkshopConstants.ResolverId,
            SourceUrl = SteamWorkshopHelper.BuildFileDetailsUrl(publishedFileId),
            ResolverMetadata =
            {
                [SteamWorkshopConstants.PublishedFileIdMetadataKey] = publishedFileId,
            },
        };

        return ResolveAndValidateAsync(request, cancellationToken);
    }

    /// <inheritdoc />
    protected override Task<OperationResult<ContentManifest>> PrepareContentInternalAsync(
        ContentManifest manifest,
        string workingDirectory,
        IProgress<ContentAcquisitionProgress>? progress,
        CancellationToken cancellationToken)
    {
        Logger.LogInformation("Preparing Steam Workshop content: {ManifestId} ({Name})", manifest.Id, manifest.Name);

        return DeliverAndEnrichContentAsync(
            _steamWorkshopDeliverer,
            manifestFactory,
            manifest,
            workingDirectory,
            progress,
            cancellationToken);
    }
}
