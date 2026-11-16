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
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Content.Services.Publishers;

/// <summary>
/// Content provider for TheSuperHackers publisher.
/// Discovers and delivers game client releases from TheSuperHackers GitHub repositories.
/// </summary>
public class SuperHackersProvider(
    IProviderDefinitionLoader providerDefinitionLoader,
    IEnumerable<IContentDiscoverer> discoverers,
    IEnumerable<IContentResolver> resolvers,
    IEnumerable<IContentDeliverer> deliverers,
    IContentValidator contentValidator,
    ILogger<SuperHackersProvider> logger,
    IInstallationInstructionsService installationInstructionsService)
    : BaseContentProvider(contentValidator, installationInstructionsService, logger)
{
    private readonly IContentDiscoverer _discoverer = ResolveDiscoverer(discoverers, PublisherTypeConstants.TheSuperHackers);

    private readonly IContentResolver _resolver = ResolveResolver(resolvers, SuperHackersConstants.ResolverId);

    private readonly IContentDeliverer _deliverer = ResolveDeliverer(deliverers, ContentSourceNames.GitHubDeliverer);

    /// <inheritdoc/>
    public override string SourceName => PublisherTypeConstants.TheSuperHackers;

    /// <inheritdoc/>
    public override string Description => SuperHackersConstants.ProviderDescription;

    /// <inheritdoc/>
    public override bool IsEnabled => true;

    /// <inheritdoc/>
    public override ContentSourceCapabilities Capabilities =>
        ContentSourceCapabilities.RequiresDiscovery |
        ContentSourceCapabilities.SupportsPackageAcquisition;

    /// <inheritdoc/>
    protected override IContentDiscoverer Discoverer => _discoverer;

    /// <inheritdoc/>
    protected override IContentResolver Resolver => _resolver;

    /// <inheritdoc/>
    protected override IContentDeliverer Deliverer => _deliverer;

    /// <inheritdoc/>
    public override async Task<OperationResult<ContentManifest>> GetValidatedContentAsync(
        string contentId,
        CancellationToken cancellationToken = default)
    {
        Logger.LogInformation("Getting SuperHackers manifest for: {ContentId}", contentId);

        // Create a search result for resolution
        var searchResult = CreateResolutionRequest(
            contentId,
            SuperHackersConstants.PublisherName,
            contentId,
            SuperHackersConstants.ResolverId);

        return await ResolveAndValidateAsync(searchResult, cancellationToken);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Returns the TheSuperHackers provider definition loaded from JSON configuration.
    /// The definition contains GitHub repository info, endpoints, and other configuration.
    /// </remarks>
    protected override ProviderDefinition? GetProviderDefinition()
    {
        return GetCachedProviderDefinition(providerDefinitionLoader, SuperHackersConstants.PublisherId);
    }

    /// <inheritdoc/>
    protected override async Task<OperationResult<ContentManifest>> PrepareContentInternalAsync(
        ContentManifest manifest,
        string workingDirectory,
        IProgress<ContentAcquisitionProgress>? progress,
        CancellationToken cancellationToken)
    {
        Logger.LogInformation("Preparing SuperHackers content: {Version}", manifest.Version);

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
            Logger.LogError(ex, "Failed to prepare SuperHackers content");
            return OperationResult<ContentManifest>.CreateFailure(
                $"Content preparation failed: {ex.Message}");
        }
    }
}
