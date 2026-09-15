using System;
using System.Collections.Generic;
using System.Linq;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.Providers;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Content.Services.Catalog;

/// <summary>
/// Filters content releases based on version display policy.
/// Implements "Latest Stable Only" by default to address user feedback about version clutter.
/// </summary>
public class VersionSelector(ILogger<VersionSelector> logger) : IVersionSelector
{
    /// <inheritdoc />
    public IReadOnlyList<ContentRelease> SelectReleases(
        IEnumerable<ContentRelease> releases,
        VersionPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(releases);

        var releasesList = releases.ToList();
        if (releasesList.Count == 0)
        {
            return releasesList;
        }

        return policy switch
        {
            VersionPolicy.LatestStableOnly => GetLatestStableReleases(releasesList),
            VersionPolicy.AllVersions => releasesList,
            VersionPolicy.IncludePrereleases => GetLatestWithPrereleases(releasesList),
            _ => throw new ArgumentOutOfRangeException(nameof(policy), policy, "Unknown version policy"),
        };
    }

    /// <inheritdoc />
    public ContentRelease? GetLatestStable(IEnumerable<ContentRelease> releases)
    {
        ArgumentNullException.ThrowIfNull(releases);

        var stable = releases
            .Where(r => !r.IsPrerelease)
            .OrderByDescending(r => r.ReleaseDate)
            .ToList();

        return stable.FirstOrDefault(r => r.IsLatest) ?? stable.FirstOrDefault();
    }

    /// <inheritdoc />
    public ContentRelease? GetLatest(IEnumerable<ContentRelease> releases)
    {
        ArgumentNullException.ThrowIfNull(releases);

        return releases
            .OrderByDescending(r => r.ReleaseDate)
            .FirstOrDefault();
    }

    private IReadOnlyList<ContentRelease> GetLatestStableReleases(List<ContentRelease> releases)
    {
        var latest = GetLatestStable(releases);
        if (latest != null)
        {
            logger.LogDebug("Selected latest stable release: {Version}", latest.Version);
            return [latest];
        }

        logger.LogWarning("No stable releases found");
        return [];
    }

    private IReadOnlyList<ContentRelease> GetLatestWithPrereleases(List<ContentRelease> releases)
    {
        var latest = GetLatest(releases);
        if (latest != null)
        {
            logger.LogDebug("Selected latest release (including prereleases): {Version}", latest.Version);
            return [latest];
        }

        return [];
    }
}
