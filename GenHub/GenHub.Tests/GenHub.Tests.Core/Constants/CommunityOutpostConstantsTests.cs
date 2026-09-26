using FluentAssertions;
using GenHub.Core.Constants;
using GenHub.Core.Models.Results.Content;
using Xunit;

namespace GenHub.Tests.Core.Constants;

/// <summary>
/// Unit tests for <see cref="CommunityOutpostConstants"/> content classifiers.
/// </summary>
public class CommunityOutpostConstantsTests
{
    /// <summary>
    /// Verifies that exact base game codes and plain version display names are detected.
    /// </summary>
    /// <param name="value">The identifier to test.</param>
    [Theory]
    [InlineData("10zh")]
    [InlineData("10gn")]
    [InlineData("generals.10zh")]
    [InlineData("zerohour.10gn")]
    [InlineData("basegame")]
    [InlineData("base-game")]
    [InlineData("official")]
    [InlineData("Zero Hour 1.04")]
    [InlineData("Generals 1.08")]
    public void IsBaseGameIdentifier_WithBaseGameValues_ShouldReturnTrue(string value)
    {
        CommunityOutpostConstants.IsBaseGameIdentifier(value).Should().BeTrue();
    }

    /// <summary>
    /// Verifies that version display names carrying a Community Patch marker are not
    /// misclassified as base game content.
    /// </summary>
    /// <param name="value">The identifier to test.</param>
    [Theory]
    [InlineData("Zero Hour 1.04 (Community Patch)")]
    [InlineData("Generals 1.08 (Community Patch)")]
    [InlineData("Zero Hour 1.04 community-patch")]
    public void IsBaseGameIdentifier_WithCommunityPatchMarker_ShouldReturnFalse(string value)
    {
        CommunityOutpostConstants.IsBaseGameIdentifier(value).Should().BeFalse();
    }

    /// <summary>
    /// Verifies that unrelated or empty values are not detected as base game content.
    /// </summary>
    /// <param name="value">The identifier to test.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Community Patch 23-07-2026")]
    [InlineData("generalszh_23-07-2026.zip")]
    public void IsBaseGameIdentifier_WithOtherValues_ShouldReturnFalse(string? value)
    {
        CommunityOutpostConstants.IsBaseGameIdentifier(value).Should().BeFalse();
    }

    /// <summary>
    /// Verifies that a Community Patch release whose display name contains a base game
    /// version string is still recognized as Community Patch content.
    /// </summary>
    [Fact]
    public void IsCommunityPatch_WithBaseGameVersionInDisplayName_ShouldReturnTrue()
    {
        var result = new ContentSearchResult
        {
            Id = "zerohour104cp.zip",
            Name = "Zero Hour 1.04 (Community Patch)",
            Version = "23-07-2026",
        };

        CommunityOutpostConstants.IsCommunityPatch(result).Should().BeTrue();
    }
}
