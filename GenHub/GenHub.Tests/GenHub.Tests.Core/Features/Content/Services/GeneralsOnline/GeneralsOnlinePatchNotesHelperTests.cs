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
    [InlineData("www.playgenerals.online", "092826", true)]
    [InlineData("https://www.playgenerals.online/patchnotes", "092826", true)]
    [InlineData("Generals Online 092826", "092826", true)]
    [InlineData("Generals Online v092826", "v092826", true)]
    [InlineData("GeneralsOnline 092826 portable release", "092826", true)]
    [InlineData("Generals Online 092526", "092526", true)]
    [InlineData("Generals Online", "092826", true)]
    public void NeedsPatchNotes_PlaceholderDescriptions_ReturnsTrue(string? description, string? version, bool expected)
    {
        Assert.Equal(expected, GeneralsOnlinePatchNotesHelper.NeedsPatchNotes(description, version));
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
}
