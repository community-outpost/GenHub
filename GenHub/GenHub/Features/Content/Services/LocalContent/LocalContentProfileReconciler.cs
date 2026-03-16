using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Content.Services.LocalContent;

/// <summary>
/// Service for reconciling game profiles when local content is modified.
/// </summary>
public class LocalContentProfileReconciler(
    IContentReconciliationService reconciliationService,
    INotificationService notificationService,
    ILogger<LocalContentProfileReconciler> logger)
    : ILocalContentProfileReconciler
{
    /// <inheritdoc />
    public async Task<OperationResult<int>> ReconcileProfilesAsync(
        string oldManifestId,
        string newManifestId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(oldManifestId))
        {
            return OperationResult<int>.CreateFailure("Cannot reconcile profiles: old manifest ID is required.");
        }

        if (string.IsNullOrWhiteSpace(newManifestId))
        {
            return OperationResult<int>.CreateFailure("Cannot reconcile profiles: new manifest ID is required.");
        }

        try
        {
            // Create a ManifestMapping from the old and new manifest IDs
            var manifestMapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { { oldManifestId, newManifestId } };

            var profileResult = await reconciliationService.OrchestrateBulkUpdateAsync(
                manifestMapping,
                false, // GC handled elsewhere
                cancellationToken);

            if (!profileResult.Success)
            {
                return OperationResult<int>.CreateFailure(profileResult.FirstError ?? "Reconciliation failed");
            }

            int profilesUpdated = profileResult.Data.ProfilesUpdated;

            if (profileResult.Data.FailedProfilesCount > 0)
            {
                logger.LogWarning("Reconciliation partial success: {FailedCount} profiles failed to update for local content change", profileResult.Data.FailedProfilesCount);
            }

            if (profilesUpdated > 0)
            {
                notificationService.ShowInfo(
                    "Profiles Updated",
                    $"Updated {profilesUpdated} profile(s) to use the renamed content.",
                    4000);
            }

            return OperationResult<int>.CreateSuccess(profilesUpdated);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error reconciling profiles for local content update via unified service");
            return OperationResult<int>.CreateFailure($"Reconciliation failed: {ex.Message}");
        }
    }
}
