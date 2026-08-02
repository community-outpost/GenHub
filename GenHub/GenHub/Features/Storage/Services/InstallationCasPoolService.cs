using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Storage;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Storage.Services;

/// <summary>
/// Selects a writable installation CAS pool while preserving prior readable content.
/// </summary>
public sealed class InstallationCasPoolService(
    IUserSettingsService userSettingsService,
    IStorageWritabilityProbe writabilityProbe,
    ICasPoolManager casPoolManager,
    ILogger<InstallationCasPoolService> logger) : IInstallationCasPoolService
{
    private const string ExplicitInstallationPoolPathKey = nameof(CasConfiguration.InstallationPoolRootPath);

    /// <inheritdoc/>
    public async Task<bool> EnsurePoolPathAsync(
        IReadOnlyList<GameInstallation> installations,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(installations);

        if (installations.Count == 0)
        {
            logger.LogWarning("No installations detected; the primary CAS pool will be used");
            return false;
        }

        var preferredInstallation = installations.Count == 1
            ? installations[0]
            : installations.FirstOrDefault(installation => installation.InstallationType == GameInstallationType.Steam)
                ?? installations.FirstOrDefault(installation => installation.InstallationType == GameInstallationType.EaApp)
                ?? installations[0];

        var derivedPaths = installations
            .Select(GetDerivedPoolPath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(NormalizePath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToHashSet(PathHelper.PathComparer);
        var candidatePath = GetDerivedPoolPath(preferredInstallation);
        if (string.IsNullOrWhiteSpace(candidatePath))
        {
            logger.LogWarning(
                "Preferred installation {InstallationId} has no usable path; the primary CAS pool will be used",
                preferredInstallation.Id);
            return false;
        }

        candidatePath = NormalizePath(candidatePath);
        if (string.IsNullOrWhiteSpace(candidatePath))
        {
            logger.LogWarning("The derived installation CAS pool path is invalid; the primary pool will be used");
            return false;
        }

        var currentSettings = userSettingsService.Get();
        var configuredCurrentPath = currentSettings.CasConfiguration.InstallationPoolRootPath;
        var currentPath = NormalizePath(configuredCurrentPath);
        var historicalAutoDerivedMarker =
            currentSettings.ExplicitlySetProperties.Contains(ExplicitInstallationPoolPathKey);
        if (!string.IsNullOrWhiteSpace(configuredCurrentPath) &&
            string.IsNullOrWhiteSpace(currentPath) &&
            !currentSettings.CasConfiguration.IsInstallationPoolRootPathAutoDerived &&
            !historicalAutoDerivedMarker)
        {
            logger.LogWarning(
                "User-configured installation CAS pool {PoolPath} is invalid; preserving the setting and using the primary pool",
                configuredCurrentPath);
            return true;
        }

        var currentIsAutoDerived = IsAutoDerived(currentSettings, currentPath, derivedPaths);

        if (!string.IsNullOrWhiteSpace(currentPath) && !currentIsAutoDerived)
        {
            if (writabilityProbe.CanCreateStorageAt(currentPath))
            {
                logger.LogInformation("Keeping user-configured installation CAS pool {PoolPath}", currentPath);
            }
            else
            {
                logger.LogWarning(
                    "User-configured installation CAS pool {PoolPath} is not writable; preserving the setting and using the primary pool",
                    currentPath);
            }

            return true;
        }

        var candidateIsWritable = writabilityProbe.CanCreateStorageAt(candidatePath);
        var effectivePath = candidateIsWritable ? candidatePath : string.Empty;
        var legacyPath = SelectLegacyPath(currentSettings, currentPath, candidatePath, effectivePath);
        var settingsAlreadyMatch =
            string.Equals(currentPath, effectivePath, PathHelper.PathComparison) &&
            currentSettings.CasConfiguration.IsInstallationPoolRootPathAutoDerived == candidateIsWritable &&
            string.Equals(
                NormalizePath(currentSettings.CasConfiguration.LegacyInstallationPoolRootPath),
                legacyPath,
                PathHelper.PathComparison) &&
            string.Equals(currentSettings.PreferredStorageInstallationId, preferredInstallation.Id, StringComparison.Ordinal) &&
            !currentSettings.ExplicitlySetProperties.Contains(ExplicitInstallationPoolPathKey);
        if (settingsAlreadyMatch)
        {
            return true;
        }

        var saved = await userSettingsService.TryUpdateAndSaveAsync(settings =>
        {
            settings.CasConfiguration.InstallationPoolRootPath = effectivePath;
            settings.CasConfiguration.IsInstallationPoolRootPathAutoDerived = candidateIsWritable;
            settings.CasConfiguration.LegacyInstallationPoolRootPath = legacyPath;
            settings.PreferredStorageInstallationId = preferredInstallation.Id;
            settings.ExplicitlySetProperties.Remove(ExplicitInstallationPoolPathKey);
            return true;
        });

        if (!saved)
        {
            logger.LogError("Failed to save installation CAS pool settings");
            return false;
        }

        if (candidateIsWritable)
        {
            logger.LogInformation(
                "Using installation-adjacent CAS pool {PoolPath} for installation {InstallationId}",
                candidatePath,
                preferredInstallation.Id);
        }
        else
        {
            logger.LogWarning(
                "Installation-adjacent CAS pool {PoolPath} is not writable; new content will use the primary pool",
                candidatePath);
        }

        casPoolManager.ReinitializeInstallationPool();
        return true;
    }

    private static string? GetDerivedPoolPath(GameInstallation installation)
    {
        var installationPath = !string.IsNullOrWhiteSpace(installation.InstallationPath)
            ? installation.InstallationPath
            : !string.IsNullOrWhiteSpace(installation.ZeroHourPath)
                ? installation.ZeroHourPath
                : installation.GeneralsPath;

        if (string.IsNullOrWhiteSpace(installationPath))
        {
            return null;
        }

        if (Path.HasExtension(installationPath))
        {
            installationPath = Path.GetDirectoryName(installationPath);
        }

        return string.IsNullOrWhiteSpace(installationPath)
            ? null
            : Path.Combine(installationPath, DirectoryNames.GenHubCasPool);
    }

    private static bool IsAutoDerived(
        UserSettings settings,
        string currentPath,
        IReadOnlySet<string> derivedPaths)
    {
        if (string.IsNullOrWhiteSpace(currentPath))
        {
            return true;
        }

        return settings.CasConfiguration.IsInstallationPoolRootPathAutoDerived ||
            settings.ExplicitlySetProperties.Contains(ExplicitInstallationPoolPathKey) ||
            derivedPaths.Contains(currentPath);
    }

    private static string SelectLegacyPath(
        UserSettings settings,
        string currentPath,
        string candidatePath,
        string effectivePath)
    {
        var existingLegacyPath = NormalizePath(settings.CasConfiguration.LegacyInstallationPoolRootPath);
        var previousPath = !string.IsNullOrWhiteSpace(currentPath)
            ? currentPath
            : candidatePath;

        if (!string.IsNullOrWhiteSpace(effectivePath) &&
            string.Equals(previousPath, effectivePath, PathHelper.PathComparison))
        {
            return existingLegacyPath.Equals(effectivePath, PathHelper.PathComparison)
                ? string.Empty
                : existingLegacyPath;
        }

        return Directory.Exists(previousPath)
            ? previousPath
            : existingLegacyPath;
    }

    private static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException or SecurityException)
        {
            return string.Empty;
        }
    }
}
