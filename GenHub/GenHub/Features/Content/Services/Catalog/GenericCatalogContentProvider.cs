using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.Content.Services.ContentProviders;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Content.Services.Catalog;

/// <summary>
/// Content provider that acquires content from any subscribed publisher catalog
/// through the generic catalog pipeline (resolution → HTTP delivery → manifest factory).
/// </summary>
public class GenericCatalogContentProvider(
    GenericCatalogDiscoverer discoverer,
    IEnumerable<IContentResolver> resolvers,
    IEnumerable<IContentDeliverer> deliverers,
    GenericCatalogManifestFactory manifestFactory,
    ILogger<GenericCatalogContentProvider> logger,
    IContentValidator contentValidator,
    IInstallationInstructionsService installationInstructionsService,
    IContentManifestPool? manifestPool = null)
    : BaseContentProvider(contentValidator, installationInstructionsService, logger)
{
    private readonly IContentResolver _genericCatalogResolver = resolvers.FirstOrDefault(r =>
        string.Equals(r.ResolverId, CatalogConstants.GenericCatalogResolverId, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException("Generic catalog resolver not found");

    private readonly IReadOnlyList<IContentDeliverer> _deliverers = [.. deliverers];

    private readonly IContentDeliverer _httpDeliverer = deliverers.FirstOrDefault(d =>
        string.Equals(d.SourceName, ContentSourceNames.HttpDeliverer, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException("HTTP deliverer not found");

    private readonly ConcurrentDictionary<string, List<string>> _registeredManifestIdsByOperation = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public override string SourceName => CatalogConstants.GenericCatalogProviderName;

    /// <inheritdoc />
    public override string Description => "Provides content from subscribed publisher catalogs";

    /// <inheritdoc />
    protected override IContentDiscoverer Discoverer => discoverer;

    /// <inheritdoc />
    protected override IContentResolver Resolver => _genericCatalogResolver;

    /// <inheritdoc />
    protected override IContentDeliverer Deliverer => _httpDeliverer;

    /// <inheritdoc />
    public override Task<OperationResult<IEnumerable<ContentSearchResult>>> SearchAsync(
        ContentSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        // Subscription feeds are browsed through per-subscription discoverers configured
        // with a catalog URL. This provider has no subscription scope, so global search
        // has nothing to return; acquisition still works through resolution metadata.
        return Task.FromResult(OperationResult<IEnumerable<ContentSearchResult>>.CreateSuccess([]));
    }

    /// <inheritdoc />
    public override Task<OperationResult<ContentManifest>> GetValidatedContentAsync(
        string contentId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(contentId))
        {
            return Task.FromResult(OperationResult<ContentManifest>.CreateFailure("Content ID cannot be null or empty"));
        }

        return Task.FromResult(OperationResult<ContentManifest>.CreateFailure(
            $"Generic catalog content '{contentId}' requires resolution metadata and cannot be fetched by ID alone."));
    }

    /// <inheritdoc />
    protected override async Task<OperationResult<ContentManifest>> PrepareContentInternalAsync(
        ContentManifest manifest,
        string workingDirectory,
        IProgress<ContentAcquisitionProgress>? progress,
        CancellationToken cancellationToken)
    {
        Logger.LogInformation("Preparing generic catalog content: {ManifestId} ({Name})", manifest.Id, manifest.Name);

        var specializedDeliverer = _deliverers.FirstOrDefault(d =>
            !string.Equals(d.SourceName, ContentSourceNames.HttpDeliverer, StringComparison.OrdinalIgnoreCase) &&
            d.CanDeliver(manifest));

        if (specializedDeliverer != null)
        {
            var isGeneralsOnline = string.Equals(
                specializedDeliverer.SourceName,
                GeneralsOnlineConstants.DelivererSourceName,
                StringComparison.OrdinalIgnoreCase);

            if (isGeneralsOnline && !OperatingSystem.IsWindows())
            {
                return OperationResult<ContentManifest>.CreateFailure(
                    "GeneralsOnline is currently supported only on Windows. Easy Anti-Cheat was not designed for Wine/Proton environments.");
            }

            HashSet<string>? preExistingIds = null;
            if (isGeneralsOnline && manifestPool != null)
            {
                var existingPool = await manifestPool.GetAllManifestsAsync(cancellationToken);
                if (existingPool.Success && existingPool.Data != null)
                {
                    preExistingIds = new HashSet<string>(existingPool.Data.Select(m => m.Id), StringComparer.OrdinalIgnoreCase);
                }
            }

            Logger.LogInformation(
                "Routing generic catalog content {ManifestId} to specialized deliverer: {DelivererSource}",
                manifest.Id,
                specializedDeliverer.SourceName);

            var deliveryResult = await DeliverContentOnlyAsync(
                specializedDeliverer,
                manifest,
                workingDirectory,
                progress,
                cancellationToken);

            if (deliveryResult.Success && isGeneralsOnline && manifestPool != null)
            {
                var postPool = await manifestPool.GetAllManifestsAsync(cancellationToken);
                if (postPool.Success && postPool.Data != null)
                {
                    var addedIds = postPool.Data
                        .Where(m => (preExistingIds == null || !preExistingIds.Contains(m.Id)) &&
                                    string.Equals(m.Publisher?.PublisherType, GeneralsOnlineConstants.PublisherType, StringComparison.OrdinalIgnoreCase))
                        .Select(m => m.Id)
                        .ToList();

                    var opKey = GetOperationKey(manifest.Id, workingDirectory);
                    _registeredManifestIdsByOperation[opKey] = addedIds;
                }
            }

            return deliveryResult;
        }

        return await DeliverAndEnrichContentAsync(
            _httpDeliverer,
            manifestFactory,
            manifest,
            workingDirectory,
            progress,
            cancellationToken);
    }

    /// <inheritdoc />
    protected override async Task RollbackPreparedContentAsync(
        ContentManifest originalManifest,
        ContentManifest preparedManifest,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        if (manifestPool == null)
        {
            return;
        }

        var opKey = GetOperationKey(originalManifest.Id, workingDirectory);
        if (!_registeredManifestIdsByOperation.TryRemove(opKey, out var registeredIds) || registeredIds == null || registeredIds.Count == 0)
        {
            return;
        }

        Logger.LogWarning("Rolling back generic catalog Generals Online manifest registration for version {Version}", preparedManifest.Version);

        foreach (var manifestId in registeredIds)
        {
            try
            {
                var removeResult = await manifestPool.RemoveManifestAsync(manifestId, cancellationToken: cancellationToken);
                if (!removeResult.Success)
                {
                    Logger.LogWarning("Failed to remove manifest {ManifestId} during rollback: {Error}", manifestId, removeResult.FirstError);
                }
                else
                {
                    Logger.LogInformation("Unregistered manifest {ManifestId} during rollback", manifestId);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error occurred during generic catalog manifest registration rollback for {ManifestId}", manifestId);
            }
        }
    }

    /// <inheritdoc />
    protected override Task OnContentPreparationCompletedAsync(
        ContentManifest originalManifest,
        ContentManifest preparedManifest,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var opKey = GetOperationKey(originalManifest.Id, workingDirectory);
        _registeredManifestIdsByOperation.TryRemove(opKey, out _);
        return Task.CompletedTask;
    }

    internal void SetRegisteredManifestsForTesting(string manifestId, string workingDirectory, IEnumerable<string> registeredIds)
    {
        var opKey = GetOperationKey(manifestId, workingDirectory);
        _registeredManifestIdsByOperation[opKey] = [.. registeredIds];
    }

    internal Task InvokeRollbackPreparedContentAsyncForTesting(
        ContentManifest originalManifest,
        ContentManifest preparedManifest,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        return RollbackPreparedContentAsync(originalManifest, preparedManifest, workingDirectory, cancellationToken);
    }

    private static string GetOperationKey(string manifestId, string workingDirectory) =>
        $"{manifestId}::{workingDirectory}";
}
