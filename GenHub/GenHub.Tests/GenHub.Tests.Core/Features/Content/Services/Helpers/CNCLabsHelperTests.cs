using System.Threading.Tasks;
using AngleSharp;
using GenHub.Core.Models.Enums;
using GenHub.Features.Content.Services.Helpers;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Content.Services.Helpers;

/// <summary>
/// Unit tests for <see cref="CNCLabsHelper"/> category parsing and breadcrumb extraction.
/// </summary>
public class CNCLabsHelperTests
{
    /// <summary>
    /// Verifies that <see cref="CNCLabsHelper.ParseCategoryString"/> maps known categories to their target game and content type.
    /// </summary>
    /// <param name="input">The category string to test.</param>
    /// <param name="expectedGame">The expected resolved game type.</param>
    /// <param name="expectedType">The expected resolved content type.</param>
    [Theory]
    [InlineData("generals maps", GameType.Generals, ContentType.Map)]
    [InlineData("Generals Maps", GameType.Generals, ContentType.Map)]
    [InlineData("zero hour maps", GameType.ZeroHour, ContentType.Map)]
    [InlineData("Zero Hour Maps", GameType.ZeroHour, ContentType.Map)]
    [InlineData("generals missions", GameType.Generals, ContentType.Mission)]
    [InlineData("zero hour missions", GameType.ZeroHour, ContentType.Mission)]
    [InlineData("generals winamp skins", GameType.Generals, ContentType.Skin)]
    [InlineData("zero hour replays", GameType.ZeroHour, ContentType.Replay)]
    [InlineData("modding and mapping", GameType.Unknown, ContentType.ModdingTool)]
    [InlineData("mods (generals and zero hour)", GameType.Unknown, ContentType.Mod)]
    [InlineData("patches (generals and zero hour)", GameType.Unknown, ContentType.Patch)]
    [InlineData("screensavers", GameType.Unknown, ContentType.Screensaver)]
    [InlineData("videos (generals and zero hour)", GameType.Unknown, ContentType.Video)]
    public void ParseCategoryString_ValidInputs_ReturnsExpectedGameAndContentType(
        string input,
        GameType expectedGame,
        ContentType expectedType)
    {
        var (game, type) = CNCLabsHelper.ParseCategoryString(input);

        Assert.Equal(expectedGame, game);
        Assert.Equal(expectedType, type);
    }

    /// <summary>
    /// Verifies that <see cref="CNCLabsHelper.ParseCategoryString"/> returns Unknown for invalid, null, or unrecognized inputs.
    /// </summary>
    /// <param name="input">The category string to test.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("random unknown category")]
    public void ParseCategoryString_InvalidOrEmpty_ReturnsUnknown(string? input)
    {
        var (game, type) = CNCLabsHelper.ParseCategoryString(input);

        Assert.Equal(GameType.Unknown, game);
        Assert.Equal(ContentType.UnknownContentType, type);
    }

    /// <summary>
    /// Verifies that modern nav breadcrumb items take first priority when extracting category.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ExtractBreadcrumbCategory_NavElement_ReturnsCategoryFromBreadcrumbItemAsync()
    {
        const string html = "<html><body><ol class='breadcrumb'><li class='breadcrumb-item'><a href='#'>All Downloads</a></li><li class='breadcrumb-item'><a href='#'>C&C Generals</a></li><li class='breadcrumb-item active'>Zero Hour Maps</li></ol></body></html>";

        var context = BrowsingContext.New(Configuration.Default);
        var document = await context.OpenAsync(req => req.Content(html));

        var (game, type) = CNCLabsHelper.ExtractBreadcrumbCategory(document);

        Assert.Equal(GameType.ZeroHour, game);
        Assert.Equal(ContentType.Map, type);
    }

    /// <summary>
    /// Verifies that title is used as fallback when nav breadcrumbs are absent.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ExtractBreadcrumbCategory_TitleFallback_WhenNavMissing_ReturnsFromTitleAsync()
    {
        const string html = "<html><head><title>Operation Desert - Zero Hour Missions - CNC Labs</title></head><body><div>No breadcrumb nav here</div></body></html>";

        var context = BrowsingContext.New(Configuration.Default);
        var document = await context.OpenAsync(req => req.Content(html));

        var (game, type) = CNCLabsHelper.ExtractBreadcrumbCategory(document);

        Assert.Equal(GameType.ZeroHour, game);
        Assert.Equal(ContentType.Mission, type);
    }

    /// <summary>
    /// Verifies that legacy h1 separator breadcrumb is used when nav and title are absent.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ExtractBreadcrumbCategory_LegacyHeaderFallback_WhenNavAndTitleMissing_ReturnsFromH1Async()
    {
        const string html = "<html><head><title>Generic Page</title></head><body><h1>All Downloads \u00bb Generals \u00bb Generals Maps \u00bb Scorpion Cell</h1></body></html>";

        var context = BrowsingContext.New(Configuration.Default);
        var document = await context.OpenAsync(req => req.Content(html));

        var (game, type) = CNCLabsHelper.ExtractBreadcrumbCategory(document);

        Assert.Equal(GameType.Generals, game);
        Assert.Equal(ContentType.Map, type);
    }

    /// <summary>
    /// Verifies that unknown category is returned when no breadcrumbs, title matches, or headers exist.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ExtractBreadcrumbCategory_WhenNothingMatches_ReturnsUnknownAsync()
    {
        const string html = "<html><head><title>Unknown Page</title></head><body><p>Hello world</p></body></html>";

        var context = BrowsingContext.New(Configuration.Default);
        var document = await context.OpenAsync(req => req.Content(html));

        var (game, type) = CNCLabsHelper.ExtractBreadcrumbCategory(document);

        Assert.Equal(GameType.Unknown, game);
        Assert.Equal(ContentType.UnknownContentType, type);
    }
}
