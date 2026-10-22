using GenHub.Features.Content.Services.SteamWorkshop;
using Xunit;

namespace GenHub.Tests.Core.Features.Content.SteamWorkshop;

/// <summary>
/// Unit tests for <see cref="SteamWorkshopBrowseParser"/>.
/// </summary>
public sealed class SteamWorkshopBrowseParserTests
{
    private const string BrowseHtml = """
        <div class="I2YQ9xth4Xw-">552 entries matching filters</div>
        <div class="UNowfeldbNg- Panel"><div><a href="https://steamcommunity.com/sharedfiles/filedetails/?id=3790356853" class="tK5agp5sRy8-"><img src="https://images.steamusercontent.com/ugc/preview1.jpg" alt="[SUPZH] USA10 Shock And Awe" loading="lazy" class=""/></a></div><div><div><a href="https://steamcommunity.com/sharedfiles/filedetails/?id=3790356853">[SUPZH] USA10 Shock And Awe</a></div><div><a href="https://steamcommunity.com/profiles/76561198886953206/myworkshopfiles/?appid=2732960">By SUPZH</a></div></div></div>
        <div class="UNowfeldbNg- Panel"><div><a href="https://steamcommunity.com/sharedfiles/filedetails/?id=3435144917" class="tK5agp5sRy8-"><img src="https://images.steamusercontent.com/ugc/preview2.jpg" alt="Desert Duel" loading="lazy" class=""/></a></div><div><div><a href="https://steamcommunity.com/sharedfiles/filedetails/?id=3435144917">Desert Duel</a></div><div><a href="https://steamcommunity.com/id/someguy/myworkshopfiles/?appid=2732960">By SomeGuy</a></div></div></div>
        """;

    /// <summary>
    /// Verifies browse HTML parses into items with IDs, titles, authors, and previews.
    /// </summary>
    [Fact]
    public void Parse_BrowseHtml_ReturnsItems()
    {
        var (items, total) = SteamWorkshopBrowseParser.Parse(BrowseHtml);

        Assert.Equal(2, items.Count);
        Assert.Equal(552, total);
        Assert.Equal("3790356853", items[0].PublishedFileId);
        Assert.Equal("[SUPZH] USA10 Shock And Awe", items[0].Title);
        Assert.Equal("SUPZH", items[0].Author);
        Assert.Equal("https://steamcommunity.com/profiles/76561198886953206", items[0].AuthorProfileUrl);
        Assert.Equal("76561198886953206", items[0].CreatorId);
        Assert.Equal("https://images.steamusercontent.com/ugc/preview1.jpg", items[0].PreviewUrl);
        Assert.Contains("id=3790356853", items[0].DetailsUrl);
        Assert.Equal("3435144917", items[1].PublishedFileId);
        Assert.Equal("Desert Duel", items[1].Title);
        Assert.Equal("SomeGuy", items[1].Author);
        Assert.Equal("https://steamcommunity.com/id/someguy", items[1].AuthorProfileUrl);
    }

    /// <summary>
    /// Verifies items without an author link still parse.
    /// </summary>
    [Fact]
    public void Parse_ItemWithoutAuthor_ReturnsItemWithNullAuthor()
    {
        const string html = """<a href="https://steamcommunity.com/sharedfiles/filedetails/?id=1" class="x"><img src="https://example.com/p.jpg" alt="Lonely Map"/></a><a href="https://steamcommunity.com/sharedfiles/filedetails/?id=1">Lonely Map</a>""";

        var (items, total) = SteamWorkshopBrowseParser.Parse(html);

        Assert.Single(items);
        Assert.Equal("Lonely Map", items[0].Title);
        Assert.Null(items[0].Author);
        Assert.Null(total);
    }

    /// <summary>
    /// Verifies embedded creator avatar hashes are parsed and converted to fastly CDN URLs.
    /// </summary>
    [Fact]
    public void Parse_BrowseHtml_WithEmbeddedCreatorAvatars_ExtractsAvatarUrl()
    {
        const string htmlWithEmbeddedState = """
            <script>
            const data = {"publishedfileid":"3790356853","creator":"76561198886953206"};
            const avatars = {"steamid":"76561198886953206","sha_digest_avatar":{"_t":0,"v":[81,55,7,252,234,117,113,228,14,232,1,101,202,242,150,249,236,160,10,49]}};
            </script>
            <div><a href="https://steamcommunity.com/sharedfiles/filedetails/?id=3790356853"><img src="https://example.com/p.jpg"/></a><a href="https://steamcommunity.com/sharedfiles/filedetails/?id=3790356853">Shock And Awe</a><a href="https://steamcommunity.com/profiles/76561198886953206/myworkshopfiles/?appid=2732960">By SUPZH</a></div>
            """;

        var (items, _) = SteamWorkshopBrowseParser.Parse(htmlWithEmbeddedState);

        var item = Assert.Single(items);
        Assert.Equal("3790356853", item.PublishedFileId);
        Assert.Equal("76561198886953206", item.CreatorId);
        Assert.Equal("https://avatars.fastly.steamstatic.com/513707fcea7571e40ee80165caf296f9eca00a31_full.jpg", item.CreatorAvatarUrl);
    }

    /// <summary>
    /// Verifies live SSR browse HTML with triply-escaped JSON quotes parses creator IDs and avatar digests.
    /// </summary>
    [Fact]
    public void Parse_BrowseHtml_WithTripleEscapedCreatorAvatars_ExtractsAvatarUrl()
    {
        const string htmlWithTripleEscapedState = """
            <script>
            const raw = "{\"publishedfileid\\\":\\\"3437830737\\\",\\\"creator\\\":\\\"76561198886953206\\\"}";
            const avatars = "{\"steamid\\\":\\\"76561198886953206\\\",\\\"sha_digest_avatar\\\":{\\\"_t\\\":0,\\\"v\\\":[81,55,7,252,234,117,113,228,14,232,1,101,202,242,150,249,236,160,10,49]}}";
            </script>
            <div><a href="https://steamcommunity.com/sharedfiles/filedetails/?id=3437830737"><img src="https://example.com/p.jpg"/></a><a href="https://steamcommunity.com/sharedfiles/filedetails/?id=3437830737">Triple Escaped Map</a><a href="https://steamcommunity.com/profiles/76561198886953206/myworkshopfiles/?appid=2732960">By SUPZH</a></div>
            """;

        var (items, _) = SteamWorkshopBrowseParser.Parse(htmlWithTripleEscapedState);

        var item = Assert.Single(items);
        Assert.Equal("3437830737", item.PublishedFileId);
        Assert.Equal("76561198886953206", item.CreatorId);
        Assert.Equal("https://avatars.fastly.steamstatic.com/513707fcea7571e40ee80165caf296f9eca00a31_full.jpg", item.CreatorAvatarUrl);
    }

    /// <summary>
    /// Verifies zero-digest avatars fall back to the default Steam avatar.
    /// </summary>
    [Fact]
    public void Parse_BrowseHtml_WithDefaultAvatarDigest_ExtractsDefaultAvatarUrl()
    {
        const string htmlWithDefaultAvatar = """
            <script>
            {"publishedfileid":"42","creator":"76561198000000000"}
            {"steamid":"76561198000000000","sha_digest_avatar":{"_t":0,"v":[0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0]}}
            </script>
            <div><a href="https://steamcommunity.com/sharedfiles/filedetails/?id=42"><img src="https://example.com/p.jpg"/></a><a href="https://steamcommunity.com/sharedfiles/filedetails/?id=42">Default Guy Map</a><a href="https://steamcommunity.com/profiles/76561198000000000/myworkshopfiles/?appid=2732960">By DefaultGuy</a></div>
            """;

        var (items, _) = SteamWorkshopBrowseParser.Parse(htmlWithDefaultAvatar);

        var item = Assert.Single(items);
        Assert.Equal("https://avatars.fastly.steamstatic.com/fef49e7fa7e1997310d705b2a6158ff8dc1cdfeb_full.jpg", item.CreatorAvatarUrl);
    }

    /// <summary>
    /// Verifies empty HTML yields no items.
    /// </summary>
    /// <param name="html">The raw HTML.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("<html><body>No workshop items here</body></html>")]
    public void Parse_EmptyHtml_ReturnsNoItems(string? html)
    {
        var (items, total) = SteamWorkshopBrowseParser.Parse(html);

        Assert.Empty(items);
        Assert.Null(total);
    }

    /// <summary>
    /// Verifies pagination math against the reported total.
    /// </summary>
    /// <param name="itemsOnPage">The number of items parsed on the page.</param>
    /// <param name="page">The current page index.</param>
    /// <param name="total">The total match count.</param>
    /// <param name="pageSize">The page size.</param>
    /// <param name="expected">The expected result.</param>
    [Theory]
    [InlineData(30, 1, 552, 30, true)]
    [InlineData(12, 19, 552, 30, false)]
    [InlineData(30, 1, 30, 30, false)]
    public void HasMoreItems_WithTotal_ComparesAgainstTotal(int itemsOnPage, int page, int total, int pageSize, bool expected)
    {
        Assert.Equal(expected, SteamWorkshopBrowseParser.HasMoreItems(itemsOnPage, page, total, pageSize));
    }

    /// <summary>
    /// Verifies a full page implies more items when no total is reported.
    /// </summary>
    /// <param name="itemsOnPage">The number of items parsed on the page.</param>
    /// <param name="expected">The expected result.</param>
    [Theory]
    [InlineData(30, true)]
    [InlineData(12, false)]
    public void HasMoreItems_WithoutTotal_UsesFullPageHeuristic(int itemsOnPage, bool expected)
    {
        Assert.Equal(expected, SteamWorkshopBrowseParser.HasMoreItems(itemsOnPage, 1, null, 30));
    }

    /// <summary>
    /// Verifies title anchors with extra attributes still parse.
    /// </summary>
    [Fact]
    public void Parse_TitleAnchorWithAttributes_ReturnsTitle()
    {
        const string html = """<a href="https://steamcommunity.com/sharedfiles/filedetails/?id=5" class="preview"><img src="https://example.com/p.jpg" alt="x"/></a><a class="title" data-x="1" href="https://steamcommunity.com/sharedfiles/filedetails/?id=5">Attr Map</a>""";

        var (items, _) = SteamWorkshopBrowseParser.Parse(html);

        Assert.Equal("Attr Map", Assert.Single(items).Title);
    }

    /// <summary>
    /// Verifies preview images with attributes preceding src attribute are matched correctly.
    /// </summary>
    [Fact]
    public void Parse_PreviewImageWithPrecedingAttributes_ExtractsPreview()
    {
        const string html = """<a href="https://steamcommunity.com/sharedfiles/filedetails/?id=99" class="preview"><img loading="lazy" class="thumb" src="https://example.com/preview.jpg" alt="Preview"/></a><a href="https://steamcommunity.com/sharedfiles/filedetails/?id=99">Attr Image Map</a>""";

        var (items, _) = SteamWorkshopBrowseParser.Parse(html);

        var item = Assert.Single(items);
        Assert.Equal("https://example.com/preview.jpg", item.PreviewUrl);
    }

    /// <summary>
    /// Verifies previews stay null when only unrelated images are present.
    /// </summary>
    [Fact]
    public void Parse_NoPreviewImage_ReturnsNullPreview()
    {
        const string html = """<a href="https://steamcommunity.com/sharedfiles/filedetails/?id=9" class="preview"></a><a href="https://steamcommunity.com/sharedfiles/filedetails/?id=9">No Preview</a><img src="https://example.com/avatar.png"/>""";

        var (items, _) = SteamWorkshopBrowseParser.Parse(html);

        Assert.Null(Assert.Single(items).PreviewUrl);
    }
}
