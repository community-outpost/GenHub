using FluentAssertions;
using GenHub.Features.Content.Services.ContentDiscoverers;
using Xunit;

namespace GenHub.Tests.Core.Features.Content;

/// <summary>
/// Unit and regression tests for <see cref="GitHubTopicsDiscoverer"/>.
/// </summary>
public class GitHubTopicsDiscovererTests
{
    /// <summary>
    /// Verifies that KResolutionPattern matches underscore-delimited resolution tokens while rejecting embedded numbers.
    /// </summary>
    /// <param name="input">The asset or variant name.</param>
    /// <param name="expectedMatch">Whether a match is expected.</param>
    /// <param name="expectedValue">The matched K token digit if matched.</param>
    [Theory]
    [InlineData("textures_4K.zip", true, "4")]
    [InlineData("4K_pack.zip", true, "4")]
    [InlineData("Asset_2K_release.zip", true, "2")]
    [InlineData("Asset_8K_v1.zip", true, "8")]
    [InlineData("Asset_5K_v1.zip", true, "5")]
    [InlineData("GenTool_2K.zip", true, "2")]
    [InlineData("48K.zip", false, null)]
    [InlineData("2K19_mod.zip", false, null)]
    [InlineData("regular_file.zip", false, null)]
    public void KResolutionPattern_MatchesExpectedTokens(string input, bool expectedMatch, string? expectedValue)
    {
        var match = GitHubTopicsDiscoverer.VariantPatterns.KResolutionPattern().Match(input);

        match.Success.Should().Be(expectedMatch);
        if (expectedMatch)
        {
            match.Groups[1].Value.Should().Be(expectedValue);
        }
    }
}
