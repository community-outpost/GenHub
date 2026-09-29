using FluentAssertions;
using GenHub.Core.Constants;
using GenHub.Core.Models.Providers;
using Xunit;

namespace GenHub.Tests.Core.Constants;

/// <summary>
/// Unit tests for <see cref="CatalogConstants"/> and <see cref="CatalogConstants.UpstreamProviders"/>.
/// </summary>
public class CatalogConstantsTests
{
    /// <summary>
    /// Verifies that provider aliases are normalized to their canonical provider names.
    /// </summary>
    /// <param name="alias">The raw provider alias.</param>
    /// <param name="expected">The expected canonical name.</param>
    [Theory]
    [InlineData("TheSuperHackers", "TheSuperHackers")]
    [InlineData("thesuperhackers", "TheSuperHackers")]
    [InlineData("superhackers", "TheSuperHackers")]
    [InlineData("GeneralsOnline", "GeneralsOnline")]
    [InlineData("generalsonline", "GeneralsOnline")]
    [InlineData("CommunityOutpost", "CommunityOutpost")]
    [InlineData("communityoutpost", "CommunityOutpost")]
    [InlineData("community-outpost", "CommunityOutpost")]
    [InlineData("GitHubReleases", "GitHubReleases")]
    [InlineData("githubreleases", "GitHubReleases")]
    [InlineData("github", "GitHubReleases")]
    public void Normalize_WithValidAliases_ReturnsCanonicalName(string alias, string expected)
    {
        CatalogConstants.UpstreamProviders.Normalize(alias).Should().Be(expected);
    }

    /// <summary>
    /// Verifies that invalid or null providers return null.
    /// </summary>
    /// <param name="invalidProvider">The unsupported provider string.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("unknown-provider")]
    [InlineData("moddb")]
    public void Normalize_WithInvalidOrNullProvider_ReturnsNull(string? invalidProvider)
    {
        CatalogConstants.UpstreamProviders.Normalize(invalidProvider).Should().BeNull();
    }

    /// <summary>
    /// Verifies that IsConfiguredUpstreamSource recognises configured Community Outpost items.
    /// </summary>
    /// <param name="provider">The upstream provider identifier or alias.</param>
    [Theory]
    [InlineData("CommunityOutpost")]
    [InlineData("communityoutpost")]
    [InlineData("community-outpost")]
    [InlineData("TheSuperHackers")]
    [InlineData("GeneralsOnline")]
    [InlineData("GitHubReleases")]
    public void IsConfiguredUpstreamSource_WithSupportedProviders_ReturnsTrue(string provider)
    {
        var item = new CatalogContentItem
        {
            Id = "test-item",
            UpstreamSync = new CatalogUpstreamSync
            {
                Provider = provider,
            },
        };

        CatalogConstants.UpstreamProviders.IsConfiguredUpstreamSource(item).Should().BeTrue();
    }

    /// <summary>
    /// Verifies that declared repositories resolve to trimmed owner/repo coordinates.
    /// </summary>
    [Fact]
    public void TryResolveGitHubRepository_WithDeclaredRepository_ReturnsCoordinates()
    {
        var resolved = CatalogConstants.UpstreamProviders.TryResolveGitHubRepository(
            "GitHubReleases",
            " Owner/Repo ",
            out var owner,
            out var repo);

        resolved.Should().BeTrue();
        owner.Should().Be("Owner");
        repo.Should().Be("Repo");
    }

    /// <summary>
    /// Verifies that a TheSuperHackers item without a repository falls back to the default game-code repo.
    /// </summary>
    [Fact]
    public void TryResolveGitHubRepository_WithoutRepository_FallsBackToDefault()
    {
        var resolved = CatalogConstants.UpstreamProviders.TryResolveGitHubRepository(
            "TheSuperHackers",
            null,
            out var owner,
            out var repo);

        resolved.Should().BeTrue();
        $"{owner}/{repo}".Should().Be(CatalogConstants.UpstreamProviders.DefaultSuperHackersRepository);
    }

    /// <summary>
    /// Verifies that unsupported providers and malformed repositories fail resolution.
    /// </summary>
    /// <param name="provider">The declared provider.</param>
    /// <param name="repository">The declared repository.</param>
    [Theory]
    [InlineData("GeneralsOnline", "Owner/Repo")]
    [InlineData("unknown-provider", "Owner/Repo")]
    [InlineData("GitHubReleases", null)]
    [InlineData("GitHubReleases", "")]
    [InlineData("GitHubReleases", "OwnerOnly")]
    [InlineData("GitHubReleases", "Owner/Repo/Extra")]
    [InlineData("GitHubReleases", "Owner/ ")]
    public void TryResolveGitHubRepository_WithUnsupportedOrMalformedInput_ReturnsFalse(string? provider, string? repository)
    {
        var resolved = CatalogConstants.UpstreamProviders.TryResolveGitHubRepository(
            provider,
            repository,
            out var owner,
            out var repo);

        resolved.Should().BeFalse();
        owner.Should().BeEmpty();
        repo.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies owner/repo validation.
    /// </summary>
    /// <param name="repository">The repository value.</param>
    /// <param name="expected">The expected validation result.</param>
    [Theory]
    [InlineData("Owner/Repo", true)]
    [InlineData("TheSuperHackers/GeneralsGameCode", true)]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("OwnerOnly", false)]
    [InlineData("Owner/Repo/Extra", false)]
    [InlineData("/Repo", false)]
    [InlineData("Owner/ ", false)]
    [InlineData("Own er/Repo", false)]
    public void IsValidOwnerRepo_ValidatesOwnerRepoFormat(string? repository, bool expected)
    {
        CatalogConstants.UpstreamProviders.IsValidOwnerRepo(repository).Should().Be(expected);
    }

    /// <summary>
    /// Verifies prerelease channel detection.
    /// </summary>
    /// <param name="channel">The channel name.</param>
    /// <param name="expected">The expected detection result.</param>
    [Theory]
    [InlineData("prerelease", true)]
    [InlineData("beta", true)]
    [InlineData("nightly", true)]
    [InlineData("stable", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void IsPrereleaseChannel_DetectsPrereleaseChannels(string? channel, bool expected)
    {
        CatalogConstants.UpstreamChannels.IsPrereleaseChannel(channel).Should().Be(expected);
    }
}
