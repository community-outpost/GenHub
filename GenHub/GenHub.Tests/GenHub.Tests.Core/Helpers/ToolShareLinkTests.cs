using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Models.Enums;
using System;
using Xunit;

namespace GenHub.Tests.Core.Helpers;

/// <summary>
/// Unit tests for <see cref="ToolShareLink"/>.
/// </summary>
public sealed class ToolShareLinkTests
{
    private const string MapDownloadUrl = "https://example.com/maps/cool_map.zip";
    private const string ReplayDownloadUrl = "https://50ea2z8yuk.ufs.sh/f/ZlHfBAzftgeLJxG1453BRquaUgnl90MjYIFymdAfOpCs67GN";

    /// <summary>
    /// Verifies that BuildShareUri builds a map share URI carrying the game value.
    /// </summary>
    [Fact]
    public void BuildShareUri_WithMapAndGame_ReturnsShareUri()
    {
        var result = ToolShareLink.BuildShareUri(CommandLineConstants.MapCommand, MapDownloadUrl, GameType.Generals);

        Assert.Equal(
            $"{CommandLineConstants.MapImportUriPrefix}?url={Uri.EscapeDataString(MapDownloadUrl)}&game=generals",
            result);
    }

    /// <summary>
    /// Verifies that BuildShareUri builds a replay share URI without a game value when none is provided.
    /// </summary>
    [Fact]
    public void BuildShareUri_WithoutGame_OmitsGameParameter()
    {
        var result = ToolShareLink.BuildShareUri(CommandLineConstants.ReplayCommand, ReplayDownloadUrl);

        Assert.Equal(
            $"{CommandLineConstants.ReplayImportUriPrefix}?url={Uri.EscapeDataString(ReplayDownloadUrl)}",
            result);
    }

    /// <summary>
    /// Verifies that BuildShareUri rejects unknown tool commands.
    /// </summary>
    [Fact]
    public void BuildShareUri_WithUnknownTool_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => ToolShareLink.BuildShareUri("profiles", MapDownloadUrl, GameType.ZeroHour));
    }

    /// <summary>
    /// Verifies that BuildShareUri rejects unknown game types instead of serializing them as Zero Hour.
    /// </summary>
    [Fact]
    public void BuildShareUri_WithUnknownGame_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => ToolShareLink.BuildShareUri(CommandLineConstants.MapCommand, MapDownloadUrl, GameType.Unknown));
    }

    /// <summary>
    /// Verifies that BuildShareUri rejects download URLs that are not absolute HTTP or HTTPS URLs.
    /// </summary>
    /// <param name="innerUrl">The invalid download URL.</param>
    [Theory]
    [InlineData("ftp://example.com/maps.zip")]
    [InlineData("javascript:alert(1)")]
    [InlineData("not-a-url")]
    [InlineData("")]
    public void BuildShareUri_WithInvalidUrl_ThrowsArgumentException(string innerUrl)
    {
        Assert.Throws<ArgumentException>(() => ToolShareLink.BuildShareUri(CommandLineConstants.MapCommand, innerUrl, GameType.ZeroHour));
    }

    /// <summary>
    /// Verifies that TryParseShareUri parses a map share URI with a game value.
    /// </summary>
    [Fact]
    public void TryParseShareUri_WithMapUri_ReturnsTarget()
    {
        var input = $"{CommandLineConstants.MapImportUriPrefix}?url={Uri.EscapeDataString(MapDownloadUrl)}&game=zerohour";

        var parsed = ToolShareLink.TryParseShareUri(input, out var target);

        Assert.True(parsed);
        Assert.NotNull(target);
        Assert.Equal(CommandLineConstants.MapCommand, target.ToolCommand);
        Assert.Equal(MapDownloadUrl, target.Url);
        Assert.Equal(GameType.ZeroHour, target.Game);
    }

    /// <summary>
    /// Verifies that TryParseShareUri parses a replay share URI without a game value.
    /// </summary>
    [Fact]
    public void TryParseShareUri_WithoutGame_ReturnsNullGame()
    {
        var input = $"{CommandLineConstants.ReplayImportUriPrefix}?url={Uri.EscapeDataString(ReplayDownloadUrl)}";

        var parsed = ToolShareLink.TryParseShareUri(input, out var target);

        Assert.True(parsed);
        Assert.NotNull(target);
        Assert.Equal(CommandLineConstants.ReplayCommand, target.ToolCommand);
        Assert.Equal(ReplayDownloadUrl, target.Url);
        Assert.Null(target.Game);
    }

    /// <summary>
    /// Verifies that TryParseShareUri accepts query parameters in any order.
    /// </summary>
    [Fact]
    public void TryParseShareUri_WithReorderedParameters_ReturnsTarget()
    {
        var input = $"{CommandLineConstants.MapImportUriPrefix}?game=generals&url={Uri.EscapeDataString(MapDownloadUrl)}";

        var parsed = ToolShareLink.TryParseShareUri(input, out var target);

        Assert.True(parsed);
        Assert.NotNull(target);
        Assert.Equal(MapDownloadUrl, target.Url);
        Assert.Equal(GameType.Generals, target.Game);
    }

    /// <summary>
    /// Verifies that TryParseShareUri tolerates surrounding quotes, whitespace, and a trailing path slash.
    /// </summary>
    [Fact]
    public void TryParseShareUri_WithQuotesAndTrailingSlash_ReturnsTarget()
    {
        var input = $"  \"{CommandLineConstants.ReplayImportUriPrefix}/?url={Uri.EscapeDataString(ReplayDownloadUrl)}\"  ";

        var parsed = ToolShareLink.TryParseShareUri(input, out var target);

        Assert.True(parsed);
        Assert.NotNull(target);
        Assert.Equal(ReplayDownloadUrl, target.Url);
    }

    /// <summary>
    /// Verifies that TryParseShareUri rejects malformed or foreign inputs.
    /// </summary>
    /// <param name="input">The invalid input.</param>
    [Theory]
    [InlineData("genhub://map/import")]
    [InlineData("genhub://map/import?game=generals")]
    [InlineData("genhub://map/import?url=not-a-url")]
    [InlineData("genhub://map/import?url=ftp://example.com/maps.zip")]
    [InlineData("genhub://map/import?url=https://example.com/maps.zip&game=unknown")]
    [InlineData("genhub://subscribe?url=https://example.com/catalog.json")]
    [InlineData("genhub://profile/import?data=abc")]
    [InlineData("https://example.com/maps.zip")]
    [InlineData("")]
    [InlineData("   ")]
    public void TryParseShareUri_WithInvalidInput_ReturnsFalse(string input)
    {
        var parsed = ToolShareLink.TryParseShareUri(input, out var target);

        Assert.False(parsed);
        Assert.Null(target);
    }

    /// <summary>
    /// Verifies that TryParseShareUri returns false for null input.
    /// </summary>
    [Fact]
    public void TryParseShareUri_WithNullInput_ReturnsFalse()
    {
        var parsed = ToolShareLink.TryParseShareUri(null, out var target);

        Assert.False(parsed);
        Assert.Null(target);
    }

    /// <summary>
    /// Verifies that NormalizeImportUrl unwraps matching share URIs and passes other inputs through.
    /// </summary>
    [Fact]
    public void NormalizeImportUrl_UnwrapsMatchingToolOnly()
    {
        var mapUri = ToolShareLink.BuildShareUri(CommandLineConstants.MapCommand, MapDownloadUrl, GameType.ZeroHour);
        var replayUri = ToolShareLink.BuildShareUri(CommandLineConstants.ReplayCommand, ReplayDownloadUrl, GameType.ZeroHour);

        Assert.Equal(MapDownloadUrl, ToolShareLink.NormalizeImportUrl(mapUri, CommandLineConstants.MapCommand));
        Assert.Equal(replayUri, ToolShareLink.NormalizeImportUrl(replayUri, CommandLineConstants.MapCommand));
        Assert.Equal(MapDownloadUrl, ToolShareLink.NormalizeImportUrl(MapDownloadUrl, CommandLineConstants.MapCommand));
    }

    /// <summary>
    /// Verifies that IsOtherToolShareUri detects cross-tool share URIs.
    /// </summary>
    [Fact]
    public void IsOtherToolShareUri_DetectsCrossToolLinks()
    {
        var mapUri = ToolShareLink.BuildShareUri(CommandLineConstants.MapCommand, MapDownloadUrl, GameType.ZeroHour);

        Assert.True(ToolShareLink.IsOtherToolShareUri(mapUri, CommandLineConstants.ReplayCommand));
        Assert.False(ToolShareLink.IsOtherToolShareUri(mapUri, CommandLineConstants.MapCommand));
        Assert.False(ToolShareLink.IsOtherToolShareUri(MapDownloadUrl, CommandLineConstants.ReplayCommand));
        Assert.False(ToolShareLink.IsOtherToolShareUri(null, CommandLineConstants.ReplayCommand));
    }

    /// <summary>
    /// Verifies that HasShareUriScheme detects the GenHub scheme regardless of validity.
    /// </summary>
    /// <param name="input">The raw input.</param>
    /// <param name="expected">Whether the GenHub scheme is expected.</param>
    [Theory]
    [InlineData("genhub://replay/import?url=https%3A%2F%2Fexample.com%2Freplay.rep", true)]
    [InlineData("genhub://map/import", true)]
    [InlineData("genhub://subscribe?url=https://example.com/catalog.json", true)]
    [InlineData("  \"genhub://map/import?url=https://example.com/maps.zip\"  ", true)]
    [InlineData("GENHUB://map/import?url=https://example.com/maps.zip", true)]
    [InlineData("https://example.com/maps.zip", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData(null, false)]
    public void HasShareUriScheme_DetectsGenHubScheme(string? input, bool expected)
    {
        Assert.Equal(expected, ToolShareLink.HasShareUriScheme(input));
    }

    /// <summary>
    /// Verifies that BuildIpcCommand builds tool-specific IPC commands.
    /// </summary>
    [Fact]
    public void BuildIpcCommand_WithShareUris_ReturnsPrefixedCommands()
    {
        var mapUri = ToolShareLink.BuildShareUri(CommandLineConstants.MapCommand, MapDownloadUrl, GameType.ZeroHour);
        var replayUri = ToolShareLink.BuildShareUri(CommandLineConstants.ReplayCommand, ReplayDownloadUrl, GameType.Generals);

        Assert.Equal($"{IpcCommands.ImportMapPrefix}{mapUri}", ToolShareLink.BuildIpcCommand(mapUri));
        Assert.Equal($"{IpcCommands.ImportReplayPrefix}{replayUri}", ToolShareLink.BuildIpcCommand(replayUri));
    }

    /// <summary>
    /// Verifies that BuildIpcCommand returns null for missing or malformed share URIs.
    /// </summary>
    [Fact]
    public void BuildIpcCommand_WithInvalidInput_ReturnsNull()
    {
        Assert.Null(ToolShareLink.BuildIpcCommand(null));
        Assert.Null(ToolShareLink.BuildIpcCommand(string.Empty));
        Assert.Null(ToolShareLink.BuildIpcCommand("genhub://map/import"));
        Assert.Null(ToolShareLink.BuildIpcCommand(MapDownloadUrl));
    }

    /// <summary>
    /// Verifies that BuildShareUri and TryParseShareUri round-trip.
    /// </summary>
    [Fact]
    public void BuildAndParse_RoundTrips()
    {
        var built = ToolShareLink.BuildShareUri(CommandLineConstants.ReplayCommand, ReplayDownloadUrl, GameType.Generals);

        var parsed = ToolShareLink.TryParseShareUri(built, out var target);

        Assert.True(parsed);
        Assert.NotNull(target);
        Assert.Equal(CommandLineConstants.ReplayCommand, target.ToolCommand);
        Assert.Equal(ReplayDownloadUrl, target.Url);
        Assert.Equal(GameType.Generals, target.Game);
    }
}
