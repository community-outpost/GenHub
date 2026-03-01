using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Features.Storage.Services;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Content.Services;

/// <summary>
/// Core implementation of the unified content reconciliation service.
/// </summary>
public class ContentReconciliationService(
    IGameProfileManager profileManager,
    IWorkspaceManager workspaceManager,
    IContentManifestPool manifestPool,
    ICasReferenceTracker referenceTracker,
    ICasService casService,
    ILogger<ContentReconciliationService> logger) : IContentReconciliationService, IDisposable
{
    private readonly SemaphoreSlim _reconciliationLock = new(1, 1);

    /// <inheritdoc />
    public Task<OperationResult<ReconciliationResult>> ReconcileManifestReplacementAsync(
        ManifestId oldId,
        ContentManifest newManifest,
        CancellationToken cancellationToken = default)
    {
        var replacements = new Dictionary<string, ContentManifest>(StringComparer.OrdinalIgnoreCase) { { oldId.Value, newManifest } };
        return ReconcileBulkManifestReplacementAsync(replacements, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<OperationResult<ReconciliationResult>> ReconcileBulkManifestReplacementAsync(
        IReadOnlyDictionary<string, ContentManifest> replacements,
        CancellationToken cancellationToken = default)
    {
        if (replacements == null || replacements.Count == 0)
        {
            return OperationResult<ReconciliationResult>.CreateSuccess(ReconciliationResult.Empty);
        }

        await _reconciliationLock.WaitAsync(cancellationToken);
        try
        {
            return await ReconcileBulkManifestReplacementInternalAsync(replacements, cancellationToken);
        }
        finally
        {
            _reconciliationLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<ReconciliationResult>> ReconcileManifestRemovalAsync(
        ManifestId manifestId,
        bool skipUntrack = false,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Reconciling: Removing manifest '{Id}' from all profiles", manifestId);

        await _reconciliationLock.WaitAsync(cancellationToken);
        try
        {
            var result = await ReconcileManifestRemovalInternalAsync(manifestId, cancellationToken);

            if (result.Success && !skipUntrack)
            {
                logger.LogInformation("Untracking CAS references for manifest '{ManifestId}'", manifestId.Value);
                var untrackResult = await referenceTracker.UntrackManifestAsync(manifestId.Value, cancellationToken);
                if (!untrackResult.Success)
                {
                    logger.LogError("Failed to untrack CAS references for manifest '{ManifestId}': {Error}", manifestId.Value, untrackResult.FirstError);
                    return OperationResult<ReconciliationResult>.CreateFailure($"Failed to untrack CAS references for {manifestId.Value}");
                }
            }

            return result;
        }
        finally
        {
            _reconciliationLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<ContentUpdateResult>> OrchestrateLocalUpdateAsync(
        string? oldId,
        ContentManifest newManifest,
        CancellationToken cancellationToken = default)
    {
        string newId = newManifest.Id.Value;
        bool idChanged = !string.IsNullOrEmpty(oldId) && !string.Equals(oldId, newId, StringComparison.OrdinalIgnoreCase);

        var stopwatch = Stopwatch.StartNew();

        await _reconciliationLock.WaitAsync(cancellationToken);
        try
        {
            // 1. Track new manifest CAS references FIRST (before any workspace invalidation)
            // This ensures CAS objects are tracked before workspace rebuild attempts to use them
            var trackResult = await referenceTracker.TrackManifestReferencesAsync(newId, newManifest, cancellationToken);
            if (!trackResult.Success)
            {
                return OperationResult<ContentUpdateResult>.CreateFailure($"Failed to track CAS references: {trackResult.FirstError}");
            }

            logger.LogDebug("Tracked CAS references for manifest '{ManifestId}'", newId);

            // 2. Reconcile Profiles
            int profilesUpdated = 0;
            int workspacesInvalidated = 0;

            if (idChanged)
            {
                // Ensure the new manifest is available in the pool before attempting reconciliation
                // This prevents race conditions where GetManifestAsync fails to find the just-created manifest
                var addResult = await manifestPool.AddManifestAsync(newManifest, cancellationToken);
                if (!addResult.Success)
                {
                    return OperationResult<ContentUpdateResult>.CreateFailure($"Failed to add new manifest to pool: {addResult.FirstError}");
                }

                var reconcileResult = await ReconcileBulkManifestReplacementInternalAsync(new Dictionary<string, ContentManifest> { { oldId!, newManifest } }, cancellationToken);
                if (!reconcileResult.Success)
                {
                    return OperationResult<ContentUpdateResult>.CreateFailure($"Reconciliation failed: {reconcileResult.FirstError}");
                }

                profilesUpdated = reconcileResult.Data!.ProfilesUpdated;
                workspacesInvalidated = reconcileResult.Data!.WorkspacesInvalidated;
            }
            else
            {
                // Even if ID is same, content might have changed (files removed/added).
                // We clear workspaces to ensure deltas are applied at launch.
                // This is safe because we've already tracked the new CAS references above.
                var reconcileResult = await InvalidateWorkspacesForManifestInternalAsync(newId, cancellationToken);
                profilesUpdated = reconcileResult.ProfilesUpdated;
                workspacesInvalidated = reconcileResult.WorkspacesInvalidated;
            }

            // 3. Untrack old manifest if ID changed
            if (idChanged)
            {
                logger.LogInformation("Untracking old manifest references for '{OldId}'", oldId);
                var untrackResult = await referenceTracker.UntrackManifestAsync(oldId!, cancellationToken);

                if (untrackResult.Success)
                {
                    // 4. Remove Old Manifest from pool
                    // We can skip untrack here because we just did it above
                    await manifestPool.RemoveManifestAsync(ManifestId.Create(oldId!), skipUntrack: true, cancellationToken);
                }
                else
                {
                    logger.LogWarning("Failed to untrack references for old manifest '{OldId}'. Skipping removal from pool. Error: {Error}", oldId, untrackResult.FirstError);
                }
            }

            stopwatch.Stop();
            var updateResult = new ContentUpdateResult
            {
                IdChanged = idChanged,
                ProfilesUpdated = profilesUpdated,
                WorkspacesInvalidated = workspacesInvalidated,
                Duration = stopwatch.Elapsed,
            };

            return OperationResult<ContentUpdateResult>.CreateSuccess(updateResult);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to orchestrate local content update for '{OldId}'", oldId);
            return OperationResult<ContentUpdateResult>.CreateFailure($"Orchestration failed: {ex.Message}");
        }
        finally
        {
            _reconciliationLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<ReconciliationResult>> OrchestrateBulkUpdateAsync(
        IReadOnlyDictionary<string, string> replacements,
        bool removeOld = true,
        CancellationToken cancellationToken = default)
    {
        if (replacements == null || replacements.Count == 0)
        {
            return OperationResult<ReconciliationResult>.CreateSuccess(ReconciliationResult.Empty);
        }

        await _reconciliationLock.WaitAsync(cancellationToken);
        try
        {
            // Resolve string IDs to manifests for reconciliation
            var manifestReplacements = new Dictionary<string, ContentManifest>(StringComparer.OrdinalIgnoreCase);
            foreach (var replacement in replacements)
            {
                var manifestResult = await manifestPool.GetManifestAsync(ManifestId.Create(replacement.Value), cancellationToken);
                if (manifestResult.Success && manifestResult.Data != null)
                {
                    manifestReplacements[replacement.Key] = manifestResult.Data;
                }
                else
                {
                    logger.LogWarning("Skipping bulk update for manifest '{OldId}' -> '{NewId}' because new manifest could not be resolved.", replacement.Key, replacement.Value);
                }
            }

            // 1. Reconcile Profiles (Apply replacements globally)
            var reconcileResult = await ReconcileBulkManifestReplacementInternalAsync(manifestReplacements, cancellationToken);
            if (!reconcileResult.Success)
            {
                return reconcileResult;
            }

            if (removeOld)
            {
                // 2. Untrack old CAS references only for resolved replacements
                foreach (var oldId in manifestReplacements.Keys)
                {
                    logger.LogInformation("Untracking stale CAS references for manifest '{ManifestId}'", oldId);
                    await referenceTracker.UntrackManifestAsync(oldId, cancellationToken);
                }

                // 3. Remove old manifests from pool only for resolved replacements
                foreach (var oldId in manifestReplacements.Keys)
                {
                    logger.LogInformation("Removing stale manifest from pool: '{ManifestId}'", oldId);
                    await manifestPool.RemoveManifestAsync(ManifestId.Create(oldId), skipUntrack: true, cancellationToken: cancellationToken);
                }
            }

            return reconcileResult;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Bulk update orchestration failed");
            return OperationResult<ReconciliationResult>.CreateFailure($"Bulk update orchestration failed: {ex.Message}");
        }
        finally
        {
            _reconciliationLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<ReconciliationResult>> OrchestrateBulkRemovalAsync(
        IEnumerable<ManifestId> manifestIds,
        CancellationToken cancellationToken = default)
    {
        if (manifestIds == null)
        {
            return OperationResult<ReconciliationResult>.CreateSuccess(ReconciliationResult.Empty);
        }

        await _reconciliationLock.WaitAsync(cancellationToken);
        try
        {
            var totalResult = ReconciliationResult.Empty;
            var failedManifests = new List<string>();

            foreach (var manifestId in manifestIds)
            {
                // 1. Reconcile Profiles (Remove manifest references)
                var reconcileResult = await ReconcileManifestRemovalInternalAsync(manifestId, cancellationToken);
                if (reconcileResult.Success)
                {
                    totalResult += reconcileResult.Data!;

                    // 2. Untrack CAS references
                    logger.LogInformation("Untracking CAS references for removed manifest '{ManifestId}'", manifestId.Value);
                    await referenceTracker.UntrackManifestAsync(manifestId.Value, cancellationToken);

                    // 3. Remove from manifest pool
                    await manifestPool.RemoveManifestAsync(manifestId, skipUntrack: true, cancellationToken: cancellationToken);
                }
                else
                {
                    logger.LogWarning("Skipping removal of manifest '{ManifestId}' because profile reconciliation failed: {Error}", manifestId.Value, reconcileResult.FirstError);
                    failedManifests.Add(manifestId.Value);
                }
            }

            // Return success even with partial failures to allow cleanup of old manifests.
            // Failed manifests are logged for visibility.
            return OperationResult<ReconciliationResult>.CreateSuccess(totalResult);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Bulk removal orchestration failed");
            return OperationResult<ReconciliationResult>.CreateFailure($"Bulk removal orchestration failed: {ex.Message}");
        }
        finally
        {
            _reconciliationLock.Release();
        }
    }

    /// <summary>
    /// Schedules garbage collection. Should be called AFTER all untrack operations complete.
    /// </summary>
    /// <param name="force">If set to true, forces garbage collection even if not strictly needed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result of the operation.</returns>
    public Task<OperationResult> ScheduleGarbageCollectionAsync(
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(
            async () =>
            {
                try
                {
                    await casService.RunGarbageCollectionAsync(force, cancellationToken);
                    return OperationResult.CreateSuccess();
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Scheduled garbage collection failed");
                    return OperationResult.CreateFailure($"GC failed: {ex.Message}");
                }
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _reconciliationLock.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<ReconciliationResult> InvalidateWorkspacesForManifestInternalAsync(string manifestId, CancellationToken cancellationToken)
    {
        var profilesResult = await profileManager.GetAllProfilesAsync(cancellationToken);
        if (!profilesResult.Success) return ReconciliationResult.Empty;

        var affectedProfiles = profilesResult.Data?.Where(p =>
            p.EnabledContentIds?.Contains(manifestId, StringComparer.OrdinalIgnoreCase) == true &&
            !string.IsNullOrEmpty(p.ActiveWorkspaceId)).ToList() ?? [];

        int invalidatedCount = 0;
        foreach (var profile in affectedProfiles)
        {
            logger.LogDebug("Invalidating workspace for profile '{ProfileName}' due to manifest update", profile.Name);
            await workspaceManager.CleanupWorkspaceAsync(profile.ActiveWorkspaceId!, cancellationToken);
            var updateResult = await profileManager.UpdateProfileAsync(profile.Id, new UpdateProfileRequest { ActiveWorkspaceId = string.Empty }, cancellationToken);

            if (updateResult.Success)
            {
                await NotifyProfileUpdatedAsync(profile.Id, cancellationToken);
                invalidatedCount++;
            }
            else
            {
                logger.LogWarning("Failed to clear ActiveWorkspaceId for profile '{ProfileName}': {Error}", profile.Name, updateResult.FirstError);

                // Mark as invalidated anyway as we did CleanupWorkspaceAsync, but profile state might be stale
                invalidatedCount++;
            }
        }

        return new ReconciliationResult(invalidatedCount, invalidatedCount);
    }

    private async Task NotifyProfileUpdatedAsync(string profileId, CancellationToken cancellationToken)
    {
        try
        {
            var result = await profileManager.GetProfileAsync(profileId, cancellationToken);
            if (result.Success && result.Data is GameProfile updatedProfile)
            {
                WeakReferenceMessenger.Default.Send(new Core.Models.GameProfile.ProfileUpdatedMessage(updatedProfile));
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to notify profile update for '{ProfileId}'", profileId);
        }
    }

    private async Task<OperationResult<ReconciliationResult>> ReconcileBulkManifestReplacementInternalAsync(
        IReadOnlyDictionary<string, ContentManifest> replacements,
        CancellationToken cancellationToken = default)
    {
        var oldIds = replacements.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        logger.LogInformation("Reconciling: Performing bulk replacement of {Count} manifests in all profiles", replacements.Count);

        var profilesResult = await profileManager.GetAllProfilesAsync(cancellationToken);
        if (!profilesResult.Success)
        {
            return OperationResult<ReconciliationResult>.CreateFailure($"Failed to retrieve profiles: {profilesResult.FirstError}");
        }

        var affectedProfiles = profilesResult.Data?.Where(p =>
            (p.EnabledContentIds?.Any(id => oldIds.Contains(id)) == true) ||
            (p.GameClient != null && oldIds.Contains(p.GameClient.Id))).ToList() ?? [];

        if (affectedProfiles.Count == 0)
        {
            logger.LogInformation("No profiles referenced affected manifests for bulk reconciliation");
            return OperationResult<ReconciliationResult>.CreateSuccess(ReconciliationResult.Empty);
        }

        logger.LogInformation("Found {Count} affected profiles for bulk reconciliation", affectedProfiles.Count);

        int updatedProfilesCount = 0;
        int invalidatedWorkspacesCount = 0;
        var failedProfiles = new List<string>();

        foreach (var profile in affectedProfiles)
        {
            try
            {
                var newContentIds = profile.EnabledContentIds!
                    .Select(id => replacements.TryGetValue(id, out var newManifest) ? newManifest.Id.Value : id)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                GameClient? newGameClient = null;
                if (profile.GameClient != null)
                {
                    if (replacements.TryGetValue(profile.GameClient.Id, out var m))
                    {
                        newGameClient = new GameClient
                        {
                            Id = m.Id.Value,
                            Name = m.Name,
                            Version = m.Version ?? string.Empty,
                            GameType = m.TargetGame,
                            SourceType = m.ContentType,
                            PublisherType = m.Publisher?.PublisherType,
                            InstallationId = profile.GameClient.InstallationId, // Preserve installation link
                        };
                    }
                    else
                    {
                        // Preserve existing GameClient if its ID is not in the replacement list
                        newGameClient = profile.GameClient;
                    }
                }

                bool workspaceInvalidated = false;

                // Clear workspace to force launch-time sync
                if (!string.IsNullOrEmpty(profile.ActiveWorkspaceId))
                {
                    logger.LogDebug("Cleaning up workspace '{WorkspaceId}' for stale profile '{ProfileName}'", profile.ActiveWorkspaceId, profile.Name);
                    await workspaceManager.CleanupWorkspaceAsync(profile.ActiveWorkspaceId, cancellationToken);
                    workspaceInvalidated = true;
                }

                var updateRequest = new UpdateProfileRequest
                {
                    EnabledContentIds = newContentIds,
                    GameClient = newGameClient,
                    ActiveWorkspaceId = string.Empty,
                };

                var updateResult = await profileManager.UpdateProfileAsync(profile.Id, updateRequest, cancellationToken);
                if (updateResult.Success)
                {
                    updatedProfilesCount++;
                    if (workspaceInvalidated) invalidatedWorkspacesCount++;
                    await NotifyProfileUpdatedAsync(profile.Id, cancellationToken);
                }
                else
                {
                    var error = $"Failed to update profile '{profile.Name}': {updateResult.FirstError}";
                    logger.LogWarning("Failed to update profile '{ProfileName}': {Error}", profile.Name, updateResult.FirstError);
                    failedProfiles.Add(profile.Name);
                }
            }
            catch (Exception ex)
            {
                var error = $"Error reconciling profile '{profile.Name}': {ex.Message}";
                logger.LogError(ex, "Error reconciling profile '{ProfileName}': {Message}", profile.Name, ex.Message);
                failedProfiles.Add(profile.Name);
            }
        }

        // Return success even with partial failures to allow cleanup of old manifests.
        // Callers can check ProfilesUpdated count vs expected count to detect partial failures.
        // Failed profiles are logged for visibility.
        foreach (var replacement in replacements)
        {
            WeakReferenceMessenger.Default.Send(new ManifestReplacedMessage(replacement.Key, replacement.Value.Id.Value));
        }

        logger.LogInformation("Bulk reconciliation complete. Updated {Count} profiles. {FailedCount} failures.", updatedProfilesCount, failedProfiles.Count);

        return OperationResult<ReconciliationResult>.CreateSuccess(new ReconciliationResult(updatedProfilesCount, invalidatedWorkspacesCount, failedProfiles.Count));
    }

    private async Task<OperationResult<ReconciliationResult>> ReconcileManifestRemovalInternalAsync(
        ManifestId manifestId,
        CancellationToken cancellationToken = default)
    {
        var profilesResult = await profileManager.GetAllProfilesAsync(cancellationToken);
        if (!profilesResult.Success)
        {
            return OperationResult<ReconciliationResult>.CreateFailure($"Failed to retrieve profiles: {profilesResult.FirstError}");
        }

        var affectedProfiles = profilesResult.Data?.Where(p =>
            p.EnabledContentIds?.Contains(manifestId.Value, StringComparer.OrdinalIgnoreCase) == true).ToList() ?? [];

        if (affectedProfiles.Count == 0)
        {
            return OperationResult<ReconciliationResult>.CreateSuccess(ReconciliationResult.Empty);
        }

        int updatedProfilesCount = 0;
        int invalidatedWorkspacesCount = 0;
        var failedProfiles = new List<string>();

        foreach (var profile in affectedProfiles)
        {
            try
            {
                var newContentIds = profile.EnabledContentIds!
                    .Where(id => !id.Equals(manifestId.Value, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                bool workspaceInvalidated = false;
                if (!string.IsNullOrEmpty(profile.ActiveWorkspaceId))
                {
                    logger.LogDebug("Cleaning up workspace '{WorkspaceId}' for deleted content in profile '{ProfileName}'", profile.ActiveWorkspaceId, profile.Name);
                    await workspaceManager.CleanupWorkspaceAsync(profile.ActiveWorkspaceId, cancellationToken);
                    workspaceInvalidated = true;
                }

                var updateRequest = new UpdateProfileRequest
                {
                    EnabledContentIds = newContentIds,
                    ActiveWorkspaceId = string.Empty,
                };

                var updateResult = await profileManager.UpdateProfileAsync(profile.Id, updateRequest, cancellationToken);
                if (updateResult.Success)
                {
                    updatedProfilesCount++;
                    if (workspaceInvalidated) invalidatedWorkspacesCount++;
                    await NotifyProfileUpdatedAsync(profile.Id, cancellationToken);
                }
                else
                {
                    var error = $"Failed to update profile '{profile.Name}': {updateResult.FirstError}";
                    logger.LogWarning("Failed to update profile '{ProfileName}': {Error}", profile.Name, updateResult.FirstError);
                    failedProfiles.Add(profile.Name);
                }
            }
            catch (Exception ex)
            {
                var error = $"Error removing manifest from profile '{profile.Name}': {ex.Message}";
                logger.LogError(ex, "Error removing manifest from profile '{ProfileName}': {Message}", profile.Name, ex.Message);
                failedProfiles.Add(profile.Name);
            }
        }

        // Return success even with partial failures (as results now include failure count) to allow cleanup of old manifests.
        // Failed profiles are logged for visibility.
        return OperationResult<ReconciliationResult>.CreateSuccess(new ReconciliationResult(updatedProfilesCount, invalidatedWorkspacesCount, failedProfiles.Count));
    }
}
