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
}
