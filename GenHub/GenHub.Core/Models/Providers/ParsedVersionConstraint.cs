using System;
using System.Collections.Generic;
using System.Linq;

namespace GenHub.Core.Models.Providers;

/// <summary>
/// Represents parsed version constraint bounds and/or compatible versions list.
/// </summary>
/// <param name="MinVersion">The minimum version bound, or empty string if unbounded.</param>
/// <param name="MaxVersion">The maximum version bound, or empty string if unbounded.</param>
/// <param name="MinInclusive">Whether the minimum version bound is inclusive.</param>
/// <param name="MaxInclusive">Whether the maximum version bound is inclusive.</param>
/// <param name="CompatibleVersions">Optional list of explicitly compatible versions.</param>
public sealed record ParsedVersionConstraint(
    string MinVersion,
    string MaxVersion,
    bool MinInclusive,
    bool MaxInclusive,
    IReadOnlyList<string>? CompatibleVersions)
{
    /// <summary>
    /// Checks whether the specified release version satisfies this constraint.
    /// </summary>
    /// <param name="version">The version string to evaluate.</param>
    /// <returns><c>true</c> if the version satisfies the constraint; otherwise, <c>false</c>.</returns>
    public bool IsSatisfiedBy(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return false;
        }

        if (CompatibleVersions != null)
        {
            return SatisfiesCompatibleVersions(version);
        }

        return SatisfiesMin(version) && SatisfiesMax(version);
    }

    private bool SatisfiesCompatibleVersions(string version)
    {
        return CompatibleVersions!.Count > 0 &&
               CompatibleVersions.Any(cv =>
                   string.Equals(cv, version, StringComparison.OrdinalIgnoreCase) ||
                   CatalogManifestIdentity.CompareVersions(cv, version) == 0);
    }

    private bool SatisfiesMin(string version)
    {
        if (string.IsNullOrEmpty(MinVersion))
        {
            return true;
        }

        var cmp = CatalogManifestIdentity.CompareVersions(version, MinVersion);
        return MinInclusive ? cmp >= 0 : cmp > 0;
    }

    private bool SatisfiesMax(string version)
    {
        if (string.IsNullOrEmpty(MaxVersion))
        {
            return true;
        }

        var cmp = CatalogManifestIdentity.CompareVersions(version, MaxVersion);
        return MaxInclusive ? cmp <= 0 : cmp < 0;
    }
}
