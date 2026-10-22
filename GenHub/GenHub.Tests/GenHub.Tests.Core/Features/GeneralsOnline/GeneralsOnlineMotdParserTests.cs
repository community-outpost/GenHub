using GenHub.Core.Helpers;

namespace GenHub.Tests.Core.Features.GeneralsOnline;

/// <summary>
/// Unit tests for <see cref="GeneralsOnlineMotdParser"/> color code handling.
/// </summary>
public class GeneralsOnlineMotdParserTests
{
    /// <summary>
    /// Tests that null or empty input parses to no runs.
    /// </summary>
    /// <param name="motd">The raw MOTD text.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Parse_NullOrEmpty_ShouldReturnEmpty(string? motd)
    {
        // Act
        var runs = GeneralsOnlineMotdParser.Parse(motd);

        // Assert
        Assert.Empty(runs);
    }

    /// <summary>
    /// Tests that text without codes parses to one default run.
    /// </summary>
    [Fact]
    public void Parse_WithoutCodes_ShouldReturnSingleDefaultRun()
    {
        // Act
        var runs = GeneralsOnlineMotdParser.Parse("There are currently 234 player(s) online.");

        // Assert
        var run = Assert.Single(runs);
        Assert.Equal("There are currently 234 player(s) online.", run.Text);
        Assert.Null(run.ColorHex);
    }

    /// <summary>
    /// Tests that a leading color code colors the run with its RGB part.
    /// </summary>
    [Fact]
    public void Parse_WithLeadingColorCode_ShouldColorRun()
    {
        // Act
        var runs = GeneralsOnlineMotdParser.Parse("\\ffff7f2f========= Outpost Live LAN Event =========");

        // Assert
        var run = Assert.Single(runs);
        Assert.Equal("========= Outpost Live LAN Event =========", run.Text);
        Assert.Equal("#FF7F2F", run.ColorHex);
    }

    /// <summary>
    /// Tests that multiple codes split the text into matching runs.
    /// </summary>
    [Fact]
    public void Parse_WithMultipleCodes_ShouldSplitRuns()
    {
        // Act
        var runs = GeneralsOnlineMotdParser.Parse("Players online.\n\\ffff0000 Code of Conduct: example\n\\ffff7f2fBack to orange");

        // Assert
        Assert.Equal(3, runs.Count);
        Assert.Equal("Players online.\n", runs[0].Text);
        Assert.Null(runs[0].ColorHex);
        Assert.Equal(" Code of Conduct: example\n", runs[1].Text);
        Assert.Equal("#FF0000", runs[1].ColorHex);
        Assert.Equal("Back to orange", runs[2].Text);
        Assert.Equal("#FF7F2F", runs[2].ColorHex);
    }

    /// <summary>
    /// Tests that repeated identical codes merge into one run.
    /// </summary>
    [Fact]
    public void Parse_WithRepeatedColorCode_ShouldMergeRuns()
    {
        // Act
        var runs = GeneralsOnlineMotdParser.Parse("\\ffff7f2fOne\\ffff7f2fTwo");

        // Assert
        var run = Assert.Single(runs);
        Assert.Equal("OneTwo", run.Text);
        Assert.Equal("#FF7F2F", run.ColorHex);
    }

    /// <summary>
    /// Tests that malformed codes stay literal text.
    /// </summary>
    /// <param name="motd">The raw MOTD text.</param>
    [Theory]
    [InlineData("Price \\zzzzzzzz here")]
    [InlineData("Truncated \\ffff7f2")]
    [InlineData("Trailing backslash \\")]
    public void Parse_WithMalformedCode_ShouldKeepLiteral(string motd)
    {
        // Act
        var runs = GeneralsOnlineMotdParser.Parse(motd);

        // Assert
        var run = Assert.Single(runs);
        Assert.Equal(motd, run.Text);
        Assert.Null(run.ColorHex);
    }

    /// <summary>
    /// Tests that stripping removes codes and keeps display text.
    /// </summary>
    [Fact]
    public void StripColorCodes_ShouldRemoveCodesKeepText()
    {
        // Act
        var stripped = GeneralsOnlineMotdParser.StripColorCodes("Online.\n\\ffff0000 Rules\n\\ffff7f2fLinks");

        // Assert
        Assert.Equal("Online.\n Rules\nLinks", stripped);
    }

    /// <summary>
    /// Tests that stripping null returns an empty string.
    /// </summary>
    [Fact]
    public void StripColorCodes_Null_ShouldReturnEmpty()
    {
        // Act
        var stripped = GeneralsOnlineMotdParser.StripColorCodes(null);

        // Assert
        Assert.Equal(string.Empty, stripped);
    }
}
