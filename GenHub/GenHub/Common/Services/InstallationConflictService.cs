using System;
using System.Globalization;
using System.IO;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Storage;
using Microsoft.Extensions.Logging;

namespace GenHub.Common.Services;

/// <summary>
/// Service responsible for detecting and resolving conflicts between default and custom GenHub installations.
/// </summary>
/// <param name="installationLocationTracker">The installation location tracker.</param>
/// <param name="notificationService">Optional notification service to alert the user about detected conflicts.</param>
/// <param name="userSettingsService">Optional user settings service to reload settings after adoption.</param>
/// <param name="logger">Optional logger for diagnostics.</param>
public class InstallationConflictService(
    IInstallationLocationTracker installationLocationTracker,
    INotificationService? notificationService = null,
    IUserSettingsService? userSettingsService = null,
    ILogger<InstallationConflictService>? logger = null) : IInstallationConflictService
{
    private const string ConflictCheckErrorMessage = "Error checking for installation location conflicts.";
    private const string RemoveMarkerErrorMessage = "Failed to remove adoption marker file at {MarkerPath}";

    /// <inheritdoc />
    public Task CheckAndResolveConflictsAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() => ExecuteConflictResolution(cancellationToken), cancellationToken);
    }

    private void ExecuteConflictResolution(CancellationToken cancellationToken)
    {
        var defaultRoot = StorageMigrationService.GetDefaultDataRoot();
        var markerPath = Path.Combine(defaultRoot, StorageMigrationConstants.AdoptionPendingMarkerFileName);

        try
        {
            if (StorageMigrationService.IsCustomInstallRoot())
            {
                installationLocationTracker.RecordInstallLocation();
                StorageMigrationService.CleanOrphanedDefaultAppDataIfCustom();
                return;
            }

            var customPath = installationLocationTracker.GetRegisteredCustomInstallPath();
            string? detectedCustomPath = null;

            if (StorageMigrationService.HasDuplicateInstallationConflict(customPath, out var resolvedTrackerPath) &&
                !string.IsNullOrWhiteSpace(resolvedTrackerPath))
            {
                detectedCustomPath = resolvedTrackerPath;
            }
            else if (File.Exists(markerPath))
            {
                // Never take tracker returning null or current install as proof there is no conflict
                // while .adoption-pending still names a live custom root.
                try
                {
                    var markerContent = File.ReadAllText(markerPath).Trim();
                    if (StorageMigrationService.HasDuplicateInstallationConflict(markerContent, out var resolvedMarkerPath) &&
                        !string.IsNullOrWhiteSpace(resolvedMarkerPath))
                    {
                        detectedCustomPath = resolvedMarkerPath;
                    }
                }
                catch (IOException ex)
                {
                    logger?.LogWarning(ex, "Failed to read adoption pending marker file at {MarkerPath}", markerPath);
                }
                catch (UnauthorizedAccessException ex)
                {
                    logger?.LogWarning(ex, "Failed to read adoption pending marker file at {MarkerPath}", markerPath);
                }
                catch (SecurityException ex)
                {
                    logger?.LogWarning(ex, "Failed to read adoption pending marker file at {MarkerPath}", markerPath);
                }
                catch (ArgumentException ex)
                {
                    logger?.LogWarning(ex, "Failed to read adoption pending marker file at {MarkerPath}", markerPath);
                }
            }

            if (!string.IsNullOrWhiteSpace(detectedCustomPath))
            {
                ResolveDuplicateInstallationConflict(detectedCustomPath, markerPath, defaultRoot, cancellationToken);
            }
            else
            {
                // Only clean up an orphaned marker if there is no live custom root or no remaining unadopted data.
                // Never delete it if HasUnadoptedUserData is true or null.
                TryClearAdoptionMarkerIfSafe(markerPath, defaultRoot);
            }
        }
        catch (IOException ex)
        {
            logger?.LogWarning(ex, ConflictCheckErrorMessage);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger?.LogWarning(ex, ConflictCheckErrorMessage);
        }
        catch (SecurityException ex)
        {
            logger?.LogWarning(ex, ConflictCheckErrorMessage);
        }
        catch (ArgumentException ex)
        {
            logger?.LogWarning(ex, ConflictCheckErrorMessage);
        }
        catch (InvalidOperationException ex)
        {
            logger?.LogWarning(ex, ConflictCheckErrorMessage);
        }
    }

    private void ResolveDuplicateInstallationConflict(
        string detectedCustomPath,
        string markerPath,
        string defaultRoot,
        CancellationToken cancellationToken)
    {
        logger?.LogWarning(
            "Duplicate installation detected: GenHub is running from default location '{DefaultLocation}', " +
            "but an existing custom installation was found at '{CustomLocation}'.",
            defaultRoot,
            detectedCustomPath);

        var isPendingRetry = StorageMigrationService.IsMarkerMatchingPath(markerPath, detectedCustomPath);
        var hasExistingData = StorageMigrationService.HasExistingUserData(defaultRoot);
        var shouldAdopt = (!hasExistingData || isPendingRetry) &&
                          StorageMigrationService.HasExistingUserData(detectedCustomPath);

        if (shouldAdopt)
        {
            if (!TryAdoptUserData(detectedCustomPath, markerPath, defaultRoot, cancellationToken, out var imported))
            {
                NotifyDuplicateInstallationConflict(detectedCustomPath, imported: false);
                return;
            }

            NotifyDuplicateInstallationConflict(detectedCustomPath, imported);
            return;
        }

        HandleNonAdoptedConflict(detectedCustomPath, markerPath, defaultRoot, cancellationToken);
    }

    private bool TryAdoptUserData(
        string detectedCustomPath,
        string markerPath,
        string defaultRoot,
        CancellationToken cancellationToken,
        out bool imported)
    {
        imported = false;
        if (!SetAdoptionMarker(markerPath, detectedCustomPath))
        {
            logger?.LogWarning(
                "Aborting user configuration adoption because writing adoption marker failed: {MarkerPath}",
                markerPath);

            if (StorageMigrationService.WasEarlyAdopted)
            {
                imported = true;
                userSettingsService?.Reload();
                TryFinalizeAdoptionCleanup(detectedCustomPath, defaultRoot, markerPath);
                return true;
            }

            return false;
        }

        logger?.LogInformation(
            "Adopting user configuration from previous custom installation '{CustomLocation}' into '{DefaultLocation}'",
            detectedCustomPath,
            defaultRoot);

        imported = StorageMigrationService.TryImportUserDataFromCustomInstall(detectedCustomPath, defaultRoot, logger, cancellationToken) ||
                   StorageMigrationService.WasEarlyAdopted;
        cancellationToken.ThrowIfCancellationRequested();

        if (imported)
        {
            userSettingsService?.Reload();
        }

        TryFinalizeAdoptionCleanup(detectedCustomPath, defaultRoot, markerPath);
        return true;
    }

    private void HandleNonAdoptedConflict(
        string detectedCustomPath,
        string markerPath,
        string defaultRoot,
        CancellationToken cancellationToken)
    {
        var imported = false;
        if (StorageMigrationService.WasEarlyAdopted)
        {
            imported = true;
            userSettingsService?.Reload();
            TryFinalizeAdoptionCleanup(detectedCustomPath, defaultRoot, markerPath);
        }
        else
        {
            ClearAdoptionMarker(markerPath);
            installationLocationTracker.ClearCustomInstallPath();
        }

        cancellationToken.ThrowIfCancellationRequested();
        NotifyDuplicateInstallationConflict(detectedCustomPath, imported);
    }

    private void TryFinalizeAdoptionCleanup(string detectedCustomPath, string defaultRoot, string markerPath)
    {
        var hasRemainingUnadopted = StorageMigrationService.HasUnadoptedUserData(detectedCustomPath, defaultRoot);
        if (hasRemainingUnadopted == false)
        {
            ClearAdoptionMarker(markerPath);
            installationLocationTracker.ClearCustomInstallPath();
        }
    }

    private bool SetAdoptionMarker(string markerPath, string customPath)
    {
        return StorageMigrationService.WriteAdoptionMarkerSafely(markerPath, customPath, logger);
    }

    private void ClearAdoptionMarker(string markerPath)
    {
        try
        {
            if (File.Exists(markerPath))
            {
                File.Delete(markerPath);
            }
        }
        catch (IOException ex)
        {
            logger?.LogWarning(ex, RemoveMarkerErrorMessage, markerPath);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger?.LogWarning(ex, RemoveMarkerErrorMessage, markerPath);
        }
        catch (SecurityException ex)
        {
            logger?.LogWarning(ex, RemoveMarkerErrorMessage, markerPath);
        }
        catch (ArgumentException ex)
        {
            logger?.LogWarning(ex, RemoveMarkerErrorMessage, markerPath);
        }
    }

    private void TryClearAdoptionMarkerIfSafe(string markerPath, string defaultRoot)
    {
        try
        {
            if (!File.Exists(markerPath))
            {
                return;
            }

            var recorded = File.ReadAllText(markerPath).Trim();
            if (string.IsNullOrWhiteSpace(recorded))
            {
                ClearAdoptionMarker(markerPath);
                return;
            }

            var hasRemainingUnadopted = StorageMigrationService.HasUnadoptedUserData(recorded, defaultRoot);
            if (hasRemainingUnadopted == false)
            {
                ClearAdoptionMarker(markerPath);
                installationLocationTracker.ClearCustomInstallPath();
            }
        }
        catch (IOException ex)
        {
            logger?.LogWarning(ex, RemoveMarkerErrorMessage, markerPath);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger?.LogWarning(ex, RemoveMarkerErrorMessage, markerPath);
        }
        catch (SecurityException ex)
        {
            logger?.LogWarning(ex, RemoveMarkerErrorMessage, markerPath);
        }
        catch (ArgumentException ex)
        {
            logger?.LogWarning(ex, RemoveMarkerErrorMessage, markerPath);
        }
    }

    private void NotifyDuplicateInstallationConflict(string customPath, bool imported)
    {
        if (notificationService == null)
        {
            return;
        }

        var message = imported
            ? string.Format(CultureInfo.InvariantCulture, StorageMigrationConstants.DuplicateInstallationAdoptedMessageFormat, customPath)
            : string.Format(CultureInfo.InvariantCulture, StorageMigrationConstants.DuplicateInstallationDetectedMessageFormat, customPath);

        notificationService.ShowWarning(
            StorageMigrationConstants.DuplicateInstallationDetectedTitle,
            message,
            StorageMigrationConstants.DuplicateInstallationNotificationAutoDismissMs,
            showInBadge: true);
    }
}
