using GenHub.Core.Constants;
using GenHub.Core.Models.GeneralsOnline;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.Content.Services.GeneralsOnline;
using System;
using Xunit;

namespace GenHub.Tests.Core.Features.Content.Services.GeneralsOnline;

/// <summary>
/// Unit tests for <see cref="GeneralsOnlinePatchNotesHelper"/>.
/// </summary>
public sealed class GeneralsOnlinePatchNotesHelperTests
{
    /// <summary>
    /// Verifies that passing null returns false.
    /// </summary>
    [Fact]
    public void IsGeneralsOnline_NullSearchResult_ReturnsFalse()
    {
        Assert.False(GeneralsOnlinePatchNotesHelper.IsGeneralsOnline(null));
    }

    /// <summary>
    /// Verifies that matching provider name returns true.
    /// </summary>
    /// <param name="providerName">The provider name to test.</param>
    [Theory]
    [InlineData("generalsonline")]
    [InlineData("GeneralsOnline")]
    public void IsGeneralsOnline_MatchingProviderName_ReturnsTrue(string providerName)
    {
        var sr = new ContentSearchResult { ProviderName = providerName };
        Assert.True(GeneralsOnlinePatchNotesHelper.IsGeneralsOnline(sr));
    }

    /// <summary>
    /// Verifies that matching resolver ID returns true.
    /// </summary>
    [Fact]
    public void IsGeneralsOnline_MatchingResolverId_ReturnsTrue()
    {
        var sr = new ContentSearchResult { ResolverId = GeneralsOnlineConstants.ResolverId };
        Assert.True(GeneralsOnlinePatchNotesHelper.IsGeneralsOnline(sr));
    }

    /// <summary>
    /// Verifies that attached release data returns true.
    /// </summary>
    [Fact]
    public void IsGeneralsOnline_HasReleaseData_ReturnsTrue()
    {
        var sr = new ContentSearchResult();
        sr.SetData(new GeneralsOnlineRelease { Version = "092826", PortableUrl = "https://example.com/portable.zip" });
        Assert.True(GeneralsOnlinePatchNotesHelper.IsGeneralsOnline(sr));
    }

    /// <summary>
    /// Verifies that unrelated content returns false.
    /// </summary>
    [Fact]
    public void IsGeneralsOnline_UnrelatedContent_ReturnsFalse()
    {
        var sr = new ContentSearchResult
        {
            ProviderName = "ModDB",
            ResolverId = "ModDB",
        };
        Assert.False(GeneralsOnlinePatchNotesHelper.IsGeneralsOnline(sr));
    }

    /// <summary>
    /// Verifies placeholder and empty descriptions return true for needing patch notes.
    /// </summary>
    /// <param name="description">The description string.</param>
    /// <param name="version">The version string.</param>
    /// <param name="expected">Expected bool result.</param>
    [Theory]
    [InlineData(null, null, true)]
    [InlineData("", "092826", true)]
    [InlineData("   ", "092826", true)]
    [InlineData("playgeneralsonline.com", "092826", true)]
    [InlineData("https://www.playgenerals.online/patchnotes", "092826", true)]
    [InlineData("Generals Online 092826", "092826", true)]
    [InlineData("Generals Online v092826", "v092826", true)]
    [InlineData("GeneralsOnline 092826 (portable)", "092826", true)]
    [InlineData("Generals Online", "092826", true)]
    [InlineData("GeneralsOnline", "092826", true)]
    public void NeedsPatchNotes_PlaceholderDescriptions_ReturnsTrue(string? description, string? version, bool expected)
    {
        Assert.Equal(expected, GeneralsOnlinePatchNotesHelper.NeedsPatchNotes(description, version));
    }

    /// <summary>
    /// Verifies that non-placeholder URLs (such as ModDB or GitHub) return false.
    /// </summary>
    [Fact]
    public void NeedsPatchNotes_UnrelatedUrl_ReturnsFalse()
    {
        Assert.False(GeneralsOnlinePatchNotesHelper.NeedsPatchNotes("https://www.moddb.com/mods/generals", "092826"));
    }

    /// <summary>
    /// Verifies that legitimate short descriptions are not classified as placeholders.
    /// </summary>
    [Fact]
    public void NeedsPatchNotes_LegitimateShortDescription_ReturnsFalse()
    {
        Assert.False(GeneralsOnlinePatchNotesHelper.NeedsPatchNotes("Generals Online hotfix for lobby crash", "092826"));
    }

    /// <summary>
    /// Verifies that rich multi-line changelogs return false.
    /// </summary>
    [Fact]
    public void NeedsPatchNotes_RealChangelog_ReturnsFalse()
    {
        var changelog = "Update 092826 (28th September 2026)\n\n- Fixed camera issue\n- Added tournament lobby";
        Assert.False(GeneralsOnlinePatchNotesHelper.NeedsPatchNotes(changelog, "092826"));
    }

    /// <summary>
    /// Verifies that matching version strings evaluate to true.
    /// </summary>
    /// <param name="versionA">First version.</param>
    /// <param name="versionB">Second version.</param>
    [Theory]
    [InlineData("092826", "092826")]
    [InlineData("092826", "v092826")]
    [InlineData("v092826", "092826")]
    [InlineData("V092826", "v092826")]
    [InlineData("082826_QFE1", "v082826_qfe1")]
    public void VersionsMatch_MatchingVersions_ReturnsTrue(string? versionA, string? versionB)
    {
        Assert.True(GeneralsOnlinePatchNotesHelper.VersionsMatch(versionA, versionB));
    }

    /// <summary>
    /// Verifies that non-matching, null, empty, or degenerate version strings evaluate to false.
    /// </summary>
    /// <param name="versionA">First version.</param>
    /// <param name="versionB">Second version.</param>
    [Theory]
    [InlineData(null, null)]
    [InlineData(null, "092826")]
    [InlineData("092826", null)]
    [InlineData("", "")]
    [InlineData("   ", "   ")]
    [InlineData("v", "V")]
    [InlineData("vv092826", "092826")]
    [InlineData("092826", "092926")]
    public void VersionsMatch_NonMatchingOrDegenerate_ReturnsFalse(string? versionA, string? versionB)
    {
        Assert.False(GeneralsOnlinePatchNotesHelper.VersionsMatch(versionA, versionB));
    }

    /// <summary>
    /// Verifies that WithChangelog copies all properties and updates Changelog.
    /// </summary>
    [Fact]
    public void WithChangelog_PreservesPropertiesAndUpdatesChangelog()
    {
        var original = new GeneralsOnlineRelease
        {
            Version = "092826",
            VersionDate = new DateTime(2026, 9, 28, 0, 0, 0, DateTimeKind.Utc),
            ReleaseDate = new DateTime(2026, 9, 28, 0, 0, 0, DateTimeKind.Utc),
            PortableUrl = "https://cdn.example.com/file.zip",
            PortableSize = 1234567,
            Sha256 = "abcdef123456",
            Changelog = "old changelog",
        };

        var updated = original.WithChangelog("new changelog");

        Assert.Equal("092826", updated.Version);
        Assert.Equal(original.VersionDate, updated.VersionDate);
        Assert.Equal(original.ReleaseDate, updated.ReleaseDate);
        Assert.Equal("https://cdn.example.com/file.zip", updated.PortableUrl);
        Assert.Equal(1234567, updated.PortableSize);
        Assert.Equal("abcdef123456", updated.Sha256);
        Assert.Equal("new changelog", updated.Changelog);
    }
}
