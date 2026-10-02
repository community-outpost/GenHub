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
using System.IO;
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
    : BaseContentProvider(contentValidator, installationInstructionsService, logger), IDisposable
{
    private readonly SemaphoreSlim _generalsOnlineDeliveryLock = new(1, 1);
    private readonly IContentResolver _genericCatalogResolver = resolvers.FirstOrDefault(r =>
        string.Equals(r.ResolverId, CatalogConstants.GenericCatalogResolverId, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException("Generic catalog resolver not found");

    private readonly IReadOnlyList<IContentDeliverer> _deliverers = [.. deliverers];

    private readonly IContentDeliverer _httpDeliverer = deliverers.FirstOrDefault(d =>
        string.Equals(d.SourceName, ContentSourceNames.HttpDeliverer, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException("HTTP deliverer not found");

    private readonly ConcurrentDictionary<string, HashSet<ManifestId>> _preExistingManifestIdsByOperation = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, List<ManifestId>> _registeredManifestIdsByOperation = new(StringComparer.OrdinalIgnoreCase);

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
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Invokes RollbackPreparedContentAsync for testing purposes.
    /// </summary>
    /// <param name="originalManifest">The original manifest.</param>
    /// <param name="preparedManifest">The prepared manifest.</param>
    /// <param name="workingDirectory">The working directory.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the rollback operation.</returns>
    internal Task InvokeRollbackPreparedContentAsyncForTesting(
        ContentManifest originalManifest,
        ContentManifest preparedManifest,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        return RollbackPreparedContentAsync(originalManifest, preparedManifest, workingDirectory, cancellationToken);
    }

    /// <summary>
    /// Seeds pre-existing manifest IDs for testing rollback scenarios.
    /// </summary>
    /// <param name="manifestId">The manifest ID.</param>
    /// <param name="workingDirectory">The working directory.</param>
    /// <param name="existingIds">The pre-existing manifest IDs.</param>
    internal void SetPreExistingManifestsForTesting(
        ManifestId manifestId,
        string workingDirectory,
        IEnumerable<ManifestId> existingIds)
    {
        var opKey = GetOperationKey(manifestId.Value, workingDirectory);
        _preExistingManifestIdsByOperation[opKey] = new HashSet<ManifestId>(existingIds);
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
            return await ExecuteSpecializedDelivererAsync(
                specializedDeliverer,
                manifest,
                workingDirectory,
                progress,
                cancellationToken);
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

        var opKey = GetOperationKey(originalManifest.Id.Value, workingDirectory);
        var manifestIdsToRemove = await ResolveManifestIdsToRemoveAsync(opKey, originalManifest.Id, preparedManifest.Version);

        if (manifestIdsToRemove == null || manifestIdsToRemove.Count == 0)
        {
            return;
        }

        Logger.LogWarning("Rolling back generic catalog Generals Online manifest registration for version {Version}", preparedManifest.Version);
        await RemoveManifestsAsync(manifestIdsToRemove, CancellationToken.None);
    }

    /// <inheritdoc />
    protected override Task OnContentPreparationCompletedAsync(
        ContentManifest originalManifest,
        ContentManifest preparedManifest,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var opKey = GetOperationKey(originalManifest.Id.Value, workingDirectory);
        _registeredManifestIdsByOperation.TryRemove(opKey, out _);
        _preExistingManifestIdsByOperation.TryRemove(opKey, out _);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Releases unmanaged and - optionally - managed resources.
    /// </summary>
    /// <param name="disposing"><c>true</c> to release both managed and unmanaged resources; <c>false</c> to release only unmanaged resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _generalsOnlineDeliveryLock.Dispose();
        }
    }

    private static string GetOperationKey(string manifestId, string workingDirectory) =>
        $"{manifestId}::{workingDirectory}";

    private async Task<OperationResult<ContentManifest>> ExecuteSpecializedDelivererAsync(
        IContentDeliverer specializedDeliverer,
        ContentManifest manifest,
        string workingDirectory,
        IProgress<ContentAcquisitionProgress>? progress,
        CancellationToken cancellationToken)
    {
        var isGeneralsOnline = string.Equals(
            specializedDeliverer.SourceName,
            GeneralsOnlineConstants.DelivererSourceName,
            StringComparison.OrdinalIgnoreCase);

        if (isGeneralsOnline)
        {
            if (!OperatingSystem.IsWindows())
            {
                return OperationResult<ContentManifest>.CreateFailure(
                    "GeneralsOnline is currently supported only on Windows. Easy Anti-Cheat was not designed for Wine/Proton environments.");
            }

            await _generalsOnlineDeliveryLock.WaitAsync(cancellationToken);
            try
            {
                HashSet<ManifestId>? preExistingIds = null;
                var opKey = GetOperationKey(manifest.Id.Value, workingDirectory);

                if (manifestPool != null)
                {
                    var baselineResult = await CaptureManifestPoolBaselineAsync(manifest.Id, cancellationToken);
                    if (!baselineResult.Success || baselineResult.Data == null)
                    {
                        return OperationResult<ContentManifest>.CreateFailure(
                            $"Failed to initialize manifest pool state: {baselineResult.FirstError ?? "Unable to retrieve manifest pool"}");
                    }

                    preExistingIds = baselineResult.Data;
                    _preExistingManifestIdsByOperation[opKey] = preExistingIds;
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

                if (deliveryResult.Success && manifestPool != null && preExistingIds != null)
                {
                    return await FinalizeGeneralsOnlineDeliveryAsync(
                        manifest,
                        deliveryResult,
                        workingDirectory,
                        opKey,
                        preExistingIds,
                        cancellationToken);
                }

                return deliveryResult;
            }
            finally
            {
                _generalsOnlineDeliveryLock.Release();
            }
        }

        Logger.LogInformation(
            "Routing generic catalog content {ManifestId} to specialized deliverer: {DelivererSource}",
            manifest.Id,
            specializedDeliverer.SourceName);

        return await DeliverContentOnlyAsync(
            specializedDeliverer,
            manifest,
            workingDirectory,
            progress,
            cancellationToken);
    }

    private async Task<OperationResult<HashSet<ManifestId>>> CaptureManifestPoolBaselineAsync(
        ManifestId manifestId,
        CancellationToken cancellationToken)
    {
        var existingPool = await manifestPool!.GetAllManifestsAsync(cancellationToken);
        if (!existingPool.Success || existingPool.Data == null)
        {
            Logger.LogError(
                "Failed to capture manifest pool baseline prior to Generals Online delivery for {ManifestId}: {Error}",
                manifestId,
                existingPool.FirstError);
            return OperationResult<HashSet<ManifestId>>.CreateFailure(
                existingPool.FirstError ?? "Unable to retrieve manifest pool");
        }

        return OperationResult<HashSet<ManifestId>>.CreateSuccess(
            new HashSet<ManifestId>(existingPool.Data.Select(m => m.Id)));
    }

    private async Task<OperationResult<ContentManifest>> FinalizeGeneralsOnlineDeliveryAsync(
        ContentManifest manifest,
        OperationResult<ContentManifest> deliveryResult,
        string workingDirectory,
        string opKey,
        HashSet<ManifestId> preExistingIds,
        CancellationToken cancellationToken)
    {
        var postPool = await manifestPool!.GetAllManifestsAsync(cancellationToken);

        if (postPool.Success && postPool.Data != null)
        {
            var targetVersion = deliveryResult.Data?.Version ?? manifest.Version;
            var addedIds = postPool.Data
                .Where(m => !preExistingIds.Contains(m.Id) &&
                            string.Equals(m.Publisher?.PublisherType, GeneralsOnlineConstants.PublisherType, StringComparison.OrdinalIgnoreCase) &&
                            (string.IsNullOrEmpty(targetVersion) || string.Equals(m.Version, targetVersion, StringComparison.OrdinalIgnoreCase)))
                .Select(m => m.Id)
                .ToList();

            _registeredManifestIdsByOperation[opKey] = addedIds;
            _preExistingManifestIdsByOperation.TryRemove(opKey, out _);
            return deliveryResult;
        }

        Logger.LogError(
            "Failed to verify manifest pool registrations after delivering Generals Online content: {Error}",
            postPool.FirstError);

        await RollbackPreparedContentAsync(manifest, deliveryResult.Data ?? manifest, workingDirectory, CancellationToken.None);

        return OperationResult<ContentManifest>.CreateFailure(
            $"Failed to verify manifest pool registrations after delivery: {postPool.FirstError ?? "Unable to retrieve manifests"}");
    }

    private async Task<List<ManifestId>?> ResolveManifestIdsToRemoveAsync(
        string opKey,
        ManifestId originalManifestId,
        string preparedVersion)
    {
        if (_registeredManifestIdsByOperation.TryRemove(opKey, out var registeredIds) && registeredIds is { Count: > 0 })
        {
            return registeredIds;
        }

        if (_preExistingManifestIdsByOperation.TryRemove(opKey, out var preExistingIds) && preExistingIds != null)
        {
            return await RecoverManifestIdsFromPoolAsync(originalManifestId, preparedVersion, preExistingIds);
        }

        return null;
    }

    private async Task<List<ManifestId>?> RecoverManifestIdsFromPoolAsync(
        ManifestId originalManifestId,
        string preparedVersion,
        HashSet<ManifestId> preExistingIds)
    {
        try
        {
            var currentPool = await manifestPool!.GetAllManifestsAsync(CancellationToken.None);
            if (currentPool.Success && currentPool.Data != null)
            {
                return currentPool.Data
                    .Where(m => !preExistingIds.Contains(m.Id) &&
                                string.Equals(m.Publisher?.PublisherType, GeneralsOnlineConstants.PublisherType, StringComparison.OrdinalIgnoreCase) &&
                                string.Equals(m.Version, preparedVersion, StringComparison.OrdinalIgnoreCase))
                    .Select(m => m.Id)
                    .ToList();
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Logger.LogError(ex, "Failed to recover manifest registrations during rollback for {ManifestId}", originalManifestId);
        }

        return null;
    }

    private async Task RemoveManifestsAsync(
        IEnumerable<ManifestId> manifestIdsToRemove,
        CancellationToken cancellationToken)
    {
        foreach (var manifestId in manifestIdsToRemove)
        {
            try
            {
                var removeResult = await manifestPool!.RemoveManifestAsync(manifestId, false, cancellationToken);
                if (!removeResult.Success)
                {
                    Logger.LogWarning("Failed to remove manifest {ManifestId} during rollback: {Error}", manifestId, removeResult.FirstError);
                }
                else
                {
                    Logger.LogInformation("Unregistered manifest {ManifestId} during rollback", manifestId);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                Logger.LogError(ex, "Error occurred during generic catalog manifest registration rollback for {ManifestId}", manifestId);
            }
        }
    }
}
