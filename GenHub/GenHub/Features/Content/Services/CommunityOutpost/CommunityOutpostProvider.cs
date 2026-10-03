using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.Content.Services.ContentProviders;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Content.Services.CommunityOutpost;

/// <summary>
/// Content provider for Community Outpost community patches.
/// </summary>
/// <param name="providerDefinitionLoader">The provider definition loader for data-driven configuration.</param>
/// <param name="discoverers">Available content discoverers.</param>
/// <param name="resolvers">Available content resolvers.</param>
/// <param name="deliverers">Available content deliverers.</param>
/// <param name="contentValidator">The content validator.</param>
/// <param name="installationInstructionsService">The installation instructions service.</param>
/// <param name="logger">The logger.</param>
public class CommunityOutpostProvider(
    IProviderDefinitionLoader providerDefinitionLoader,
    IEnumerable<IContentDiscoverer> discoverers,
    IEnumerable<IContentResolver> resolvers,
    IEnumerable<IContentDeliverer> deliverers,
    IContentValidator contentValidator,
    IInstallationInstructionsService installationInstructionsService,
    ILogger<CommunityOutpostProvider> logger)
    : BaseContentProvider(contentValidator, installationInstructionsService, logger)
{
    private readonly IContentDiscoverer _discoverer = ResolveDiscoverer(discoverers, CommunityOutpostConstants.PublisherType);

    private readonly IContentResolver _resolver = ResolveResolver(resolvers, CommunityOutpostConstants.PublisherId);

    // Use CommunityOutpostDeliverer for specialized ZIP extraction and manifest factory invocation
    private readonly IContentDeliverer _deliverer = ResolveDeliverer(deliverers, CommunityOutpostConstants.PublisherId);

    /// <inheritdoc/>
    public override string SourceName => CommunityOutpostConstants.PublisherType;

    /// <inheritdoc/>
    public override string Description => CommunityOutpostConstants.ProviderDescription;

    /// <inheritdoc/>
    public override bool IsEnabled => true;

    /// <inheritdoc/>
    public override ContentSourceCapabilities Capabilities =>
        ContentSourceCapabilities.RequiresDiscovery |
        ContentSourceCapabilities.SupportsPackageAcquisition;

    /// <inheritdoc/>
    public override async Task<OperationResult<ContentManifest>> GetValidatedContentAsync(
        string contentId,
        CancellationToken cancellationToken = default)
    {
        Logger.LogInformation("Getting Community Outpost manifest for: {ContentId}", contentId);

        var searchResult = CreateResolutionRequest(
            contentId,
            CommunityOutpostConstants.ContentName,
            contentId,
            CommunityOutpostConstants.PublisherId);

        return await ResolveAndValidateAsync(searchResult, cancellationToken);
    }

    /// <inheritdoc/>
    protected override IContentDiscoverer Discoverer => _discoverer;

    /// <inheritdoc/>
    protected override IContentResolver Resolver => _resolver;

    /// <inheritdoc/>
    protected override IContentDeliverer Deliverer => _deliverer;

    /// <inheritdoc/>
    /// <remarks>
    /// Returns the CommunityOutpost provider definition loaded from JSON configuration.
    /// The definition contains endpoint URLs, timeouts, and other configuration that can be
    /// modified without recompiling the application.
    /// </remarks>
    protected override ProviderDefinition? GetProviderDefinition()
    {
        return GetCachedProviderDefinition(providerDefinitionLoader, CommunityOutpostConstants.PublisherId) ?? GetCachedProviderDefinition(providerDefinitionLoader, CommunityOutpostConstants.PublisherType);
    }

    /// <inheritdoc/>
    protected override async Task<OperationResult<ContentManifest>> PrepareContentInternalAsync(
        ContentManifest manifest,
        string workingDirectory,
        IProgress<ContentAcquisitionProgress>? progress,
        CancellationToken cancellationToken)
    {
        Logger.LogInformation("Preparing Community Outpost content: {Version}", manifest.Version);

        try
        {
            return await DeliverContentOnlyAsync(
                Deliverer,
                manifest,
                workingDirectory,
                progress,
                cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to prepare Community Outpost content");
            return OperationResult<ContentManifest>.CreateFailure(
                $"Content preparation failed: {ex.Message}");
        }
    }
}
