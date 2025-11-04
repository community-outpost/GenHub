using GenHub.Core.Interfaces.Content;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
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
public class GeneralsOnlineProvider : BaseContentProvider
{
    private readonly IContentDiscoverer _discoverer;
    private readonly IContentResolver _resolver;
    private readonly IContentDeliverer _deliverer;

    /// <summary>
    /// Initializes a new instance of the <see cref="GeneralsOnlineProvider"/> class.
    /// </summary>
    /// <param name="discoverers">Available content discoverers.</param>
    /// <param name="resolvers">Available content resolvers.</param>
    /// <param name="deliverers">Available content deliverers.</param>
    /// <param name="contentValidator">The content validator.</param>
    /// <param name="logger">The logger.</param>
    public GeneralsOnlineProvider(
        IEnumerable<IContentDiscoverer> discoverers,
        IEnumerable<IContentResolver> resolvers,
        IEnumerable<IContentDeliverer> deliverers,
        IContentValidator contentValidator,
        ILogger<GeneralsOnlineProvider> logger)
        : base(contentValidator, logger)
    {
        _discoverer = discoverers.FirstOrDefault(d => d.SourceName.Contains("Generals Online"))
            ?? throw new InvalidOperationException("No Generals Online discoverer found. Ensure a discoverer with 'Generals Online' in its SourceName is registered.");
        _resolver = resolvers.FirstOrDefault(r => r.ResolverId.Contains("GeneralsOnline"))
            ?? throw new InvalidOperationException("No Generals Online resolver found. Ensure a resolver with 'GeneralsOnline' in its ResolverId is registered.");
        _deliverer = deliverers.FirstOrDefault(d => d.SourceName.Contains("Generals Online Deliverer"))
            ?? throw new InvalidOperationException("No Generals Online deliverer found. Ensure GeneralsOnlineDeliverer is registered.");
    }

    /// <inheritdoc />
    public override string SourceName => "GeneralsOnline";

    /// <inheritdoc />
    public override string Description =>
        "Official Generals Online multiplayer service and community mod platform";

    /// <inheritdoc />
    public override bool IsEnabled => true;

    /// <inheritdoc />
    public override ContentSourceCapabilities Capabilities =>
        ContentSourceCapabilities.RequiresDiscovery |
        ContentSourceCapabilities.SupportsPackageAcquisition;

    /// <inheritdoc />
    protected override IContentDiscoverer Discoverer => _discoverer;

    /// <inheritdoc />
    protected override IContentResolver Resolver => _resolver;

    /// <inheritdoc />
    protected override IContentDeliverer Deliverer => _deliverer;

    /// <inheritdoc />
    public override async Task<OperationResult<ContentManifest>> GetValidatedContentAsync(
        string contentId,
        CancellationToken cancellationToken = default)
    {
        Logger.LogInformation("Getting Generals Online manifest for: {ContentId}", contentId);

        var searchResult = new ContentSearchResult
        {
            Id = contentId,
            Name = "Generals Online",
            Version = contentId,
            ProviderName = SourceName,
            RequiresResolution = true,
            ResolverId = "GeneralsOnline",
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

    /// <inheritdoc />
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
                return OperationResult<ContentManifest>.CreateFailure($"Cannot deliver content for manifest {manifest.Id}");
            }

            var deliveryResult = await Deliverer.DeliverContentAsync(manifest, workingDirectory, progress, cancellationToken);
            if (!deliveryResult.Success)
            {
                return OperationResult<ContentManifest>.CreateFailure($"Content delivery failed: {deliveryResult.FirstError}");
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
