using System;
using GenHub.Core.Interfaces.AppUpdate;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.AppUpdate.Services;

/// <summary>
/// Semantic version comparator implementation.
/// </summary>
public class SemVerComparator(ILogger<SemVerComparator> logger) : IVersionComparator
{
    /// <summary>
    /// Compares two version strings using semantic versioning rules.
    /// </summary>
    /// <param name="versionA">The first version to compare.</param>
    /// <param name="versionB">The second version to compare.</param>
    /// <returns>
    /// Less than zero if versionA is less than versionB;
    /// zero if they are equal;
    /// greater than zero if versionA is greater than versionB.
    /// </returns>
    public int Compare(string versionA, string versionB)
    {
        if (string.IsNullOrWhiteSpace(versionA) || string.IsNullOrWhiteSpace(versionB))
        {
            return string.Compare(versionA, versionB, StringComparison.OrdinalIgnoreCase);
        }

        try
        {
            var versionAObj = ParseVersion(versionA);
            var versionBObj = ParseVersion(versionB);

            var baseComparison = versionAObj.CompareTo(versionBObj);

            // If base versions are equal, check for pre-release
            if (baseComparison == 0)
            {
                var aHasPreRelease = versionA.Contains('-');
                var bHasPreRelease = versionB.Contains('-');

                if (aHasPreRelease && !bHasPreRelease)
                    return -1; // pre-release is less than release
                if (!aHasPreRelease && bHasPreRelease)
                    return 1; // release is greater than pre-release

                // Both have pre-release or both don't, compare as strings
                if (aHasPreRelease && bHasPreRelease)
                {
                    var aPreRelease = versionA.Substring(versionA.IndexOf('-'));
                    var bPreRelease = versionB.Substring(versionB.IndexOf('-'));
                    return string.Compare(aPreRelease, bPreRelease, StringComparison.OrdinalIgnoreCase);
                }
            }

            return baseComparison;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to parse versions for comparison: {VersionA} vs {VersionB}", versionA, versionB);
            return string.Compare(versionA, versionB, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Determines if the candidate version is newer than the current version.
    /// </summary>
    /// <param name="current">The current version string.</param>
    /// <param name="candidate">The candidate version string.</param>
    /// <returns>True if candidate is newer; otherwise, false.</returns>
    public bool IsNewer(string current, string candidate)
    {
        return Compare(candidate, current) > 0;
    }

    private static Version ParseVersion(string versionString)
    {
        // Clean the version string
        var cleanVersion = versionString.Trim().TrimStart('v');

        // Handle pre-release versions (alpha, beta, etc.)
        var dashIndex = cleanVersion.IndexOf('-');
        if (dashIndex > 0)
        {
            cleanVersion = cleanVersion.Substring(0, dashIndex);
        }

        // Handle semantic versioning with build/revision numbers
        var parts = cleanVersion.Split('.');
        var versionParts = new int[4]; // Major.Minor.Build.Revision

        for (int i = 0; i < Math.Min(parts.Length, 4); i++)
        {
            if (int.TryParse(parts[i], out var part))
            {
                versionParts[i] = part;
            }
        }

        return new Version(versionParts[0], versionParts[1], versionParts[2], versionParts[3]);
    }
}