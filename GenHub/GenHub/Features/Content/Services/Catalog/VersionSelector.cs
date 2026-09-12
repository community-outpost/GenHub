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
    private readonly ILogger<VersionSelector> _logger = logger;

    /// <inheritdoc />
    public IEnumerable<ContentRelease> SelectReleases(
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

        return releases
            .Where(r => !r.IsPrerelease)
            .OrderByDescending(r => r.ReleaseDate)
            .FirstOrDefault(r => r.IsLatest) ?? releases
            .Where(r => !r.IsPrerelease)
            .OrderByDescending(r => r.ReleaseDate)
            .FirstOrDefault();
    }

    /// <inheritdoc />
    public ContentRelease? GetLatest(IEnumerable<ContentRelease> releases)
    {
        ArgumentNullException.ThrowIfNull(releases);

        return releases
            .OrderByDescending(r => r.ReleaseDate)
            .FirstOrDefault();
    }

    private IEnumerable<ContentRelease> GetLatestStableReleases(List<ContentRelease> releases)
    {
        var latest = GetLatestStable(releases);
        if (latest != null)
        {
            _logger.LogDebug("Selected latest stable release: {Version}", latest.Version);
            return [latest];
        }

        _logger.LogWarning("No stable releases found");
        return [];
    }

    private IEnumerable<ContentRelease> GetLatestWithPrereleases(List<ContentRelease> releases)
    {
        var latest = GetLatest(releases);
        if (latest != null)
        {
            _logger.LogDebug("Selected latest release (including prereleases): {Version}", latest.Version);
            return [latest];
        }

        return [];
    }
}
