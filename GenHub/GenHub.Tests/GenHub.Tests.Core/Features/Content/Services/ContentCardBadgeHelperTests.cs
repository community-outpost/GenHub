using System;
using System.Collections.Generic;
using GenHub.Core.Constants;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Results.Content;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Content.Services;

/// <summary>
/// Unit tests verifying ContentCardBadgeHelper cover fallbacks, publisher logo mappings,
/// and deterministic GitHub cover distribution.
/// </summary>
public class ContentCardBadgeHelperTests
{
    /// <summary>
    /// Verifies that deterministic GitHub cover resolution returns identical results for the same owner.
    /// </summary>
    [Fact]
    public void GetDeterministicGitHubCover_ReturnsConsistentResultForSameOwner()
    {
        var cover1 = ContentCardBadgeHelper.GetDeterministicGitHubCover("TheSuperHackers");
        var cover2 = ContentCardBadgeHelper.GetDeterministicGitHubCover("thesuperhackers");
        var cover3 = ContentCardBadgeHelper.GetDeterministicGitHubCover(" TheSuperHackers ");

        Assert.Equal(cover1, cover2);
        Assert.Equal(cover2, cover3);
    }

    /// <summary>
    /// Verifies that deterministic GitHub cover resolution returns a valid cover URI from the known set.
    /// </summary>
    [Fact]
    public void GetDeterministicGitHubCover_ReturnsValidCoverUri()
    {
        var validCovers = new HashSet<string>
        {
            "avares://GenHub/Assets/Covers/generals-cover-2.png",
            "avares://GenHub/Assets/Covers/generals-cover.png",
            "avares://GenHub/Assets/Covers/zerohour-cover.png",
        };

        var cover = ContentCardBadgeHelper.GetDeterministicGitHubCover("some-random-user");
        Assert.Contains(cover, validCovers);

        var nullCover = ContentCardBadgeHelper.GetDeterministicGitHubCover(null);
        Assert.Contains(nullCover, validCovers);
    }

    /// <summary>
    /// Verifies that deterministic GitHub cover resolution distributes across different covers for various owners.
    /// </summary>
    [Fact]
    public void GetDeterministicGitHubCover_DistributesAcrossCovers()
    {
        var owners = new[] { "owner-a", "owner-b", "owner-c", "owner-d", "owner-e", "owner-f" };
        var seenCovers = new HashSet<string>();

        foreach (var owner in owners)
        {
            seenCovers.Add(ContentCardBadgeHelper.GetDeterministicGitHubCover(owner));
        }

        // Multiple distinct owners should map to multiple distinct covers
        Assert.True(seenCovers.Count > 1);
    }

    /// <summary>
    /// Verifies that thumbnail URL fallback returns the expected publisher covers.
    /// </summary>
    [Fact]
    public void GetThumbnailUrl_ReturnsExpectedFallbackCoversByProvider()
    {
        var generalsOnlineResult = new ContentSearchResult
        {
            Id = "generalsonline.test",
            Name = "Generals Online Mod",
            ProviderName = "GeneralsOnline",
        };

        var communityOutpostResult = new ContentSearchResult
        {
            Id = "communityoutpost.test",
            Name = "Community Outpost Maps",
            ProviderName = "Community-Outpost",
        };

        var superHackersResult = new ContentSearchResult
        {
            Id = "thesuperhackers.gameclient.test",
            Name = "SuperHackers Client",
            ProviderName = "TheSuperHackers",
        };

        var gitHubResult = new ContentSearchResult
        {
            Id = "github.testowner.repo",
            Name = "GitHub Mod",
            ProviderName = "GitHub",
        };

        Assert.Equal(PublisherInfoConstants.GeneralsOnline.LogoSource, ContentCardBadgeHelper.GetThumbnailUrl(generalsOnlineResult));
        Assert.Equal("avares://GenHub/Assets/Covers/gla-cover.png", ContentCardBadgeHelper.GetThumbnailUrl(communityOutpostResult));
        Assert.Equal("avares://GenHub/Assets/Covers/china-cover.png", ContentCardBadgeHelper.GetThumbnailUrl(superHackersResult));

        var gitHubCover = ContentCardBadgeHelper.GetThumbnailUrl(gitHubResult);
        Assert.NotNull(gitHubCover);
        Assert.StartsWith("avares://GenHub/Assets/Covers/", gitHubCover);
    }

    /// <summary>
    /// Verifies that publisher logo URL resolution returns expected logos and GitHub owner avatars.
    /// </summary>
    [Fact]
    public void GetPublisherLogoUrl_ReturnsExpectedPublisherLogos()
    {
        var generalsOnlineResult = new ContentSearchResult
        {
            Id = "generalsonline.test",
            Name = "Generals Online",
            ProviderName = "GeneralsOnline",
        };

        var communityOutpostResult = new ContentSearchResult
        {
            Id = "communityoutpost.test",
            Name = "Community Outpost",
            ProviderName = "Community-Outpost",
        };

        var superHackersResult = new ContentSearchResult
        {
            Id = "thesuperhackers.gameclient.test",
            Name = "SuperHackers",
            ProviderName = "TheSuperHackers",
        };

        var gitHubWithAvatarResult = new ContentSearchResult
        {
            Id = "github.testowner.repo",
            Name = "GitHub Mod",
            ProviderName = "GitHub",
            IconUrl = "https://avatars.githubusercontent.com/u/12345?v=4",
        };

        var gitHubWithoutAvatarResult = new ContentSearchResult
        {
            Id = "github.customdev.repo",
            Name = "GitHub Mod 2",
            ProviderName = "GitHub",
        };

        Assert.Equal(PublisherInfoConstants.GeneralsOnline.LogoSource, ContentCardBadgeHelper.GetPublisherLogoUrl(generalsOnlineResult));
        Assert.Equal(PublisherInfoConstants.CommunityOutpost.LogoSource, ContentCardBadgeHelper.GetPublisherLogoUrl(communityOutpostResult));
        Assert.Equal(PublisherInfoConstants.TheSuperHackers.LogoSource, ContentCardBadgeHelper.GetPublisherLogoUrl(superHackersResult));
        Assert.Equal("https://avatars.githubusercontent.com/u/12345?v=4", ContentCardBadgeHelper.GetPublisherLogoUrl(gitHubWithAvatarResult));
        Assert.Equal("https://github.com/customdev.png", ContentCardBadgeHelper.GetPublisherLogoUrl(gitHubWithoutAvatarResult));
    }

    /// <summary>
    /// Verifies that TheSuperHackers cards show China cover for GameClients and GLA cover for Patch.
    /// </summary>
    [Fact]
    public void GetThumbnailUrl_TheSuperHackersDifferentiatesGameClientAndPatch()
    {
        var gameClient = new ContentSearchResult
        {
            Id = "github.TheSuperHackers.GeneralsGameCode.latest.zh",
            Name = "GeneralsGameCode weekly-2026-09-05 — Zero Hour",
            ProviderName = "TheSuperHackers",
            ContentType = ContentType.GameClient,
        };

        var patch = new ContentSearchResult
        {
            Id = "github.TheSuperHackers.GeneralsGamePatch2.latest",
            Name = "Community Patch 2",
            ProviderName = "TheSuperHackers",
            ContentType = ContentType.Patch,
        };

        var gitHubGameClient = new ContentSearchResult
        {
            Id = "github.TheSuperHackers.GeneralsGameCode.latest.zh",
            Name = "GeneralsGameCode",
            ProviderName = "GitHub",
            ContentType = ContentType.GameClient,
            ResolverMetadata = { ["owner"] = "TheSuperHackers" },
        };

        var gitHubPatch = new ContentSearchResult
        {
            Id = "github.TheSuperHackers.GeneralsGamePatch2.latest",
            Name = "Community Patch 2",
            ProviderName = "GitHub",
            ContentType = ContentType.Patch,
            VariantGroupId = "thesuperhackers.patch.latest",
        };

        Assert.Equal("avares://GenHub/Assets/Covers/china-cover.png", ContentCardBadgeHelper.GetThumbnailUrl(gameClient));
        Assert.Equal("avares://GenHub/Assets/Covers/gla-cover.png", ContentCardBadgeHelper.GetThumbnailUrl(patch));
        Assert.Equal("avares://GenHub/Assets/Covers/china-cover.png", ContentCardBadgeHelper.GetThumbnailUrl(gitHubGameClient));
        Assert.Equal("avares://GenHub/Assets/Covers/gla-cover.png", ContentCardBadgeHelper.GetThumbnailUrl(gitHubPatch));
    }

    /// <summary>
    /// Verifies that GitHub items with repo names containing publisher names are not hijacked by publisher covers.
    /// </summary>
    [Fact]
    public void GetPublisherLogoUrl_DoesNotHijackGitHubReposContainingPublisherNames()
    {
        var unrelatedGitHubResult = new ContentSearchResult
        {
            Id = "github.randomdev.generalsonlinestats.latest",
            Name = "Generals Online Stats Tool",
            ProviderName = "GitHub",
        };

        var logoUrl = ContentCardBadgeHelper.GetPublisherLogoUrl(unrelatedGitHubResult);
        Assert.Equal("https://github.com/randomdev.png", logoUrl);
    }

    /// <summary>
    /// Verifies that GitHub-sourced items with official owner metadata are identified as official and not generic GitHub.
    /// </summary>
    [Fact]
    public void IsOfficialProvider_GitHubOfficialOwners_IdentifiedAsOfficial()
    {
        var goResult = new ContentSearchResult
        {
            Id = "github.generalsonline.launcher",
            Name = "Generals Online Launcher",
            ProviderName = "GitHub",
            ResolverMetadata = { [GitHubConstants.OwnerMetadataKey] = "GeneralsOnline" },
        };

        var coResult = new ContentSearchResult
        {
            Id = "github.community-outpost.maps",
            Name = "Community Outpost Maps",
            ProviderName = "GitHub",
            ResolverMetadata = { [GitHubConstants.OwnerMetadataKey] = "Community-Outpost" },
        };

        Assert.True(ContentCardBadgeHelper.IsGeneralsOnline(goResult));
        Assert.True(ContentCardBadgeHelper.IsCommunityOutpost(coResult));
        Assert.True(ContentCardBadgeHelper.IsOfficialProvider(goResult));
        Assert.True(ContentCardBadgeHelper.IsOfficialProvider(coResult));
        Assert.False(ContentCardBadgeHelper.IsGenericGitHub(goResult));
        Assert.False(ContentCardBadgeHelper.IsGenericGitHub(coResult));
    }
}
