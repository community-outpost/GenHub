using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Features.Storage.Services;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Content.Services.Reconciliation;

/// <summary>
/// Central orchestrator for content reconciliation operations.
/// Enforces correct operation ordering to prevent GC timing issues:
/// Update Profiles → Untrack Old → Remove Old → GC.
/// </summary>
public class ContentReconciliationOrchestrator(
    IContentReconciliationService reconciliationService,
    IContentManifestPool manifestPool,
    ICasLifecycleManager casLifecycleManager,
    IReconciliationAuditLog auditLog,
    ILogger<ContentReconciliationOrchestrator> logger) : IContentReconciliationOrchestrator
{
    /// <inheritdoc/>
    public async Task<OperationResult<ContentReplacementResult>> ExecuteContentReplacementAsync(
        ContentReplacementRequest request,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var operationId = Guid.NewGuid().ToString("N")[..ReconciliationConstants.OperationIdLength];
        var warnings = new List<string>();

        bool criticalFailureOccurred = false;

        logger.LogInformation(
            "[Orchestrator:{OpId}] Starting content replacement for {Count} manifests",
            operationId,
            request.ManifestMapping.Count);

        // Publish start event
        WeakReferenceMessenger.Default.Send(new ReconciliationStartedEvent(
            operationId,
            "ContentReplacement",
            0, // Will be determined during execution
            request.ManifestMapping.Count));

        try
        {
            // STEP 1: Update all profiles with new manifest references
            logger.LogDebug("[Orchestrator:{OpId}] Step 1: Updating profiles", operationId);
            var profileResult = await reconciliationService.OrchestrateBulkUpdateAsync(
                request.ManifestMapping,
                false, // GC handled in Step 4
                cancellationToken);

            int profilesUpdated = profileResult.Data?.ProfilesUpdated ?? 0;
            int workspacesInvalidated = profileResult.Data?.WorkspacesInvalidated ?? 0;
            if (!profileResult.Success)
            {
                warnings.Add($"Profile update failure: {profileResult.FirstError}");
                criticalFailureOccurred = true;
            }
            else if (profileResult.Data != null && profileResult.Data.FailedProfilesCount > 0)
            {
                warnings.Add($"Partial failure: {profileResult.Data!.FailedProfilesCount} profiles failed to update");
                criticalFailureOccurred = true;
            }

            // STEP 2: Untrack old manifests (MUST happen before GC)
            int manifestsRemoved = 0;
            if (request.RemoveOldManifests)
            {
                logger.LogDebug("[Orchestrator:{OpId}] Step 2: Untracking old manifests", operationId);

                // Untrack CAS references before removal
                // Untrack CAS references before removal
                var untrackResult = await casLifecycleManager.UntrackManifestsAsync([.. request.ManifestMapping.Keys], cancellationToken);

                // untrackResult.Success is now TRUE even for partial failures, unless the op crashed
                if (untrackResult.Success && untrackResult.Data != null)
                {
                    var stats = untrackResult.Data;

                    // Check for partial success / errors
                    if (stats.Errors.Count > 0)
                    {
                        warnings.Add($"Manifest untracking had errors: {stats.Errors.Count} errors");
                        foreach(var err in stats.Errors) warnings.Add(err);

                        criticalFailureOccurred = true;

                        if (stats.Untracked < request.ManifestMapping.Count)
                        {
                            warnings.Add($"Manifest untracking partial success (during failure): {stats.Untracked}/{request.ManifestMapping.Count} untracked");
                        }
                    }
                    else if (stats.Untracked < request.ManifestMapping.Count)
                    {
                         // No errors reported but count mismatch?
                        warnings.Add($"Manifest untracking partial success: {stats.Untracked}/{request.ManifestMapping.Count} untracked");
                        criticalFailureOccurred = true;
                    }
                }
                else
                {
                    // Hard failure (exception etc)
                     warnings.Add($"Manifest untracking failed entirely: {untrackResult.FirstError}");
                     criticalFailureOccurred = true;
                }

                // Publish removing events for each manifest
                foreach (var oldId in request.ManifestMapping.Keys)
                {
                    WeakReferenceMessenger.Default.Send(new ContentRemovingEvent(oldId, null, "Replacement"));
                }
            }

            // STEP 3: Remove old manifest files from pool
            logger.LogDebug("[Orchestrator:{OpId}] Step 3: Removing old manifests from pool", operationId);
            if (request.RemoveOldManifests)
            {
                if (criticalFailureOccurred)
                {
                    logger.LogWarning("[Orchestrator:{OpId}] Skipping manifest removal step due to previous errors", operationId);
                    warnings.Add("Manifest removal step skipped due to previous errors in profile update or untracking.");
                }
                else
                {
                    foreach (var oldId in request.ManifestMapping.Keys)
                    {
                        try
                        {
                            // Use helper for optimized removal
                            // Only skip untracking if we are sure everything went well so far
                            var removeResult = await RemoveManifestWithOptimizedUntrackingAsync(oldId, skipUntrack: true, cancellationToken);
                            if (removeResult.Success)
                            {
                                manifestsRemoved++;
                            }
                            else
                            {
                                warnings.Add(removeResult.FirstError ?? "Manifest removal failed with unknown error");
                                criticalFailureOccurred = true;
                            }
                        }
                        catch (Exception ex)
                        {
                            warnings.Add($"Error removing manifest {oldId}: {ex.Message}");
                            criticalFailureOccurred = true;
                        }
                    }
                }
            }

            // STEP 4: Run GC (now safe - .refs files are gone)
            int casObjectsCollected = 0;
            long bytesFreed = 0;
            if (request.RunGarbageCollection)
            {
                if (criticalFailureOccurred)
                {
                    logger.LogWarning("[Orchestrator:{OpId}] Skipping GC due to previous manifest removal failures", operationId);
                    warnings.Add("Garbage collection skipped due to failures in manifest untracking or removal");
                }
                else
                {
                    logger.LogDebug("[Orchestrator:{OpId}] Step 4: Running garbage collection", operationId);
                    var gcResult = await casLifecycleManager.RunGarbageCollectionAsync(force: false, cancellationToken: cancellationToken);
                    if (gcResult.Success && gcResult.Data != null)
                    {
                        casObjectsCollected = gcResult.Data.ObjectsDeleted;
                        bytesFreed = gcResult.Data.BytesFreed;
                    }
                }
            }

            stopwatch.Stop();

            var result = new ContentReplacementResult
            {
                ProfilesUpdated = profilesUpdated,
                WorkspacesInvalidated = workspacesInvalidated,
                ManifestsRemoved = manifestsRemoved,
                CasObjectsCollected = casObjectsCollected,
                BytesFreed = bytesFreed,
                Duration = stopwatch.Elapsed,
                Warnings = warnings,
            };

            await auditLog.LogOperationAsync(
                new ReconciliationAuditEntry
                {
                    OperationId = operationId,
                    OperationType = ReconciliationOperationType.ManifestReplacement,
                    Timestamp = DateTime.UtcNow,
                    Source = request.Source,
                    AffectedManifestIds = [.. request.ManifestMapping.Keys.Concat(request.ManifestMapping.Values).Distinct()],
                    ManifestMapping = request.ManifestMapping,
                    Success = !criticalFailureOccurred,
                    ErrorMessage = criticalFailureOccurred ? string.Join("; ", warnings) : null,
                    Duration = stopwatch.Elapsed,
                    Metadata = new Dictionary<string, string>
                    {
                        ["profilesUpdated"] = profilesUpdated.ToString(),
                        ["workspacesInvalidated"] = workspacesInvalidated.ToString(),
                        ["manifestsRemoved"] = manifestsRemoved.ToString(),
                        ["casObjectsCollected"] = casObjectsCollected.ToString(),
                        ["bytesFreed"] = bytesFreed.ToString(),
                    },
                },
                cancellationToken);

            // Publish completion event
            WeakReferenceMessenger.Default.Send(new ReconciliationCompletedEvent(
                operationId,
                "ContentReplacement",
                profilesUpdated,
                manifestsRemoved,
                !criticalFailureOccurred,
                criticalFailureOccurred ? string.Join("; ", warnings) : null,
                stopwatch.Elapsed));

            logger.LogInformation(
                "[Orchestrator:{OpId}] Content replacement completed: {Profiles} profiles, {Manifests} manifests, {Objects} CAS objects, {Bytes} bytes freed",
                operationId,
                profilesUpdated,
                manifestsRemoved,
                casObjectsCollected,
                bytesFreed);

            if (criticalFailureOccurred)
            {
                return OperationResult<ContentReplacementResult>.CreateFailure(
                    $"Content replacement completed with critical errors: {string.Join("; ", warnings)}", result, TimeSpan.Zero);
            }

            return OperationResult<ContentReplacementResult>.CreateSuccess(result);
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            logger.LogError(ex, "[Orchestrator:{OpId}] Content replacement failed", operationId);

            await auditLog.LogOperationAsync(
                new ReconciliationAuditEntry
                {
                    OperationId = operationId,
                    OperationType = ReconciliationOperationType.ManifestReplacement,
                    Timestamp = DateTime.UtcNow,
                    Source = request.Source,
                    AffectedManifestIds = [.. request.ManifestMapping.Keys],
                    ManifestMapping = request.ManifestMapping,
                    Success = false,
                    ErrorMessage = ex.Message,
                    Duration = stopwatch.Elapsed,
                },
                cancellationToken);

            // Publish failure event
            WeakReferenceMessenger.Default.Send(new ReconciliationCompletedEvent(
                operationId,
                "ContentReplacement",
                0,
                0,
                false,
                ex.Message,
                stopwatch.Elapsed));

            return OperationResult<ContentReplacementResult>.CreateFailure($"Content replacement failed: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<ContentRemovalResult>> ExecuteContentRemovalAsync(
        IEnumerable<string> manifestIds,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var operationId = Guid.NewGuid().ToString("N")[..ReconciliationConstants.OperationIdLength];
        var ids = manifestIds.ToList();

        logger.LogInformation(
            "[Orchestrator:{OpId}] Starting content removal for {Count} manifests",
            operationId,
            ids.Count);

        try
        {
            bool criticalFailureOccurred = false;

            // STEP 1: Update profiles to remove manifest references
            int profilesUpdated = 0;
            int invalidatedWorkspacesCount = 0;
            foreach (var manifestId in ids)
            {
                var reconcileResult = await reconciliationService.ReconcileManifestRemovalAsync(ManifestId.Create(manifestId), skipUntrack: false, cancellationToken);
                if (reconcileResult.Success && reconcileResult.Data != null)
                {
                    profilesUpdated += reconcileResult.Data.ProfilesUpdated;
                    invalidatedWorkspacesCount += reconcileResult.Data.WorkspacesInvalidated;
                }
                else
                {
                     logger.LogWarning("[Orchestrator:{OpId}] ReconcileManifestRemoval failed for {ManifestId}: {Error}", operationId, manifestId, reconcileResult.FirstError);

                     // We don't abort here, but track it so we don't do unsafe optimized cleanup later
                     // If profiles failed to update, they might still reference the content
                     criticalFailureOccurred = true;
                }
            }

            // STEP 2: Untrack all manifests (MUST happen before GC)
            var untrackResult = await casLifecycleManager.UntrackManifestsAsync(ids, cancellationToken);
            if (!untrackResult.Success)
            {
                logger.LogError("[Orchestrator:{OpId}] Bulk untracking failed entirely: {Error}", operationId, untrackResult.FirstError);
                criticalFailureOccurred = true;
            }
            else if (untrackResult.Data != null)
            {
                var stats = untrackResult.Data;
                if (stats.Errors.Count > 0)
                {
                    logger.LogWarning("[Orchestrator:{OpId}] Bulk untracking had {ErrorCount} errors", operationId, stats.Errors.Count);
                    criticalFailureOccurred = true;
                }

                if (stats.Untracked < ids.Count)
                {
                    logger.LogWarning("[Orchestrator:{OpId}] Bulk untracking partial success: {Count}/{Total}", operationId, stats.Untracked, ids.Count);
                    criticalFailureOccurred = true;
                }
            }

            foreach (var manifestId in ids)
            {
                WeakReferenceMessenger.Default.Send(new ContentRemovingEvent(manifestId, null, "Removal"));
            }

            // STEP 3: Remove manifest files from pool
            int manifestsRemoved = 0;
            if (criticalFailureOccurred)
            {
                logger.LogWarning("[Orchestrator:{OpId}] Skipping manifest removal step due to previous errors", operationId);
            }
            else
            {
                foreach (var id in ids)
                {
                    try
                    {
                        // Use helper for optimized removal
                        // We use skipUntrack: true because we are sure bulk untrack succeeded (criticalFailureOccurred is false)
                        var removeResult = await RemoveManifestWithOptimizedUntrackingAsync(id, skipUntrack: true, cancellationToken);
                        if (removeResult.Success)
                        {
                            manifestsRemoved++;
                        }
                        else
                        {
                            logger.LogWarning("[Orchestrator:{OpId}] Failed to remove manifest {ManifestId}: {Error}", operationId, id, removeResult.FirstError);
                            criticalFailureOccurred = true;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        logger.LogInformation("[Orchestrator:{OpId}] Content removal cancelled", operationId);
                        throw;
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning("[Orchestrator:{OpId}] Failed to remove manifest {ManifestId} due to exception: {Error}", operationId, id, ex.Message);
                        criticalFailureOccurred = true;
                    }
                }
            }

            // STEP 4: Run GC
            int casObjectsCollected = 0;
            long bytesFreed = 0;
            if (criticalFailureOccurred)
            {
                logger.LogWarning("[Orchestrator:{OpId}] Skipping GC due to previous manifest removal failures", operationId);
            }
            else
            {
                logger.LogDebug("[Orchestrator:{OpId}] Step 4: Running garbage collection", operationId);
                var gcResult = await casLifecycleManager.RunGarbageCollectionAsync(force: false, cancellationToken: cancellationToken);
                if (gcResult.Success && gcResult.Data != null)
                {
                    casObjectsCollected = gcResult.Data.ObjectsDeleted;
                    bytesFreed = gcResult.Data.BytesFreed;
                }
            }

            stopwatch.Stop();

            var result = new ContentRemovalResult
            {
                ProfilesUpdated = profilesUpdated,
                WorkspacesInvalidated = invalidatedWorkspacesCount,
                ManifestsRemoved = manifestsRemoved,
                CasObjectsCollected = casObjectsCollected,
                BytesFreed = bytesFreed,
                Duration = stopwatch.Elapsed,
            };

            await auditLog.LogOperationAsync(
                new ReconciliationAuditEntry
                {
                    OperationId = operationId,
                    OperationType = ReconciliationOperationType.ManifestRemoval,
                    Timestamp = DateTime.UtcNow,
                    AffectedManifestIds = ids,
                    Success = !criticalFailureOccurred,
                    ErrorMessage = criticalFailureOccurred ? "One or more manifests failed to untrack or be removed from the pool." : null,
                    Metadata = new Dictionary<string, string>
                    {
                        ["profilesUpdated"] = profilesUpdated.ToString(),
                        ["workspacesInvalidated"] = invalidatedWorkspacesCount.ToString(),
                        ["manifestsRemoved"] = manifestsRemoved.ToString(),
                        ["casObjectsCollected"] = casObjectsCollected.ToString(),
                        ["bytesFreed"] = bytesFreed.ToString(),
                    },
                    Duration = stopwatch.Elapsed,
                },
                cancellationToken);

            logger.LogInformation(
                "[Orchestrator:{OpId}] Content removal completed: {Profiles} profiles, {Manifests} manifests removed. (Critical failure={Failed})",
                operationId,
                profilesUpdated,
                manifestsRemoved,
                criticalFailureOccurred);

            if (criticalFailureOccurred)
            {
                return OperationResult<ContentRemovalResult>.CreateFailure(
                    "Content removal completed with critical errors. See audit log for details.", result, TimeSpan.Zero);
            }

            return OperationResult<ContentRemovalResult>.CreateSuccess(result);
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            logger.LogInformation("[Orchestrator:{OpId}] Content removal cancelled", operationId);
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            logger.LogError(ex, "[Orchestrator:{OpId}] Content removal failed", operationId);

            await auditLog.LogOperationAsync(
                new ReconciliationAuditEntry
                {
                    OperationId = operationId,
                    OperationType = ReconciliationOperationType.ManifestRemoval,
                    Timestamp = DateTime.UtcNow,
                    AffectedManifestIds = ids,
                    Success = false,
                    ErrorMessage = $"Unexpected failure during content removal: {ex.Message}",
                    Duration = stopwatch.Elapsed,
                },
                cancellationToken);

            // Publish failure event
            WeakReferenceMessenger.Default.Send(new ReconciliationCompletedEvent(
                operationId,
                "ContentRemoval",
                0,
                0,
                false,
                ex.Message,
                stopwatch.Elapsed));

            return OperationResult<ContentRemovalResult>.CreateFailure($"Content removal failed: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<ContentUpdateResult>> ExecuteContentUpdateAsync(
        string oldManifestId,
        ContentManifest newManifest,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var operationId = Guid.NewGuid().ToString("N")[..ReconciliationConstants.OperationIdLength];
        var newId = newManifest.Id.Value;
        var idChanged = !string.Equals(oldManifestId, newId, StringComparison.OrdinalIgnoreCase);

        logger.LogInformation(
            "[Orchestrator:{OpId}] Starting local content update: {OldId} → {NewId} (idChanged={Changed})",
            operationId,
            oldManifestId,
            newId,
            idChanged);

        try
        {
            // Delegate to existing orchestration logic which has correct ordering
            var result = await reconciliationService.OrchestrateLocalUpdateAsync(
                oldManifestId,
                newManifest,
                cancellationToken);

            stopwatch.Stop();

            var success = result.Success && result.Data != null;
            var profilesUpdated = result.Data?.ProfilesUpdated ?? 0;
            var workspacesInvalidated = result.Data?.WorkspacesInvalidated ?? 0;
            var errorMessage = result.Success ? null : (result.FirstError ?? "Update failed");

            await auditLog.LogOperationAsync(
                new ReconciliationAuditEntry
                {
                    OperationId = operationId,
                    OperationType = ReconciliationOperationType.LocalContentUpdate,
                    Timestamp = DateTime.UtcNow,
                    AffectedManifestIds = [oldManifestId, newId],
                    ManifestMapping = new Dictionary<string, string> { [oldManifestId] = newId },
                    Success = success,
                    ErrorMessage = errorMessage,
                    Metadata = new Dictionary<string, string>
                    {
                        ["idChanged"] = idChanged.ToString(),
                        ["profilesUpdated"] = profilesUpdated.ToString(),
                        ["workspacesInvalidated"] = workspacesInvalidated.ToString(),
                        ["durationMs"] = stopwatch.ElapsedMilliseconds.ToString(),
                        ["operationType"] = "ContentUpdate",
                        ["result"] = success ? "Success" : "Failure",
                    },
                    Duration = stopwatch.Elapsed,
                },
                cancellationToken);

            // Publish completion event for local update
            WeakReferenceMessenger.Default.Send(new ReconciliationCompletedEvent(
                operationId,
                "LocalContentUpdate",
                profilesUpdated,
                idChanged ? 1 : 0,
                success,
                errorMessage,
                stopwatch.Elapsed));

            if (!success || result.Data == null)
            {
                logger.LogWarning("[Orchestrator:{OpId}] Local content update failed: {Error}", operationId, errorMessage);
                return OperationResult<ContentUpdateResult>.CreateFailure(errorMessage ?? "Update failed");
            }

            var updateResult = result.Data with { Duration = stopwatch.Elapsed };

            logger.LogInformation(
                "[Orchestrator:{OpId}] Local content update completed in {Duration}ms ({Profiles} profiles, {Workspaces} workspaces invalidated)",
                operationId,
                stopwatch.ElapsedMilliseconds,
                updateResult.ProfilesUpdated,
                updateResult.WorkspacesInvalidated);

            return OperationResult<ContentUpdateResult>.CreateSuccess(updateResult);
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            logger.LogInformation("[Orchestrator:{OpId}] Content update cancelled", operationId);
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            logger.LogError(ex, "[Orchestrator:{OpId}] Local content update failed", operationId);

            await auditLog.LogOperationAsync(
                new ReconciliationAuditEntry
                {
                    OperationId = operationId,
                    OperationType = ReconciliationOperationType.LocalContentUpdate,
                    Timestamp = DateTime.UtcNow,
                    AffectedManifestIds = [oldManifestId, newId],
                    ManifestMapping = new Dictionary<string, string> { [oldManifestId] = newId },
                    Success = false,
                    ErrorMessage = $"Update failed: {ex.Message}",
                    Duration = stopwatch.Elapsed,
                },
                cancellationToken);

            // Publish completion event for local update
            WeakReferenceMessenger.Default.Send(new ReconciliationCompletedEvent(
                operationId,
                "LocalContentUpdate",
                0,
                idChanged ? 1 : 0,
                false,
                $"Update failed: {ex.Message}",
                stopwatch.Elapsed));

            return OperationResult<ContentUpdateResult>.CreateFailure($"Update failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Helper method for manifest removal that respects the optimized skipUntrack pattern.
    /// Used when bulk untracking was already performed successfully.
    /// </summary>
    /// <param name="manifestId">The ID of the manifest to remove.</param>
    /// <param name="skipUntrack">Whether to skip the untracking step in the storage layer.
    /// Should be true if bulk untracking was already performed to avoid redundant I/O.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A result indicating success or failure.</returns>
    private async Task<OperationResult<bool>> RemoveManifestWithOptimizedUntrackingAsync(
        string manifestId,
        bool skipUntrack,
        CancellationToken cancellationToken)
    {
        try
        {
            var id = ManifestId.Create(manifestId);
            var result = await manifestPool.RemoveManifestAsync(id, skipUntrack, cancellationToken);
            if (!result.Success)
            {
                return OperationResult<bool>.CreateFailure($"Failed to remove manifest {manifestId}: {result.FirstError}");
            }

            return OperationResult<bool>.CreateSuccess(true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return OperationResult<bool>.CreateFailure($"Error removing manifest {manifestId}: {ex.Message}");
        }
    }
}
