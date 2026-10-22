using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.Providers;
using GenHub.Features.Content.Services.Catalog;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace GenHub.Tests.Core.Features.Content.Services.Catalog;

/// <summary>
/// Unit tests for <see cref="VersionSelector"/> prerelease handling.
/// </summary>
public sealed class VersionSelectorTests
{
    private readonly VersionSelector _selector = new(NullLogger<VersionSelector>.Instance);

    /// <summary>
    /// A stable release is preferred over a newer prerelease under the default policy.
    /// </summary>
    [Fact]
    public void SelectReleases_LatestStableOnly_PrefersStableOverNewerPrerelease()
    {
        var releases = new List<ContentRelease>
        {
            new() { Version = "2.0-beta", ReleaseDate = new DateTime(2026, 9, 2, 0, 0, 0, DateTimeKind.Utc), IsPrerelease = true, IsLatest = true },
            new() { Version = "1.0", ReleaseDate = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), IsPrerelease = false, IsLatest = false },
        };

        var selected = _selector.SelectReleases(releases, VersionPolicy.LatestStableOnly);

        Assert.Equal("1.0", Assert.Single(selected).Version);
    }

    /// <summary>
    /// A prerelease-only item must stay visible under the default policy by falling back
    /// to the newest prerelease instead of vanishing from listings.
    /// </summary>
    [Fact]
    public void SelectReleases_LatestStableOnly_PrereleaseOnlyItemFallsBackToNewestPrerelease()
    {
        var releases = new List<ContentRelease>
        {
            new() { Version = "2.0-beta", ReleaseDate = new DateTime(2026, 9, 2, 0, 0, 0, DateTimeKind.Utc), IsPrerelease = true, IsLatest = false },
            new() { Version = "2.1-beta", ReleaseDate = new DateTime(2026, 9, 3, 0, 0, 0, DateTimeKind.Utc), IsPrerelease = true, IsLatest = true },
        };

        var selected = _selector.SelectReleases(releases, VersionPolicy.LatestStableOnly);

        Assert.Equal("2.1-beta", Assert.Single(selected).Version);
    }

    /// <summary>
    /// An item with no releases still selects nothing.
    /// </summary>
    [Fact]
    public void SelectReleases_LatestStableOnly_NoReleases_ReturnsEmpty()
    {
        var selected = _selector.SelectReleases(Enumerable.Empty<ContentRelease>(), VersionPolicy.LatestStableOnly);

        Assert.Empty(selected);
    }
}
