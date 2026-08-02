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
    IConfigurationProviderService configurationProviderService,
    IGameInstallationService gameInstallationService,
    IStorageWritabilityProbe writabilityProbe,
    ILogger<StorageLocationService> logger) : IStorageLocationService
{
    /// <inheritdoc/>
    public string GetCasPoolPath(IGameInstallation installation)
    {
        ArgumentNullException.ThrowIfNull(installation);

        var settings = userSettingsService.Get();
        var configuredInstallationPoolPath = settings.CasConfiguration.InstallationPoolRootPath;
        if (!string.IsNullOrWhiteSpace(configuredInstallationPoolPath) &&
            writabilityProbe.CanCreateStorageAt(configuredInstallationPoolPath))
        {
            return Path.GetFullPath(configuredInstallationPoolPath);
        }

        if (settings.UseInstallationAdjacentStorage)
        {
            var installationPath = Path.HasExtension(installation.InstallationPath)
                ? Path.GetDirectoryName(installation.InstallationPath)
                : installation.InstallationPath;
            if (!string.IsNullOrWhiteSpace(installationPath))
            {
                var adjacentPath = Path.Combine(installationPath, DirectoryNames.GenHubCasPool);
                if (writabilityProbe.CanCreateStorageAt(adjacentPath))
                {
                    return Path.GetFullPath(adjacentPath);
                }
            }
        }

        var primaryPoolPath = configurationProviderService.GetCasConfiguration().CasRootPath;
        logger.LogInformation(
            "Using primary CAS pool path {CasPoolPath} for installation {InstallationId}",
            primaryPoolPath,
            installation.Id);
        return primaryPoolPath;
    }

    /// <inheritdoc/>
    public string GetWorkspacePath(IGameInstallation installation)
    {
        ArgumentNullException.ThrowIfNull(installation);

        var settings = userSettingsService.Get();
        if (settings.UseInstallationAdjacentStorage &&
            TryGetWritableInstallationAdjacentPath(installation, DirectoryNames.GenHubWorkspace, out var adjacentPath))
        {
            logger.LogDebug(
                "Resolved installation-adjacent workspace path: {WorkspacePath} for installation {InstallationId}",
                adjacentPath,
                installation.Id);
            return adjacentPath;
        }

        var configuredWorkspacePath = settings.WorkspacePath;
        var workspacePath = !string.IsNullOrWhiteSpace(configuredWorkspacePath) && writabilityProbe.CanCreateStorageAt(configuredWorkspacePath)
            ? Path.GetFullPath(configuredWorkspacePath)
            : Path.Combine(configurationProviderService.GetApplicationDataPath(), DirectoryNames.Workspaces);

        logger.LogInformation(
            "Using centralized workspace path {WorkspacePath} for installation {InstallationId}",
            workspacePath,
            installation.Id);
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

    private bool TryGetWritableInstallationAdjacentPath(
        IGameInstallation installation,
        string directoryName,
        out string path)
    {
        var installationRoot = PathHelper.GetSafeParentDirectory(installation.InstallationPath);
        path = Path.Combine(installationRoot, directoryName);
        if (writabilityProbe.CanCreateStorageAt(path))
        {
            return true;
        }

        logger.LogWarning(
            "Installation-adjacent storage path {StoragePath} is not writable for installation {InstallationId}; falling back to user storage",
            path,
            installation.Id);
        return false;
    }
}
