using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace GenHub.Core.Helpers;

/// <summary>
/// Helper class for application update version comparison and parsing.
/// </summary>
public static partial class AppUpdateVersionHelper
{
    /// <summary>
    /// Extracts the workflow run number from a version string (e.g., "0.0.641-pr241" -> 641).
    /// </summary>
    /// <param name="version">The version string to extract the run number from.</param>
    /// <returns>The extracted run number, or 0 if extraction fails.</returns>
    public static int ExtractRunNumber(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return 0;
        }

        var match = RunNumberRegex().Match(version);
        if (match.Success && int.TryParse(match.Groups[1].Value, out var runNumber))
        {
            return runNumber;
        }

        var parts = version.Split('.', '-', '+');
        foreach (var part in parts.Reverse())
        {
            if (int.TryParse(part, out var number))
            {
                return number;
            }
        }

        return 0;
    }

    /// <summary>
    /// Checks whether an available artifact version is newer than the currently installed version.
    /// </summary>
    /// <param name="newVersion">The new artifact version string.</param>
    /// <param name="currentVersion">The current version string.</param>
    /// <returns>True if newVersion is newer than currentVersion; otherwise false.</returns>
    public static bool IsArtifactVersionNewer(string? newVersion, string? currentVersion)
    {
        if (string.IsNullOrWhiteSpace(newVersion))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(currentVersion))
        {
            return true;
        }

        var newVersionBase = newVersion.Split('+')[0].Trim();
        var currentVersionBase = currentVersion.Split('+')[0].Trim();

        var newRun = ExtractRunNumber(newVersionBase);
        var currentRun = ExtractRunNumber(currentVersionBase);

        if (newRun > 0 && currentRun > 0)
        {
            return newRun > currentRun;
        }

        if (newRun == 0 && currentRun > 0)
        {
            return false;
        }

        if (newRun > 0 && currentRun == 0)
        {
            return true;
        }

        var newClean = newVersionBase.Split('-')[0];
        var currentClean = currentVersionBase.Split('-')[0];
        if (Version.TryParse(newClean, out var newVer) && Version.TryParse(currentClean, out var currentVer))
        {
            return newVer > currentVer;
        }

        return false;
    }

    /// <summary>
    /// Regex for extracting workflow run number from a version string.
    /// Matches patterns like "0.0.1282-pr265", "0.0.1282-main", "0.0.1282".
    /// </summary>
    [GeneratedRegex(@"(\d+)(?:-pr\d+|-\w+)?$", RegexOptions.IgnoreCase)]
    private static partial Regex RunNumberRegex();
}
