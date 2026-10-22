using GenHub.Core.Constants;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GenHub.Linux.GameInstallations;

/// <summary>
/// Shared helpers for discovering and validating Wine prefixes on Linux.
/// </summary>
public static class WinePrefixHelper
{
    /// <summary>
    /// Gets Wine prefix directories from common locations.
    /// </summary>
    /// <param name="logger">Optional logger instance.</param>
    /// <returns>Collection of Wine prefix paths.</returns>
    public static List<string> GetWinePrefixes(ILogger? logger = null)
    {
        var winePrefixes = new List<string>();

        try
        {
            var homeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            // Common Wine prefix locations
            var commonWinePaths = new[]
            {
                Path.Combine(homeDirectory, InstallationSearchPathConstants.Linux.WinePrefixDirectoryName),
                Path.Combine(homeDirectory, InstallationSearchPathConstants.Linux.XdgLocalDirectoryName, InstallationSearchPathConstants.Linux.XdgShareDirectoryName, InstallationSearchPathConstants.Linux.WinePrefixesDirectoryName),
                Path.Combine(homeDirectory, InstallationSearchPathConstants.Linux.PlayOnLinuxDirectoryName, InstallationSearchPathConstants.Linux.PlayOnLinuxWinePrefixDirectoryName),
                Path.Combine(homeDirectory, InstallationSearchPathConstants.Linux.FlatpakVarDirectoryName, InstallationSearchPathConstants.Linux.FlatpakAppDirectoryName, InstallationSearchPathConstants.Linux.BottlesFlatpakApplicationId, InstallationSearchPathConstants.Linux.FlatpakDataDirectoryName, InstallationSearchPathConstants.Linux.BottlesDirectoryName),
                InstallationSearchPathConstants.Linux.SystemWineDirectory,
            };

            foreach (var winePath in commonWinePaths.Where(Directory.Exists))
            {
                if (IsValidWinePrefix(winePath))
                {
                    winePrefixes.Add(winePath);
                    logger?.LogDebug("Found Wine prefix: {WinePrefix}", winePath);
                }

                // Check subdirectories for additional prefixes
                try
                {
                    var subdirectories = Directory.GetDirectories(winePath).Where(IsValidWinePrefix);
                    foreach (var subdir in subdirectories)
                    {
                        winePrefixes.Add(subdir);
                        logger?.LogDebug("Found Wine prefix: {WinePrefix}", subdir);
                    }
                }
                catch (Exception ex)
                {
                    logger?.LogDebug(ex, "Failed to enumerate subdirectories in {WinePath}", winePath);
                }
            }
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to enumerate Wine prefixes");
        }

        return winePrefixes;
    }

    /// <summary>
    /// Validates if a directory is a valid Wine prefix.
    /// </summary>
    /// <param name="path">Path to check.</param>
    /// <returns>True if valid Wine prefix.</returns>
    public static bool IsValidWinePrefix(string path)
    {
        try
        {
            var driveCPath = Path.Combine(path, InstallationSearchPathConstants.Linux.WineDriveCDirectoryName);
            var systemPath = Path.Combine(driveCPath, InstallationSearchPathConstants.Linux.WineWindowsDirectoryName, InstallationSearchPathConstants.Linux.WineSystem32DirectoryName);
            return Directory.Exists(driveCPath) && Directory.Exists(systemPath);
        }
        catch
        {
            return false;
        }
    }
}
