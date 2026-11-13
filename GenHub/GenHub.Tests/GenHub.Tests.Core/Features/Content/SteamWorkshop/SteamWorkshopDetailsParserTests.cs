using GenHub.Features.Content.Services.SteamWorkshop;
using Xunit;

namespace GenHub.Tests.Core.Features.Content.SteamWorkshop;

/// <summary>
/// Unit tests for <see cref="SteamWorkshopDetailsParser"/>.
/// </summary>
public sealed class SteamWorkshopDetailsParserTests
{
    private const string DetailsHtml = """
        <html><head><title>Steam Workshop::[SUPZH] USA10 Shock And Awe</title></head><body>
        <img id="previewImageMain" class="workshopItemPreviewImageMain" src="https://images.steamusercontent.com/ugc/main.jpg"/>
        <div class="workshopItemDescription" id="highlightContent">[Operation : SHOCK AND AWE] <br>Welcome back to Baghdad.</div>
        <div class="friendBlockContent">
                        SUPZH<br>
                        <span class="friendSmallText">Offline</span>
                    </div>
        <div data-panel="x" class="workshopTags"><span class="workshopTagsTitle">Map:&nbsp;</span><a href="https://steamcommunity.com/workshop/browse/?appid=2732960&amp;requiredtags%5B%5D=Single+Player">Single Player</a>, <a href="https://steamcommunity.com/workshop/browse/?appid=2732960&amp;requiredtags%5B%5D=Detailed">Detailed</a></div>
        <div class="detailsStatsContainerLeft"><div class="detailsStatLeft">File Size </div><div class="detailsStatLeft">Posted </div><div class="detailsStatLeft">Updated </div></div>
        <div class="detailsStatsContainerRight"><div class="detailsStatRight">670.551 KB</div><div class="detailsStatRight">26 Aug @ 4:25am</div><div class="detailsStatRight">27 Aug, 2024 @ 5:30am</div></div>
        </body></html>
        """;

    /// <summary>
    /// Verifies details HTML parses into structured data.
    /// </summary>
    [Fact]
    public void Parse_DetailsHtml_ReturnsDetails()
    {
        var details = SteamWorkshopDetailsParser.Parse(DetailsHtml);

        Assert.Equal("[SUPZH] USA10 Shock And Awe", details.Title);
        Assert.Equal("SUPZH", details.Author);
        Assert.Equal("https://images.steamusercontent.com/ugc/main.jpg", details.PreviewUrl);
        Assert.Contains("Welcome back to Baghdad", details.DescriptionHtml);
        Assert.Equal("670.551 KB", details.FileSizeText);
        Assert.NotNull(details.Posted);
        Assert.Equal(new System.DateTime(2024, 8, 27, 5, 30, 0, System.DateTimeKind.Utc), details.Updated);
        Assert.Equal(["Single Player", "Detailed"], details.Tags);
    }

    /// <summary>
    /// Verifies empty HTML yields empty details.
    /// </summary>
    /// <param name="html">The raw HTML.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("<html><body>Nothing here</body></html>")]
    public void Parse_EmptyHtml_ReturnsEmptyDetails(string? html)
    {
        var details = SteamWorkshopDetailsParser.Parse(html);

        Assert.Null(details.Title);
        Assert.Null(details.DescriptionHtml);
        Assert.Null(details.Author);
        Assert.Null(details.PreviewUrl);
        Assert.Null(details.FileSizeText);
        Assert.Null(details.Posted);
        Assert.Null(details.Updated);
        Assert.Empty(details.Tags);
    }

    /// <summary>
    /// Verifies duplicate tags are returned once.
    /// </summary>
    [Fact]
    public void Parse_DuplicateTags_ReturnsDistinctTags()
    {
        const string html = """<div class="workshopTags"><a href="x">Map</a></div><div class="workshopTags"><a href="y">map</a></div>""";

        var details = SteamWorkshopDetailsParser.Parse(html);

        Assert.Equal(["Map"], details.Tags);
    }

    /// <summary>
    /// Verifies nested description markup is preserved instead of truncated.
    /// </summary>
    [Fact]
    public void Parse_NestedDescriptionDivs_ReturnsFullDescription()
    {
        const string html = """<div class="workshopItemDescription" id="highlightContent">Intro <div class="quote">quoted <div>deep</div></div> outro</div>""";

        var details = SteamWorkshopDetailsParser.Parse(html);

        Assert.Contains("deep", details.DescriptionHtml, System.StringComparison.Ordinal);
        Assert.Contains("outro", details.DescriptionHtml, System.StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies preview images resolve regardless of attribute order.
    /// </summary>
    [Fact]
    public void Parse_PreviewSrcBeforeId_ReturnsPreviewUrl()
    {
        const string html = """<img src="https://example.com/main.jpg" id="previewImageMain" class="workshopItemPreviewImageMain"/>""";

        var details = SteamWorkshopDetailsParser.Parse(html);

        Assert.Equal("https://example.com/main.jpg", details.PreviewUrl);
    }

    /// <summary>
    /// Verifies mismatched stat labels and values are dropped instead of misaligned.
    /// </summary>
    [Fact]
    public void Parse_MismatchedStats_ReturnsEmptyStats()
    {
        const string html = """
            <div class="detailsStatLeft">File Size</div>
            <div class="detailsStatLeft">Posted</div>
            <div class="detailsStatRight">1.5 MB</div>
            """;

        var details = SteamWorkshopDetailsParser.Parse(html);

        Assert.Null(details.FileSizeText);
        Assert.Null(details.Posted);
    }

    /// <summary>
    /// Verifies the workshop application ID is parsed for game detection.
    /// </summary>
    [Fact]
    public void Parse_AuthorLink_ReturnsAppId()
    {
        const string html = """<a href="https://steamcommunity.com/profiles/1/myworkshopfiles/?appid=2732960">SUPZH</a>""";

        var details = SteamWorkshopDetailsParser.Parse(html);

        Assert.Equal(2732960, details.AppId);
    }

    /// <summary>
    /// Verifies gallery screenshots exclude the main preview, chrome, and avatars.
    /// </summary>
    [Fact]
    public void Parse_GalleryHtml_ReturnsScreenshots()
    {
        const string html = """
            <html><body>
            <img id="previewImageMain" src="https://images.steamusercontent.com/ugc/main.jpg"/>
            <img src="https://images.steamusercontent.com/ugc/shot1.jpg"/>
            <img src="https://images.steamusercontent.com/ugc/shot2.jpg"/>
            <img src="https://images.steamusercontent.com/ugc/main.jpg"/>
            <img src="https://community.fastly.steamstatic.com/public/shared/images/header/logo_steam.svg"/>
            <img src="https://avatars.cloudflare.steamstatic.com/avatar.jpg"/>
            <div class="workshopItemDescription" id="highlightContent">Desc <img src="https://images.steamusercontent.com/ugc/inline.jpg"/></div>
            </body></html>
            """;

        var details = SteamWorkshopDetailsParser.Parse(html);

        Assert.Equal(["https://images.steamusercontent.com/ugc/shot1.jpg", "https://images.steamusercontent.com/ugc/shot2.jpg"], details.Screenshots);
    }

    /// <summary>
    /// Verifies the creator avatar is parsed from the author block.
    /// </summary>
    [Fact]
    public void Parse_CreatorBlock_ReturnsAvatarUrl()
    {
        const string html = """
            <img src="https://avatars.cloudflare.steamstatic.com/loggedin.jpg"/>
            <div class="friendBlockAvatar"><a href="https://steamcommunity.com/profiles/1"><img src="https://avatars.cloudflare.steamstatic.com/creator_full.jpg"></a></div>
            <div class="friendBlockContent">Mapper<br></div>
            """;

        var details = SteamWorkshopDetailsParser.Parse(html);

        Assert.Equal("https://avatars.cloudflare.steamstatic.com/creator_full.jpg", details.CreatorAvatarUrl);
    }

    /// <summary>
    /// Verifies the creator avatar is parsed from the playerAvatar class marker.
    /// </summary>
    [Fact]
    public void Parse_PlayerAvatarMarker_ReturnsAvatarUrl()
    {
        const string html = """
            <div class="playerAvatar"><img src="https://avatars.cloudflare.steamstatic.com/player_avatar.jpg"></div>
            """;

        var details = SteamWorkshopDetailsParser.Parse(html);

        Assert.Equal("https://avatars.cloudflare.steamstatic.com/player_avatar.jpg", details.CreatorAvatarUrl);
    }

    /// <summary>
    /// Verifies all details stats are exposed for subscriber counts and dates.
    /// </summary>
    [Fact]
    public void Parse_StatsHtml_ReturnsStats()
    {
        const string html = """
            <div class="detailsStatsContainerLeft"><div class="detailsStatLeft">File Size </div><div class="detailsStatLeft">Current Subscribers </div></div>
            <div class="detailsStatsContainerRight"><div class="detailsStatRight">670.551 KB</div><div class="detailsStatRight">1,234</div></div>
            """;

        var details = SteamWorkshopDetailsParser.Parse(html);

        Assert.NotNull(details.Stats);
        Assert.Equal("1,234", details.Stats["Current Subscribers"]);
    }

    /// <summary>
    /// Verifies pages without gallery or avatar content yield empty extras.
    /// </summary>
    [Fact]
    public void Parse_NoExtras_ReturnsEmptyExtras()
    {
        var details = SteamWorkshopDetailsParser.Parse(DetailsHtml);

        Assert.Empty(details.Screenshots ?? []);
        Assert.Null(details.CreatorAvatarUrl);
    }

    /// <summary>
    /// Verifies sized Steam thumbnails upgrade to full-resolution originals.
    /// </summary>
    [Fact]
    public void Parse_SizedThumbnails_ReturnsFullResolutionUrls()
    {
        const string html = """
            <html><body>
            <img id="previewImageMain" src="https://images.steamusercontent.com/ugc/1/aaa/?imw=268&imh=268&ima=fit"/>
            <img src="https://images.steamusercontent.com/ugc/2/bbb/?imw=116&imh=65&ima=fit&impolicy=Letterbox&imcolor=%23000000&letterbox=true"/>
            </body></html>
            """;

        var details = SteamWorkshopDetailsParser.Parse(html);

        Assert.Equal("https://images.steamusercontent.com/ugc/1/aaa/", details.PreviewUrl);
        Assert.Equal(["https://images.steamusercontent.com/ugc/2/bbb/"], details.Screenshots);
    }
}
