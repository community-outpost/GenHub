using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Results;
using GenHub.Features.Content.Services.ContentProviders;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Content.Services.GeneralsOnline;

/// <summary>
/// Content provider for Generals Online multiplayer service.
/// Orchestrates discovery, resolution, and delivery through the content pipeline.
/// </summary>
public class GeneralsOnlineProvider(
    IProviderDefinitionLoader providerDefinitionLoader,
    IEnumerable<IContentDiscoverer> discoverers,
    IEnumerable<IContentResolver> resolvers,
    IEnumerable<IContentDeliverer> deliverers,
    IContentValidator contentValidator,
    IContentManifestPool manifestPool,
    ILogger<GeneralsOnlineProvider> logger)
    : BaseContentProvider(contentValidator, logger)
{
    private readonly IProviderDefinitionLoader _providerDefinitionLoader = providerDefinitionLoader;
    private ProviderDefinition? _cachedProviderDefinition;

    /// <inheritdoc />
    public override string SourceName => GeneralsOnlineConstants.PublisherType;

    /// <inheritdoc />
    public override string Description => GeneralsOnlineConstants.Description;

    /// <inheritdoc />
    public override bool IsEnabled => true;

    /// <inheritdoc />
    /// <remarks>
    /// Capabilities:
    /// - RequiresDiscovery: Provider queries CDN API to find available releases
    /// - SupportsPackageAcquisition: Provider can download and install content packages.
    /// </remarks>
    public override ContentSourceCapabilities Capabilities =>
        ContentSourceCapabilities.RequiresDiscovery |
        ContentSourceCapabilities.SupportsPackageAcquisition;

    /// <inheritdoc />
    public override async Task<OperationResult<ContentManifest>> GetValidatedContentAsync(
    string contentId,
    CancellationToken cancellationToken = default)
    {
        Logger.LogInformation("Getting Generals Online manifest for: {ContentId}", contentId);

        try
        {
            // ContentId format from discoverer: "GeneralsOnline_{Version}" (e.g., "GeneralsOnline_101525_QFE5")
            // Manifest IDs in pool: "1.1015255.generalsonline.gameclient.30hz" or "1.1015255.generalsonline.gameclient.60hz"
            if (!contentId.StartsWith("GeneralsOnline_", StringComparison.OrdinalIgnoreCase))
            {
                return OperationResult<ContentManifest>.CreateFailure(
                    $"Invalid contentId format: '{contentId}'. Expected format: 'GeneralsOnline_{{version}}'");
            }

            var version = contentId.Substring("GeneralsOnline_".Length);

            if (string.IsNullOrWhiteSpace(version))
            {
                return OperationResult<ContentManifest>.CreateFailure(
                    $"Invalid version in contentId: '{contentId}'");
            }

            // Check if already installed - search all manifests and filter in-memory
            var allManifestsResult = await manifestPool.GetAllManifestsAsync(cancellationToken);

            if (allManifestsResult.Success && allManifestsResult.Data != null)
            {
                // Find any GeneralsOnline manifest with matching version (30hz or 60hz)
                var existing = allManifestsResult.Data.FirstOrDefault(m =>
                    string.Equals(m.Version, version, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(m.Publisher?.PublisherType, GeneralsOnlineConstants.PublisherType, StringComparison.OrdinalIgnoreCase));

                if (existing != null)
                {
                    Logger.LogInformation(
                        "Found existing manifest for version {Version}: {ManifestId}",
                        version,
                        existing.Id);
                    return OperationResult<ContentManifest>.CreateSuccess(existing);
                }
            }

            // Not found - resolve new manifest
            var searchResultObj = new ContentSearchResult
            {
                Id = contentId,
                Name = GeneralsOnlineConstants.ContentName,
                Version = version, // Use parsed version, not full contentId
                ProviderName = SourceName,
                RequiresResolution = true,
                ResolverId = GeneralsOnlineConstants.ResolverId,
            };

            var manifestResult = await Resolver.ResolveAsync(searchResultObj, cancellationToken);
            if (!manifestResult.Success || manifestResult.Data == null)
            {
                return OperationResult<ContentManifest>.CreateFailure(
                    $"Failed to resolve manifest: {manifestResult.FirstError}");
            }

            var validationResult = await ContentValidator.ValidateManifestAsync(
                manifestResult.Data,
                cancellationToken);

            if (!validationResult.IsValid)
            {
                var errors = validationResult.Issues.Select(i => $"Validation failed: {i.Message}");
                return OperationResult<ContentManifest>.CreateFailure(errors);
            }

            return manifestResult;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to get validated content for {ContentId}", contentId);
            return OperationResult<ContentManifest>.CreateFailure(
                $"Content validation failed: {ex.Message}");
        }
    }

    /// <inheritdoc />
    protected override IContentDiscoverer Discoverer =>
        discoverers.First(d =>
            d.SourceName.Equals(
                GeneralsOnlineConstants.DiscovererSourceName,
                StringComparison.OrdinalIgnoreCase));

    /// <inheritdoc />
    protected override IContentResolver Resolver =>
        resolvers.First(r =>
            r.ResolverId.Equals(
                GeneralsOnlineConstants.ResolverId,
                StringComparison.OrdinalIgnoreCase));

    /// <inheritdoc />
    protected override IContentDeliverer Deliverer =>
        deliverers.First(d =>
            d.SourceName.Equals(
                GeneralsOnlineConstants.DelivererSourceName,
                StringComparison.OrdinalIgnoreCase));

    /// <inheritdoc/>
    /// <remarks>
    /// Returns the GeneralsOnline provider definition loaded from JSON configuration.
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
        _cachedProviderDefinition = _providerDefinitionLoader.GetProvider(GeneralsOnlineConstants.PublisherType);

        if (_cachedProviderDefinition == null)
        {
            Logger.LogWarning(
                "No provider definition found for {ProviderId}, using hardcoded constants",
                GeneralsOnlineConstants.PublisherType);
        }
        else
        {
            Logger.LogInformation(
                "Using provider definition for {ProviderId} from JSON configuration",
                GeneralsOnlineConstants.PublisherType);
        }

        return _cachedProviderDefinition;
    }

    /// <inheritdoc />
    /// <remarks>
    /// This is the internal implementation called by the base class's public PrepareContentAsync method.
    /// The base class handles common validation and error handling, then delegates to this method.
    /// </remarks>
    protected override async Task<OperationResult<ContentManifest>> PrepareContentInternalAsync(
        ContentManifest manifest,
        string workingDirectory,
        IProgress<ContentAcquisitionProgress>? progress,
        CancellationToken cancellationToken)
    {
        Logger.LogInformation("Preparing Generals Online content: {Version}", manifest.Version);

        try
        {
            // Use the deliverer to handle content acquisition
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

            // Ensure we have valid data before validation
            var resultManifest = deliveryResult.Data ?? manifest;

            Logger.LogInformation("Successfully prepared Generals Online content {ManifestId}", manifest.Id);
            return OperationResult<ContentManifest>.CreateSuccess(resultManifest);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to prepare Generals Online content");
            return OperationResult<ContentManifest>.CreateFailure(
                $"Content preparation failed: {ex.Message}");
        }
    }
}
