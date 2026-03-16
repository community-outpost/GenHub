using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Features.Workspace;
using Microsoft.Extensions.Logging;

namespace GenHub.Common.Services;

/// <summary>
/// Service for resolving dynamic storage locations based on game installations.
/// </summary>
public class StorageLocationService(
    IUserSettingsService userSettingsService,
    IGameInstallationService gameInstallationService,
    ILogger<StorageLocationService> logger) : IStorageLocationService
{
    /// <inheritdoc/>
    public string GetCasPoolPath(IGameInstallation installation)
    {
        ArgumentNullException.ThrowIfNull(installation);

        var settings = userSettingsService.Get();
        if (!settings.UseInstallationAdjacentStorage)
        {
            // Fall back to centralized AppData location
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                AppConstants.AppName,
                DirectoryNames.CasPool);
            logger.LogDebug("Using centralized CAS pool path: {CasPoolPath} (installation-adjacent disabled)", appDataPath);
            return appDataPath;
        }

        var installationRoot = PathHelper.GetSafeParentDirectory(installation.InstallationPath);
        var casPoolPath = Path.Combine(installationRoot, DirectoryNames.GenHubCasPool);
        logger.LogDebug("Resolved CAS pool path: {CasPoolPath} for installation {InstallationId}", casPoolPath, installation.Id);
        return casPoolPath;
    }

    /// <inheritdoc/>
    public string GetWorkspacePath(IGameInstallation installation)
    {
        ArgumentNullException.ThrowIfNull(installation);

        var settings = userSettingsService.Get();
        if (!settings.UseInstallationAdjacentStorage)
        {
            // Fall back to centralized AppData location
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                AppConstants.AppName,
                DirectoryNames.Workspaces);
            logger.LogDebug("Using centralized workspace path: {WorkspacePath} (installation-adjacent disabled)", appDataPath);
            return appDataPath;
        }

        var installationRoot = PathHelper.GetSafeParentDirectory(installation.InstallationPath);
        var workspacePath = Path.Combine(installationRoot, DirectoryNames.GenHubWorkspace);
        logger.LogDebug("Resolved workspace path: {WorkspacePath} for installation {InstallationId}", workspacePath, installation.Id);
        return workspacePath;
    }

    /// <inheritdoc/>
    public async Task<IGameInstallation?> GetPreferredInstallationAsync(CancellationToken cancellationToken = default)
    {
        var settings = userSettingsService.Get();

        if (string.IsNullOrEmpty(settings.PreferredStorageInstallationId))
        {
            logger.LogDebug("No preferred storage installation ID set");
            return null;
        }

        var installationsResult = await gameInstallationService.GetAllInstallationsAsync(cancellationToken);
        if (!installationsResult.Success || installationsResult.Data == null)
        {
            logger.LogWarning("Failed to get installations: {Error}", installationsResult.FirstError);
            return null;
        }

        var preferredInstallation = installationsResult.Data.FirstOrDefault(i => i.Id == settings.PreferredStorageInstallationId);
        if (preferredInstallation == null)
        {
            logger.LogWarning("Preferred installation {InstallationId} not found", settings.PreferredStorageInstallationId);
        }

        return preferredInstallation;
    }

    /// <inheritdoc/>
    public async Task SetPreferredInstallationAsync(string installationId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installationId, nameof(installationId));

        await userSettingsService.TryUpdateAndSaveAsync(settings =>
        {
            settings.PreferredStorageInstallationId = installationId;
            settings.MarkAsExplicitlySet(nameof(settings.PreferredStorageInstallationId));
            return true;
        });

        logger.LogInformation("Set preferred storage installation to {InstallationId}", installationId);
    }

    /// <inheritdoc/>
    public bool RequiresUserSelection(IEnumerable<IGameInstallation> installations)
    {
        ArgumentNullException.ThrowIfNull(installations);

        var installationsList = installations.ToList();
        if (installationsList.Count <= 1)
        {
            logger.LogDebug("Only {Count} installation(s), no user selection required", installationsList.Count);
            return false;
        }

        // Get unique drive roots
        var drives = installationsList
            .Select(i => Path.GetPathRoot(i.InstallationPath))
            .Where(root => !string.IsNullOrEmpty(root))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var requiresSelection = drives.Count > 1;
        logger.LogDebug(
            "Found {InstallationCount} installations on {DriveCount} drive(s), user selection required: {RequiresSelection}",
            installationsList.Count,
            drives.Count,
            requiresSelection);

        return requiresSelection;
    }

    /// <inheritdoc/>
    public bool AreSameVolume(string path1, string path2)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path1, nameof(path1));
        ArgumentException.ThrowIfNullOrWhiteSpace(path2, nameof(path2));

        var sameVolume = FileOperationsService.AreSameVolume(path1, path2);

        logger.LogDebug(
            "Comparing volumes: {Path1} vs {Path2}, same: {SameVolume}",
            path1,
            path2,
            sameVolume);

        return sameVolume;
    }
}
