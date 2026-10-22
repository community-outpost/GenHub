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
    /// Extracts the channel key (e.g., "pr242", "main", "development", "release", "ci") from a version string.
    /// </summary>
    /// <param name="version">The version string to extract the channel from.</param>
    /// <returns>The normalized channel key, or null if the version is null or empty.</returns>
    public static string? ExtractChannelKey(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return null;
        }

        var clean = version.Split('+')[0].Trim();
        var dashIndex = clean.IndexOf('-');
        if (dashIndex >= 0 && dashIndex < clean.Length - 1)
        {
            var suffix = clean[(dashIndex + 1)..].Trim();
            if (!string.IsNullOrEmpty(suffix))
            {
                var ciMatch = CiMarkerRegex().Match(clean);
                if (ciMatch.Success && suffix.StartsWith("ci.", StringComparison.OrdinalIgnoreCase))
                {
                    return "ci";
                }

                return suffix.ToLowerInvariant();
            }
        }

        return "release";
    }

    /// <summary>
    /// Extracts the workflow run number from a version string (e.g., "0.0.641-pr241" -> 641).
    /// Returns 0 for plain semantic versions without CI run markers.
    /// </summary>
    /// <param name="version">The version string to extract the run number from.</param>
    /// <returns>The extracted run number, or 0 if extraction fails or not a CI build.</returns>
    public static int ExtractRunNumber(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return 0;
        }

        var match = CiRunNumberRegex().Match(version);
        if (match.Success && int.TryParse(match.Groups[1].Value, out var runNumber) && runNumber > 0)
        {
            return runNumber;
        }

        var ciMatch = CiMarkerRegex().Match(version);
        if (ciMatch.Success && int.TryParse(ciMatch.Groups[1].Value, out var ciRunNumber) && ciRunNumber > 0)
        {
            return ciRunNumber;
        }

        return 0;
    }

    /// <summary>
    /// Checks whether an available artifact version is newer than the currently installed version.
    /// Rejects cross-channel sequential comparisons when the current installation belongs to a specific channel.
    /// </summary>
    /// <param name="newVersion">The new artifact version string.</param>
    /// <param name="currentVersion">The current version string.</param>
    /// <param name="allowCrossChannel">Whether to allow comparing versions from different channels.</param>
    /// <returns>True if newVersion is newer than currentVersion; otherwise false.</returns>
    public static bool IsArtifactVersionNewer(string? newVersion, string? currentVersion, bool allowCrossChannel = false)
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

        if (!allowCrossChannel)
        {
            var newChannel = ExtractChannelKey(newVersionBase);
            var currentChannel = ExtractChannelKey(currentVersionBase);

            // If the currently installed build belongs to a specific channel (e.g. "pr242", "main", "development"),
            // reject updates from any different channel (e.g. "pr265").
            if (!string.IsNullOrEmpty(currentChannel) &&
                !string.Equals(currentChannel, "release", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(newChannel, currentChannel, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

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
    /// Regex for extracting workflow run number from a 0.0.X CI version string.
    /// Matches patterns like "0.0.1282-pr265", "0.0.1282-main", "0.0.1282".
    /// </summary>
    [GeneratedRegex(@"^0\.0\.(\d+)(?:-[a-zA-Z0-9_.-]+)?$", RegexOptions.IgnoreCase)]
    private static partial Regex CiRunNumberRegex();

    /// <summary>
    /// Regex for extracting workflow run number from a -ci.X marker.
    /// </summary>
    [GeneratedRegex(@"-ci\.(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex CiMarkerRegex();
}
