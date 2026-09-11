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
        cancellationToken.ThrowIfCancellationRequested();

        if (installations.Count == 0)
        {
            logger.LogWarning("No installations detected; the primary CAS pool will be used");
            return true;
        }

        var preferredInstallation = SelectPreferredInstallation(installations);
        var derivedPaths = CollectDerivedPaths(installations);

        if (!TryResolveCandidatePath(preferredInstallation, out var candidatePath))
        {
            return true;
        }

        var currentSettings = userSettingsService.Get();
        var configuredCurrentPath = currentSettings.CasConfiguration.InstallationPoolRootPath;
        var currentPath = NormalizePath(configuredCurrentPath);

        if (ShouldPreserveUserConfiguredPool(currentSettings, configuredCurrentPath, currentPath, derivedPaths))
        {
            return true;
        }

        candidatePath = ResolveWritableCandidatePath(preferredInstallation, candidatePath, out var candidateIsWritable);
        var effectivePath = candidateIsWritable ? candidatePath : string.Empty;
        var legacyPaths = SelectLegacyPaths(currentSettings, currentPath, candidatePath, effectivePath);

        if (AreSettingsSynchronized(currentSettings, preferredInstallation, currentPath, effectivePath, candidateIsWritable, legacyPaths))
        {
            return true;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var saved = await userSettingsService.TryUpdateAndSaveAsync(settings =>
        {
            settings.CasConfiguration.InstallationPoolRootPath = effectivePath;
            settings.CasConfiguration.IsInstallationPoolRootPathAutoDerived = candidateIsWritable;
            settings.CasConfiguration.LegacyInstallationPoolRootPaths = legacyPaths;
            settings.PreferredStorageInstallationId = preferredInstallation.Id;
            settings.ExplicitlySetProperties.Remove(ExplicitInstallationPoolPathKey);
            return true;
        });

        if (!saved)
        {
            logger.LogError("Failed to save installation CAS pool settings");
            return false;
        }

        LogSelectedPoolPath(preferredInstallation, candidatePath, candidateIsWritable);
        casPoolManager.ReinitializeInstallationPool();
        return true;
    }

    private static GameInstallation SelectPreferredInstallation(IReadOnlyList<GameInstallation> installations)
    {
        if (installations.Count == 1)
        {
            return installations[0];
        }

        return installations.FirstOrDefault(installation => installation.InstallationType == GameInstallationType.Steam)
            ?? installations.FirstOrDefault(installation => installation.InstallationType == GameInstallationType.EaApp)
            ?? installations[0];
    }

    private static HashSet<string> CollectDerivedPaths(IReadOnlyList<GameInstallation> installations)
    {
        return installations
            .Select(GetDerivedPoolPath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(NormalizePath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToHashSet(PathHelper.PathComparer);
    }

    private static bool AreSettingsSynchronized(
        UserSettings currentSettings,
        GameInstallation preferredInstallation,
        string currentPath,
        string effectivePath,
        bool candidateIsWritable,
        IReadOnlyList<string> legacyPaths)
    {
        return string.Equals(currentPath, effectivePath, PathHelper.PathComparison) &&
            currentSettings.CasConfiguration.IsInstallationPoolRootPathAutoDerived == candidateIsWritable &&
            currentSettings.CasConfiguration.LegacyInstallationPoolRootPaths
                .Select(NormalizePath)
                .SequenceEqual(legacyPaths, PathHelper.PathComparer) &&
            string.Equals(currentSettings.PreferredStorageInstallationId, preferredInstallation.Id, StringComparison.Ordinal) &&
            !currentSettings.ExplicitlySetProperties.Contains(ExplicitInstallationPoolPathKey);
    }

    private static string? GetBaseInstallationPath(GameInstallation installation)
    {
        var installationPath = installation.InstallationPath;
        if (string.IsNullOrWhiteSpace(installationPath))
        {
            installationPath = !string.IsNullOrWhiteSpace(installation.ZeroHourPath)
                ? installation.ZeroHourPath
                : installation.GeneralsPath;
        }

        return string.IsNullOrWhiteSpace(installationPath) ? null : installationPath;
    }

    private static string? GetDerivedPoolPath(GameInstallation installation)
    {
        var installationPath = GetBaseInstallationPath(installation);
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

    private static List<string> SelectLegacyPaths(
        UserSettings settings,
        string currentPath,
        string candidatePath,
        string effectivePath)
    {
        // Every root the pool has previously used is retained. Dropping one would strand the
        // objects written to it, because nothing copies them into the pool that replaces it.
        var retainedPaths = new List<string>();
        foreach (var existingLegacyPath in settings.CasConfiguration.LegacyInstallationPoolRootPaths)
        {
            AddLegacyPath(retainedPaths, NormalizePath(existingLegacyPath), effectivePath);
        }

        var previousPath = !string.IsNullOrWhiteSpace(currentPath)
            ? currentPath
            : candidatePath;
        if (Directory.Exists(previousPath))
        {
            AddLegacyPath(retainedPaths, previousPath, effectivePath);
        }

        return retainedPaths;
    }

    private static void AddLegacyPath(List<string> retainedPaths, string path, string effectivePath)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        // The pool that now takes writes is reachable directly, so it is never also a legacy root.
        if (string.Equals(path, effectivePath, PathHelper.PathComparison))
        {
            return;
        }

        if (!retainedPaths.Contains(path, PathHelper.PathComparer))
        {
            retainedPaths.Add(path);
        }
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
        catch (ArgumentException)
        {
            return string.Empty;
        }
        catch (NotSupportedException)
        {
            return string.Empty;
        }
        catch (IOException)
        {
            return string.Empty;
        }
        catch (SecurityException)
        {
            return string.Empty;
        }
    }

    private bool TryResolveCandidatePath(GameInstallation preferredInstallation, out string candidatePath)
    {
        candidatePath = string.Empty;
        var derived = GetDerivedPoolPath(preferredInstallation);
        if (string.IsNullOrWhiteSpace(derived))
        {
            logger.LogWarning(
                "Preferred installation {InstallationId} has no usable path; the primary CAS pool will be used",
                preferredInstallation.Id);
            return false;
        }

        var normalized = NormalizePath(derived);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            logger.LogWarning("The derived installation CAS pool path is invalid; the primary pool will be used");
            return false;
        }

        candidatePath = normalized;
        return true;
    }

    private bool ShouldPreserveUserConfiguredPool(
        UserSettings currentSettings,
        string configuredCurrentPath,
        string currentPath,
        HashSet<string> derivedPaths)
    {
        var historicalAutoDerivedMarker =
            currentSettings.ExplicitlySetProperties.Contains(ExplicitInstallationPoolPathKey);

        // Older GenHub builds marked their automatically derived installation pool as "explicitly set".
        // No user-facing setting wrote this nested key, so it is migration provenance rather than user intent.
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

        return false;
    }

    private string ResolveWritableCandidatePath(GameInstallation preferredInstallation, string candidatePath, out bool isWritable)
    {
        if (writabilityProbe.CanCreateStorageAt(candidatePath))
        {
            isWritable = true;
            return candidatePath;
        }

        var baseInstallationPath = GetBaseInstallationPath(preferredInstallation);
        if (!string.IsNullOrWhiteSpace(baseInstallationPath))
        {
            foreach (var ancestor in PathHelper.EnumerateSameVolumeAncestors(baseInstallationPath))
            {
                var ancestorCandidate = Path.Combine(ancestor, DirectoryNames.GenHubCasPool);
                if (writabilityProbe.CanCreateStorageAt(ancestorCandidate))
                {
                    isWritable = true;
                    return NormalizePath(ancestorCandidate);
                }
            }
        }

        isWritable = false;
        return candidatePath;
    }

    private void LogSelectedPoolPath(GameInstallation preferredInstallation, string candidatePath, bool isWritable)
    {
        if (isWritable)
        {
            logger.LogInformation(
                "Using same-volume CAS pool {PoolPath} for installation {InstallationId}",
                candidatePath,
                preferredInstallation.Id);
        }
        else
        {
            logger.LogWarning(
                "Installation-adjacent CAS pool {PoolPath} and same-volume ancestors are not writable; new content will use the primary pool",
                candidatePath);
        }
    }
}
