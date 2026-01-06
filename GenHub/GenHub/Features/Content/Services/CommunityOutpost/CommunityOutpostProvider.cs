using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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

namespace GenHub.Features.Content.Services.CommunityOutpost;

/// <summary>
/// Content provider for Community Outpost community patches.
/// </summary>
/// <param name="providerDefinitionLoader">The provider definition loader for data-driven configuration.</param>
/// <param name="discoverers">Available content discoverers.</param>
/// <param name="resolvers">Available content resolvers.</param>
/// <param name="deliverers">Available content deliverers.</param>
/// <param name="contentValidator">The content validator.</param>
/// <param name="logger">The logger.</param>
public class CommunityOutpostProvider(
    IProviderDefinitionLoader providerDefinitionLoader,
    IEnumerable<IContentDiscoverer> discoverers,
    IEnumerable<IContentResolver> resolvers,
    IEnumerable<IContentDeliverer> deliverers,
    IContentValidator contentValidator,
    ILogger<CommunityOutpostProvider> logger)
    : BaseContentProvider(contentValidator, logger)
{
    private readonly IProviderDefinitionLoader _providerDefinitionLoader = providerDefinitionLoader;

    private readonly IContentDiscoverer _discoverer = discoverers.FirstOrDefault(d =>
            d.SourceName.Contains(CommunityOutpostConstants.PublisherType, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("No Community Outpost discoverer found");

    private readonly IContentResolver _resolver = resolvers.FirstOrDefault(r =>
            r.ResolverId == CommunityOutpostConstants.PublisherId)
            ?? throw new InvalidOperationException(
                $"No Community Outpost resolver found with ResolverId '{CommunityOutpostConstants.PublisherId}'");

    // Use CommunityOutpostDeliverer for specialized ZIP extraction and manifest factory invocation
    private readonly IContentDeliverer _deliverer = deliverers.FirstOrDefault(d =>
            d.SourceName?.Equals(CommunityOutpostConstants.PublisherId, StringComparison.OrdinalIgnoreCase) == true)
            ?? throw new InvalidOperationException("No Community Outpost deliverer found");

    private ProviderDefinition? _cachedProviderDefinition;

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

        var searchResult = new ContentSearchResult
        {
            Id = contentId,
            Name = CommunityOutpostConstants.ContentName,
            Version = contentId,
            ProviderName = SourceName,
            RequiresResolution = true,
            ResolverId = CommunityOutpostConstants.PublisherId,
        };

        var manifestResult = await Resolver.ResolveAsync(searchResult, cancellationToken);
        if (!manifestResult.Success || manifestResult.Data == null)
        {
            return OperationResult<ContentManifest>.CreateFailure(
                $"Failed to resolve manifest: {manifestResult.FirstError}");
        }

        var validationResult = await ContentValidator.ValidateManifestAsync(
            manifestResult.Data, cancellationToken);

        if (!validationResult.IsValid)
        {
            var errors = validationResult.Issues.Select(i => $"Validation failed: {i.Message}");
            return OperationResult<ContentManifest>.CreateFailure(errors);
        }

        return manifestResult;
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
        // Use cached definition if available
        if (_cachedProviderDefinition != null)
        {
            return _cachedProviderDefinition;
        }

        // Try to get from the loader (it should already be loaded at startup)
        _cachedProviderDefinition = _providerDefinitionLoader.GetProvider(CommunityOutpostConstants.PublisherId);

        if (_cachedProviderDefinition == null)
        {
            Logger.LogDebug(
                "No provider definition found for {ProviderId}, using hardcoded constants",
                CommunityOutpostConstants.PublisherId);
        }
        else
        {
            Logger.LogInformation(
                "Using provider definition for {ProviderId} from JSON configuration",
                CommunityOutpostConstants.PublisherId);
        }

        return _cachedProviderDefinition;
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
            if (!Deliverer.CanDeliver(manifest))
            {
                return OperationResult<ContentManifest>.CreateFailure(
                    $"Cannot deliver content for manifest {manifest.Id}");
            }

            var deliveryResult = await Deliverer.DeliverContentAsync(
                manifest,
                workingDirectory,
                progress,
                cancellationToken);

            if (!deliveryResult.Success)
            {
                return OperationResult<ContentManifest>.CreateFailure(
                    $"Content delivery failed: {deliveryResult.FirstError}");
            }

            var resultManifest = deliveryResult.Data ?? manifest;
            Logger.LogInformation(
                "Successfully prepared Community Outpost content {ManifestId}",
                manifest.Id);
            return OperationResult<ContentManifest>.CreateSuccess(resultManifest);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to prepare Community Outpost content");
            return OperationResult<ContentManifest>.CreateFailure(
                $"Content preparation failed: {ex.Message}");
        }
    }
}
