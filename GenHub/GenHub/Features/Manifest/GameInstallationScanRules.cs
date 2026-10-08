using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;
using System;
using System.Collections.Generic;
using System.IO;

namespace GenHub.Features.Manifest;

/// <summary>
/// Shared file-selection rules for game installation manifests. Manifest generation and
/// launch-time drift detection both use these so they agree on which files a
/// regeneration would capture.
/// </summary>
internal static class GameInstallationScanRules
{
    /// <summary>
    /// File extensions captured by directory-scan manifest generation.
    /// </summary>
    internal static readonly HashSet<string> FallbackFileExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".016",
        ".256",
        ".ani",
        ".asi",
        ".big",
        ".bik",
        ".bmp",
        ".cfg",
        ".csf",
        ".dat",
        ".dll",
        ".exe",
        ".flt",
        ".ico",
        ".ini",
        ".lcf",
        ".m3d",
        ".map",
        ".scb",
        ".str",
        ".sys",
        ".tga",
        ".txt",
        ".vp6",
        ".w3d",
        ".wav",
    };

    /// <summary>
    /// Determines whether manifest generation uses the authoritative CSV catalog for the
    /// specified game and version, or falls back to a full directory scan.
    /// </summary>
    /// <param name="gameType">The game type.</param>
    /// <param name="version">The manifest version string.</param>
    /// <returns>True when an authoritative catalog backs generation; otherwise, false.</returns>
    internal static bool UsesAuthoritativeCatalog(GameType gameType, string? version)
    {
        return !string.IsNullOrWhiteSpace(version) && TryGetCatalogInfo(gameType, version, out _);
    }

    /// <summary>
    /// Determines whether a game installation file is excluded from manifests.
    /// </summary>
    /// <param name="relativePath">The installation-relative file path.</param>
    /// <returns>True when the file must be skipped; otherwise, false.</returns>
    internal static bool ShouldSkipFile(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/');
        return normalized.StartsWith(SteamConstants.BackupDirName + "/", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith(FileTypes.GitDirectoryName + "/", StringComparison.OrdinalIgnoreCase) ||
               normalized.EndsWith(SteamConstants.BackupExtension, StringComparison.OrdinalIgnoreCase) ||
               normalized.EndsWith(FileTypes.LegacyBackupExtension, StringComparison.OrdinalIgnoreCase) ||
               normalized.EndsWith(SteamConstants.ProxyLauncherFileName, StringComparison.OrdinalIgnoreCase) ||
               normalized.EndsWith(SteamConstants.TrackingFileName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Resolves the manifest source for a game file, preferring a sibling backup copy
    /// (Steam proxy deployments keep the original executable beside its backup).
    /// </summary>
    /// <param name="filePath">The on-disk file path.</param>
    /// <returns>The backup path when a usable backup exists; otherwise, the file path.</returns>
    internal static string ResolveSourcePathWithBackup(string filePath)
    {
        var backupPath = filePath + SteamConstants.BackupExtension;
        if (File.Exists(backupPath) && !IsReparsePoint(backupPath))
        {
            return backupPath;
        }

        var legacyBackupPath = filePath + FileTypes.LegacyBackupExtension;
        if (File.Exists(legacyBackupPath) && !IsReparsePoint(legacyBackupPath))
        {
            return legacyBackupPath;
        }

        return filePath;
    }

    /// <summary>
    /// Determines whether a path is a reparse point, treating unreadable paths as reparse points.
    /// </summary>
    /// <param name="path">The path to check.</param>
    /// <returns>True when the path is a reparse point or cannot be read; otherwise, false.</returns>
    internal static bool IsReparsePoint(string path)
    {
        try
        {
            return File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    /// <summary>
    /// Gets the authoritative catalog backing the specified game and version, if any.
    /// </summary>
    /// <param name="gameType">The game type.</param>
    /// <param name="version">The manifest version string.</param>
    /// <param name="info">The catalog file name and hash when available.</param>
    /// <returns>True when a catalog backs the game and version; otherwise, false.</returns>
    internal static bool TryGetCatalogInfo(GameType gameType, string version, out (string FileName, string Sha256) info)
    {
        if (gameType == GameType.Generals && version is "1.08" or "1.8")
        {
            info = (CsvConstants.GeneralsCsvFileName, CsvConstants.Generals108Sha256);
            return true;
        }

        if (gameType == GameType.ZeroHour && version is "1.04" or "1.4")
        {
            info = (CsvConstants.ZeroHourCsvFileName, CsvConstants.ZeroHour104Sha256);
            return true;
        }

        info = default;
        return false;
    }
}
