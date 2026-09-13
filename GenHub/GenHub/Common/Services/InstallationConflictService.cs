using System;
using System.IO;
using System.Security;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Storage;
using Microsoft.Extensions.Logging;

namespace GenHub.Common.Services;

/// <summary>
/// Service responsible for detecting and resolving duplicate installation collisions
/// when GenHub is launched from the default installation directory while a custom installation exists.
/// </summary>
/// <param name="installationLocationTracker">The installation tracker for recording and discovering installation paths.</param>
/// <param name="notificationService">Optional UI notification service for alert toasts.</param>
/// <param name="logger">Optional logger for diagnostics.</param>
public class InstallationConflictService(
    IInstallationLocationTracker installationLocationTracker,
    INotificationService? notificationService = null,
    ILogger<InstallationConflictService>? logger = null) : IInstallationConflictService
{
    private readonly IInstallationLocationTracker _tracker = installationLocationTracker ?? throw new ArgumentNullException(nameof(installationLocationTracker));
    private readonly INotificationService? _notificationService = notificationService;
    private readonly ILogger<InstallationConflictService>? _logger = logger;

    /// <inheritdoc />
    public async Task CheckAndResolveConflictsAsync()
    {
        await Task.Run(ExecuteConflictResolution);
    }

    private void ExecuteConflictResolution()
    {
        var defaultRoot = StorageMigrationService.GetDefaultInstallRoot();
        var markerPath = Path.Combine(defaultRoot, StorageMigrationConstants.AdoptionPendingMarkerFileName);

        try
        {
            if (StorageMigrationService.IsCustomInstallRoot())
            {
                _tracker.RecordInstallLocation();
                StorageMigrationService.CleanOrphanedDefaultAppDataIfCustom();
                return;
            }

            var customPath = _tracker.GetRegisteredCustomInstallPath();
            if (StorageMigrationService.HasDuplicateInstallationConflict(customPath, out var detectedCustomPath) &&
                !string.IsNullOrWhiteSpace(detectedCustomPath))
            {
                ResolveDuplicateInstallationConflict(detectedCustomPath, markerPath, defaultRoot);
            }
            else
            {
                // No conflict detected; clean up any orphaned marker
                ClearAdoptionMarker(markerPath);
            }
        }
        catch (IOException ex)
        {
            _logger?.LogWarning(ex, "Error checking for duplicate installation conflict on startup");
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger?.LogWarning(ex, "Error checking for duplicate installation conflict on startup");
        }
        catch (SecurityException ex)
        {
            _logger?.LogWarning(ex, "Error checking for duplicate installation conflict on startup");
        }
        catch (ArgumentException ex)
        {
            _logger?.LogWarning(ex, "Error checking for duplicate installation conflict on startup");
        }
        catch (InvalidOperationException ex)
        {
            _logger?.LogWarning(ex, "Error checking for duplicate installation conflict on startup");
        }
    }

    private void ResolveDuplicateInstallationConflict(string detectedCustomPath, string markerPath, string defaultRoot)
    {
        _logger?.LogWarning(
            "Duplicate installation detected: GenHub is running from default location '{DefaultLocation}', " +
            "but an existing custom installation was found at '{CustomLocation}'.",
            defaultRoot,
            detectedCustomPath);

        var isPendingRetry = IsMarkerMatchingPath(markerPath, detectedCustomPath);
        var hasExistingData = StorageMigrationService.HasExistingUserData(defaultRoot);
        var shouldAdopt = (!hasExistingData || isPendingRetry) &&
                          StorageMigrationService.HasExistingUserData(detectedCustomPath);
        var imported = false;

        if (shouldAdopt)
        {
            SetAdoptionMarker(markerPath, detectedCustomPath);

            _logger?.LogInformation(
                "Adopting user configuration from previous custom installation '{CustomLocation}' into '{DefaultLocation}'",
                detectedCustomPath,
                defaultRoot);

            imported = StorageMigrationService.TryImportUserDataFromCustomInstall(detectedCustomPath, defaultRoot, _logger);
            var hasRemainingUnadopted = StorageMigrationService.HasUnadoptedUserData(detectedCustomPath, defaultRoot);

            if (!hasRemainingUnadopted)
            {
                ClearAdoptionMarker(markerPath);
                _tracker.ClearCustomInstallPath();
            }
        }
        else
        {
            ClearAdoptionMarker(markerPath);
            _tracker.ClearCustomInstallPath();
        }

        NotifyDuplicateInstallationConflict(detectedCustomPath, imported);
    }

    private bool IsMarkerMatchingPath(string markerPath, string customPath)
    {
        try
        {
            if (!File.Exists(markerPath))
            {
                return false;
            }

            var recorded = File.ReadAllText(markerPath).Trim();
            return string.IsNullOrWhiteSpace(recorded) || PathHelper.AreSamePath(recorded, customPath);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (SecurityException)
        {
            return false;
        }
    }

    private void SetAdoptionMarker(string markerPath, string customPath)
    {
        try
        {
            if (!File.Exists(markerPath))
            {
                File.WriteAllText(markerPath, customPath);
            }
        }
        catch (IOException ex)
        {
            _logger?.LogWarning(ex, "Failed to create adoption marker file at {MarkerPath}", markerPath);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger?.LogWarning(ex, "Failed to create adoption marker file at {MarkerPath}", markerPath);
        }
        catch (SecurityException ex)
        {
            _logger?.LogWarning(ex, "Failed to create adoption marker file at {MarkerPath}", markerPath);
        }
        catch (ArgumentException ex)
        {
            _logger?.LogWarning(ex, "Failed to create adoption marker file at {MarkerPath}", markerPath);
        }
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
            _logger?.LogWarning(ex, "Failed to remove adoption marker file at {MarkerPath}", markerPath);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger?.LogWarning(ex, "Failed to remove adoption marker file at {MarkerPath}", markerPath);
        }
        catch (SecurityException ex)
        {
            _logger?.LogWarning(ex, "Failed to remove adoption marker file at {MarkerPath}", markerPath);
        }
        catch (ArgumentException ex)
        {
            _logger?.LogWarning(ex, "Failed to remove adoption marker file at {MarkerPath}", markerPath);
        }
    }

    private void NotifyDuplicateInstallationConflict(string customPath, bool imported)
    {
        if (_notificationService == null)
        {
            return;
        }

        var message = imported
            ? $"GenHub detected a custom installation at '{customPath}'. Your settings, profiles, and game manifests have been preserved in this installation."
            : $"GenHub is running from the default directory, but an existing installation was detected at '{customPath}'.";

        _notificationService.ShowWarning(
            StorageMigrationConstants.DuplicateInstallationDetectedTitle,
            message,
            StorageMigrationConstants.DuplicateInstallationNotificationDismissMs);
    }
}
