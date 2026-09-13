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
public class InstallationConflictService : IInstallationConflictService
{
    private const string ConflictCheckErrorMessage = "Error checking for installation location conflicts.";
    private const string RemoveMarkerErrorMessage = "Failed to remove adoption marker file at {MarkerPath}";

    private readonly IInstallationLocationTracker _installationLocationTracker;
    private readonly INotificationService? _notificationService;
    private readonly IUserSettingsService? _userSettingsService;
    private readonly ILogger<InstallationConflictService>? _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="InstallationConflictService"/> class.
    /// </summary>
    /// <param name="installationLocationTracker">The installation location tracker.</param>
    /// <param name="notificationService">Optional notification service to alert the user about detected conflicts.</param>
    /// <param name="userSettingsService">Optional user settings service to reload settings after adoption.</param>
    /// <param name="logger">Optional logger for diagnostics.</param>
    public InstallationConflictService(
        IInstallationLocationTracker installationLocationTracker,
        INotificationService? notificationService = null,
        IUserSettingsService? userSettingsService = null,
        ILogger<InstallationConflictService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(installationLocationTracker);
        _installationLocationTracker = installationLocationTracker;
        _notificationService = notificationService;
        _userSettingsService = userSettingsService;
        _logger = logger;
    }

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
                _installationLocationTracker.RecordInstallLocation();
                StorageMigrationService.CleanOrphanedDefaultAppDataIfCustom();
                return;
            }

            var customPath = _installationLocationTracker.GetRegisteredCustomInstallPath();
            if (StorageMigrationService.HasDuplicateInstallationConflict(customPath, out var detectedCustomPath) &&
                !string.IsNullOrWhiteSpace(detectedCustomPath))
            {
                ResolveDuplicateInstallationConflict(detectedCustomPath, markerPath, defaultRoot, cancellationToken);
            }
            else
            {
                // No conflict detected; clean up any orphaned marker
                ClearAdoptionMarker(markerPath);
            }
        }
        catch (IOException ex)
        {
            _logger?.LogWarning(ex, ConflictCheckErrorMessage);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger?.LogWarning(ex, ConflictCheckErrorMessage);
        }
        catch (SecurityException ex)
        {
            _logger?.LogWarning(ex, ConflictCheckErrorMessage);
        }
        catch (ArgumentException ex)
        {
            _logger?.LogWarning(ex, ConflictCheckErrorMessage);
        }
        catch (InvalidOperationException ex)
        {
            _logger?.LogWarning(ex, ConflictCheckErrorMessage);
        }
    }

    private void ResolveDuplicateInstallationConflict(
        string detectedCustomPath,
        string markerPath,
        string defaultRoot,
        CancellationToken cancellationToken)
    {
        _logger?.LogWarning(
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
            _logger?.LogWarning(
                "Aborting user configuration adoption because writing adoption marker failed: {MarkerPath}",
                markerPath);

            if (StorageMigrationService.WasEarlyAdopted)
            {
                imported = true;
                _userSettingsService?.Reload();
                TryFinalizeAdoptionCleanup(detectedCustomPath, defaultRoot, markerPath);
                return true;
            }

            return false;
        }

        _logger?.LogInformation(
            "Adopting user configuration from previous custom installation '{CustomLocation}' into '{DefaultLocation}'",
            detectedCustomPath,
            defaultRoot);

        imported = StorageMigrationService.TryImportUserDataFromCustomInstall(detectedCustomPath, defaultRoot, _logger, cancellationToken) ||
                   StorageMigrationService.WasEarlyAdopted;
        cancellationToken.ThrowIfCancellationRequested();

        if (imported)
        {
            _userSettingsService?.Reload();
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
            _userSettingsService?.Reload();
            TryFinalizeAdoptionCleanup(detectedCustomPath, defaultRoot, markerPath);
        }
        else
        {
            ClearAdoptionMarker(markerPath);
            _installationLocationTracker.ClearCustomInstallPath();
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
            _installationLocationTracker.ClearCustomInstallPath();
        }
    }

    private bool SetAdoptionMarker(string markerPath, string customPath)
    {
        return StorageMigrationService.WriteAdoptionMarkerSafely(markerPath, customPath, _logger);
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
            _logger?.LogWarning(ex, RemoveMarkerErrorMessage, markerPath);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger?.LogWarning(ex, RemoveMarkerErrorMessage, markerPath);
        }
        catch (SecurityException ex)
        {
            _logger?.LogWarning(ex, RemoveMarkerErrorMessage, markerPath);
        }
        catch (ArgumentException ex)
        {
            _logger?.LogWarning(ex, RemoveMarkerErrorMessage, markerPath);
        }
    }

    private void NotifyDuplicateInstallationConflict(string customPath, bool imported)
    {
        if (_notificationService == null)
        {
            return;
        }

        var message = imported
            ? string.Format(CultureInfo.InvariantCulture, StorageMigrationConstants.DuplicateInstallationAdoptedMessageFormat, customPath)
            : string.Format(CultureInfo.InvariantCulture, StorageMigrationConstants.DuplicateInstallationDetectedMessageFormat, customPath);

        _notificationService.ShowWarning(
            StorageMigrationConstants.DuplicateInstallationDetectedTitle,
            message,
            StorageMigrationConstants.DuplicateInstallationNotificationAutoDismissMs,
            showInBadge: true);
    }
}
