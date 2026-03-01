using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GenHub.Features.Storage.Services;

/// <summary>
/// Manages CAS reference lifecycle with proper ordering guarantees.
/// Wraps CasReferenceTracker and CasService to ensure GC only runs after untracking.
/// </summary>
public class CasLifecycleManager(
    ICasReferenceTracker referenceTracker,
    ICasService casService,
    ICasStorage casStorage,
    IOptions<CasConfiguration> config,
    ILogger<CasLifecycleManager> logger) : ICasLifecycleManager, IDisposable
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

        try
        {
            var stopwatch = Stopwatch.StartNew();
            logger.LogInformation("Starting garbage collection (force={Force})", force);

            var gcResult = await casService.RunGarbageCollectionAsync(force, cancellationToken);

            stopwatch.Stop();

            var stats = new GarbageCollectionStats
            {
                ObjectsScanned = gcResult.ObjectsScanned,
                ObjectsReferenced = gcResult.ObjectsReferenced,
                ObjectsDeleted = gcResult.ObjectsDeleted,
                BytesFreed = gcResult.BytesFreed,
                Duration = stopwatch.Elapsed,
                Skipped = false,
            };

            logger.LogInformation(
                "GC completed: scanned={Scanned}, referenced={Referenced}, deleted={Deleted}, freed={Bytes} bytes",
                stats.ObjectsScanned,
                stats.ObjectsReferenced,
                stats.ObjectsDeleted,
                stats.BytesFreed);

            return OperationResult<GarbageCollectionStats>.CreateSuccess(stats);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Garbage collection cancelled");
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Garbage collection failed");
            return OperationResult<GarbageCollectionStats>.CreateFailure($"GC failed: {ex.Message}");
        }
        finally
        {
            _gcLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<CasReferenceAudit>> GetReferenceAuditAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Get all referenced hashes
            var referencedHashes = await referenceTracker.GetAllReferencedHashesAsync(cancellationToken);

            // Get all CAS objects
            var allObjects = await casStorage.GetAllObjectHashesAsync(cancellationToken);

            // Count orphaned objects
            var orphanedCount = allObjects.Except(referencedHashes).Count();

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
                TotalReferencedHashes = referencedHashes.Count,
                TotalCasObjects = allObjects.Length,
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
}
