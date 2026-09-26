using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Interfaces.Telemetry;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Storage.Services;

/// <summary>
/// Manages CAS reference lifecycle with proper ordering guarantees.
/// Owns garbage collection so it only runs after references are properly untracked,
/// and only deletes blobs that no tracked reference and no persisted manifest link.
/// </summary>
public class CasLifecycleManager(
    ICasReferenceTracker referenceTracker,
    IContentManifestPool manifestPool,
    ICasStorage casStorage,
    IOptions<CasConfiguration> config,
    ILogger<CasLifecycleManager> logger,
    CasWriteFence writeFence,
    ICasPoolManager? poolManager = null,
    ITelemetryService? telemetryService = null) : ICasLifecycleManager, IDisposable
{
    private readonly SemaphoreSlim _gcLock = new(1, 1);

    /// <inheritdoc/>
    public async Task<OperationResult> ReplaceManifestReferencesAsync(
        string oldManifestId,
        ContentManifest newManifest,
        CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogInformation(
                "Replacing manifest references: {OldId} → {NewId}",
                oldManifestId,
                newManifest.Id.Value);

            // Step 1: Track new manifest first (ensures new content is protected)
            var trackResult = await referenceTracker.TrackManifestReferencesAsync(
                newManifest.Id.Value,
                newManifest,
                cancellationToken);

            if (!trackResult.Success)
            {
                logger.LogError(
                    "Failed to track new manifest references: {NewId} -> {Error}",
                    newManifest.Id.Value,
                    trackResult.FirstError);
                return trackResult;
            }

            // Step 2: Untrack old manifest (makes old content eligible for GC)
            if (!string.Equals(oldManifestId, newManifest.Id.Value, StringComparison.OrdinalIgnoreCase))
            {
                var untrackResult = await referenceTracker.UntrackManifestAsync(oldManifestId, cancellationToken);
                if (!untrackResult.Success)
                {
                    logger.LogWarning(
                        "Failed to untrack old manifest references: {OldId} -> {Error}",
                        oldManifestId,
                        untrackResult.FirstError);
                    return untrackResult;
                }
            }

            logger.LogInformation(
                "Successfully replaced manifest references: {OldId} → {NewId}",
                oldManifestId,
                newManifest.Id.Value);

            return OperationResult.CreateSuccess();
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Operation cancelled during manifest reference replacement");
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to replace manifest references: {OldId} → {NewId}",
                oldManifestId,
                newManifest.Id.Value);
            return OperationResult.CreateFailure($"Failed to replace references: {ex.Message}");
        }
    }

    /// <summary>
    /// Untracks multiple manifests in bulk.
    /// Note: Returns Success=false if any individual manifests fail to untrack (partial success).
    /// Callers can check <see cref="BulkUntrackResult.Errors"/> to detect individual failures.
    /// </summary>
    /// <param name="manifestIds">The IDs of the manifests to untrack.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A result containing the bulk untrack stats and any individual errors.</returns>
    public async Task<OperationResult<BulkUntrackResult>> UntrackManifestsAsync(
        IEnumerable<string> manifestIds,
        CancellationToken cancellationToken = default)
    {
        var ids = manifestIds.ToList();
        int untracked = 0;
        var errors = new List<string>();

        foreach (var manifestId in ids)
        {
            try
            {
                var result = await referenceTracker.UntrackManifestAsync(manifestId, cancellationToken);
                if (result.Success)
                {
                    untracked++;
                    logger.LogDebug("Untracked manifest: {ManifestId}", manifestId);
                }
                else
                {
                    var msg = $"Failed to untrack {manifestId}: {result.FirstError}";
                    errors.Add(msg);
                    logger.LogWarning("{Message}", msg);
                }
            }
            catch (Exception ex)
            {
                var msg = $"Error untracking {manifestId}: {ex.Message}";
                errors.Add(msg);
                logger.LogWarning(ex, "{Message}", msg);
            }
        }

        var resultData = new BulkUntrackResult(untracked, ids.Count, errors);

        if (errors.Count > 0)
        {
            logger.LogError("Untracked {Count}/{Total} manifests with {ErrorCount} errors", untracked, ids.Count, errors.Count);

            // Return FAILURE because we have individual errors, ensuring callers
            // don't proceed with inconsistent state (partial success).
            return OperationResult<BulkUntrackResult>.CreateFailure(
                $"Untracking failed for {errors.Count} manifests. See logs for details.", resultData, TimeSpan.Zero);
        }

        logger.LogInformation("Untracked {Count}/{Total} manifests", untracked, ids.Count);
        return OperationResult<BulkUntrackResult>.CreateSuccess(resultData);
    }

    /// <inheritdoc/>
    public async Task<OperationResult<GarbageCollectionStats>> RunGarbageCollectionAsync(
        bool force = false,
        TimeSpan? lockTimeout = null,
        CancellationToken cancellationToken = default)
    {
        // Ensure only one GC runs at a time
        var timeout = lockTimeout ?? config.Value.GcLockTimeout;
        if (!await _gcLock.WaitAsync(timeout, cancellationToken))
        {
            logger.LogWarning("GC already in progress, skipping");

            // Return InProgressResult which has InProgress=true and Skipped=true
            return OperationResult<GarbageCollectionStats>.CreateSuccess(GarbageCollectionStats.InProgressResult);
        }

        IDisposable? collectionLease = null;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            // Forced collection bypasses the grace period, so hold the exclusive
            // collection lease from here through the whole sweep. Imports starting
            // mid-sweep wait on the fence instead of losing untracked blobs.
            if (force && !writeFence.TryAcquireCollectionLease(TimeSpan.Zero, out collectionLease))
            {
                logger.LogWarning("Forced garbage collection refused: content is being imported into CAS");
                return OperationResult<GarbageCollectionStats>.CreateFailure(
                    "Cannot clean CAS storage while content is being imported. Try again when the import finishes.");
            }

            logger.LogInformation("Starting garbage collection (force={Force})", force);

            var liveSet = await BuildLiveSetAsync(cancellationToken);
            var stats = await CollectUnreferencedObjectsAsync(liveSet, force, cancellationToken);
            stopwatch.Stop();
            stats = stats with { Duration = stopwatch.Elapsed };

            logger.LogInformation(
                "GC completed: scanned={Scanned}, referenced={Referenced}, deleted={Deleted}, freed={Bytes} bytes",
                stats.ObjectsScanned,
                stats.ObjectsReferenced,
                stats.ObjectsDeleted,
                stats.BytesFreed);

            telemetryService?.TrackEvent(TelemetryConstants.Events.CasGarbageCollected, new Dictionary<string, object?>
            {
                [TelemetryConstants.Properties.DurationSeconds] = stopwatch.Elapsed.TotalSeconds,
                [TelemetryConstants.Properties.ObjectsScanned] = stats.ObjectsScanned,
                [TelemetryConstants.Properties.ObjectsReferenced] = stats.ObjectsReferenced,
                [TelemetryConstants.Properties.ObjectsDeleted] = stats.ObjectsDeleted,
                [TelemetryConstants.Properties.BytesFreed] = stats.BytesFreed,
                [TelemetryConstants.Properties.Success] = true,
            });

            return OperationResult<GarbageCollectionStats>.CreateSuccess(stats);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Garbage collection cancelled");
            throw;
        }
        catch (Exception ex)
        {
            telemetryService?.TrackEvent(TelemetryConstants.Events.CasGarbageCollected, new Dictionary<string, object?>
            {
                [TelemetryConstants.Properties.DurationSeconds] = stopwatch.Elapsed.TotalSeconds,
                [TelemetryConstants.Properties.Success] = false,
                [TelemetryConstants.Properties.ErrorMessage] = ex.Message,
            });

            logger.LogError(ex, "Garbage collection failed");
            return OperationResult<GarbageCollectionStats>.CreateFailure($"GC failed: {ex.Message}");
        }
        finally
        {
            collectionLease?.Dispose();
            _gcLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<CasReferenceAudit>> GetReferenceAuditAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Use the same live set as garbage collection so the audit agrees with it.
            var liveSet = await BuildLiveSetAsync(cancellationToken);

            // Get all CAS objects across every pool
            var allObjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var poolStorage in CasPoolStorages.GetStoragesForEnumeration(poolManager, casStorage))
            {
                var hashes = await poolStorage.GetAllObjectHashesAsync(cancellationToken);
                allObjects.UnionWith(hashes);
            }

            // Count orphaned objects
            var orphanedCount = allObjects.Except(liveSet, StringComparer.OrdinalIgnoreCase).Count();

            // Count manifests and workspaces from refs directory
            var casRoot = config.Value.CasRootPath;
            if (string.IsNullOrEmpty(casRoot))
            {
                return OperationResult<CasReferenceAudit>.CreateFailure("CasRootPath is not configured");
            }

            var refsDir = Path.Combine(casRoot, "refs");
            var manifestsDir = Path.Combine(refsDir, "manifests");
            var workspacesDir = Path.Combine(refsDir, "workspaces");

            var manifestIds = Directory.Exists(manifestsDir)
                ? Directory.GetFiles(manifestsDir, "*.refs")
                    .Select(f => Path.GetFileNameWithoutExtension(f))
                    .ToList()
                : [];

            var workspaceIds = Directory.Exists(workspacesDir)
                ? Directory.GetFiles(workspacesDir, "*.refs")
                    .Select(f => Path.GetFileNameWithoutExtension(f))
                    .ToList()
                : [];

            var audit = new CasReferenceAudit
            {
                TotalManifests = manifestIds.Count,
                TotalWorkspaces = workspaceIds.Count,
                TotalReferencedHashes = liveSet.Count,
                TotalCasObjects = allObjects.Count,
                OrphanedObjects = orphanedCount,
                ManifestIds = manifestIds,
                WorkspaceIds = workspaceIds,
            };

            return OperationResult<CasReferenceAudit>.CreateSuccess(audit);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Operation cancelled during reference audit");
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get reference audit");
            return OperationResult<CasReferenceAudit>.CreateFailure($"Audit failed: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _gcLock.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<HashSet<string>> BuildLiveSetAsync(CancellationToken cancellationToken)
    {
        // Reference files for tracked manifests and workspaces. The tracker throws when
        // it cannot enumerate, which fails collection closed before anything is deleted.
        var liveSet = await referenceTracker.GetAllReferencedHashesAsync(cancellationToken);

        // Every persisted manifest, even one whose reference file is missing or stale.
        // A CAS blob linked by a manifest in the pool is never deleted.
        var manifestsResult = await manifestPool.GetAllManifestsAsync(cancellationToken);
        if (!manifestsResult.Success || manifestsResult.Data == null)
        {
            throw new InvalidOperationException(
                $"Cannot enumerate persisted manifests: {manifestsResult.FirstError ?? "unknown error"}");
        }

        foreach (var manifest in manifestsResult.Data)
        {
            liveSet.UnionWith(ManifestHelper.GetContentAddressableHashes(manifest));
        }

        return liveSet;
    }

    private async Task<GarbageCollectionStats> CollectUnreferencedObjectsAsync(
        HashSet<string> liveSet,
        bool force,
        CancellationToken cancellationToken)
    {
        var scanned = 0;
        var referenced = 0;
        var deleted = 0;
        long bytesFreed = 0;

        foreach (var poolStorage in CasPoolStorages.GetStoragesForEnumeration(poolManager, casStorage))
        {
            var hashes = await poolStorage.GetAllObjectHashesAsync(cancellationToken);
            foreach (var hash in hashes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                scanned++;

                if (!liveSet.Contains(hash) && (force || await IsPastGracePeriodAsync(poolStorage, hash, cancellationToken)))
                {
                    var (objectDeleted, objectBytesFreed) = await TryDeleteObjectAsync(poolStorage, hash, cancellationToken);
                    if (objectDeleted)
                    {
                        deleted++;
                        bytesFreed += objectBytesFreed;
                        continue;
                    }
                }

                referenced++;
            }
        }

        return new GarbageCollectionStats
        {
            ObjectsScanned = scanned,
            ObjectsReferenced = referenced,
            ObjectsDeleted = deleted,
            BytesFreed = bytesFreed,
        };
    }

    private async Task<bool> IsPastGracePeriodAsync(ICasStorage poolStorage, string hash, CancellationToken cancellationToken)
    {
        var createdAt = await poolStorage.GetObjectCreationTimeAsync(hash, cancellationToken);
        if (createdAt == null)
        {
            // Fail closed: unknown age is treated as young.
            return false;
        }

        return DateTime.UtcNow - createdAt.Value >= config.Value.GcGracePeriod;
    }

    private async Task<(bool Deleted, long BytesFreed)> TryDeleteObjectAsync(
        ICasStorage poolStorage,
        string hash,
        CancellationToken cancellationToken)
    {
        try
        {
            var objectPath = poolStorage.GetObjectPath(hash);
            long size = 0;
            if (File.Exists(objectPath))
            {
                size = new FileInfo(objectPath).Length;
            }

            await poolStorage.DeleteObjectAsync(hash, cancellationToken);
            logger.LogDebug("GC deleted unreferenced object {Hash} ({Size} bytes)", hash, size);
            return (true, size);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "GC failed to delete unreferenced object {Hash}; keeping it", hash);
            return (false, 0);
        }
    }
}
