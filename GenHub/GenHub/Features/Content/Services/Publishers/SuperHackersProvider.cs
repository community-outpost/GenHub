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
using GenHub.Features.Content.Services.ContentProviders;
using Microsoft.Extensions.Logging;

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
    ILogger<SuperHackersProvider> logger)
    : BaseContentProvider(contentValidator, logger)
{
    private readonly IContentDiscoverer _discoverer = discoverers.FirstOrDefault(d =>
            d.SourceName.Contains("GitHub", StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException("No GitHub discoverer found for SuperHackers");

    private readonly IContentResolver _resolver = resolvers.FirstOrDefault(r =>
            r.ResolverId?.Equals(SuperHackersConstants.ResolverId, StringComparison.OrdinalIgnoreCase) == true)
        ?? throw new InvalidOperationException("No GitHub resolver found for SuperHackers");

    private readonly IContentDeliverer _deliverer = deliverers.FirstOrDefault(d =>
            d.SourceName?.Equals(ContentSourceNames.GitHubDeliverer, StringComparison.OrdinalIgnoreCase) == true)
        ?? throw new InvalidOperationException("No GitHub deliverer found for SuperHackers");

    private ProviderDefinition? _cachedProviderDefinition;

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
        var searchResult = new ContentSearchResult
        {
            Id = contentId,
            Name = SuperHackersConstants.PublisherName,
            Version = contentId,
            ProviderName = SourceName,
            RequiresResolution = true,
            ResolverId = SuperHackersConstants.ResolverId,
        };

        var manifestResult = await Resolver.ResolveAsync(searchResult, cancellationToken);
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

    /// <inheritdoc/>
    /// <remarks>
    /// Returns the TheSuperHackers provider definition loaded from JSON configuration.
    /// The definition contains GitHub repository info, endpoints, and other configuration.
    /// </remarks>
    protected override ProviderDefinition? GetProviderDefinition()
    {
        // Use cached definition if available
        if (_cachedProviderDefinition != null)
        {
            return _cachedProviderDefinition;
        }

        // Try to get from the loader (it should already be loaded at startup)
        _cachedProviderDefinition = providerDefinitionLoader.GetProvider(SuperHackersConstants.PublisherId);

        if (_cachedProviderDefinition == null)
        {
            Logger.LogWarning(
                "No provider definition found for {ProviderId}, using hardcoded constants",
                SuperHackersConstants.PublisherId);
        }
        else
        {
            Logger.LogInformation(
                "Using provider definition for {ProviderId} from JSON configuration",
                SuperHackersConstants.PublisherId);
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
        Logger.LogInformation("Preparing SuperHackers content: {Version}", manifest.Version);

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
            Logger.LogInformation("Successfully prepared SuperHackers content {ManifestId}", manifest.Id);
            return OperationResult<ContentManifest>.CreateSuccess(resultManifest);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to prepare SuperHackers content");
            return OperationResult<ContentManifest>.CreateFailure(
                $"Content preparation failed: {ex.Message}");
        }
    }
}
