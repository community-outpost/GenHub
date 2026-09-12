using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp;
using AngleSharp.Dom;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Tools;
using GenHub.Core.Models.Parsers;
using GenHub.Features.Content.Services.Parsers;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace GenHub.Tests.Core.Features.Content.Parsers;

/// <summary>
/// Regression tests for the current ModDB detail markup and Cloudflare-aware section loading.
/// </summary>
public sealed class ModDBPageParserTests
{
    /// <summary>
    /// Verifies the current game-addon detail page maps its metadata and /addons/start route into
    /// a usable file rather than returning an empty download URL.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ParseAsync_CurrentAddonDetailMarkup_ExtractsArchiveNameAndAddonStartUrlAsync()
    {
        // Arrange
        var playwright = CreatePlaywrightMock();
        var pageUrl = "https://www.moddb.com/games/cc-generals-zero-hour/addons/lemuria-2026-fixes";
        var doc = await CreateDocumentAsync("""
            <html><body>
              <div id="downloadsinfo">
                <div class="row clear"><h5>Filename</h5><span class="summary">Lemuria_2026_Fixes.rar</span></div>
                <div class="row clear"><h5>Category</h5><a>Singleplayer Map</a></div>
                <div class="row clear"><h5>Uploader</h5><a>BagaturKhan</a></div>
                <div class="row clear"><h5>Added</h5><time>Jan 2nd, 2026</time></div>
                <div class="row clear"><h5>Size</h5><span class="summary">1.07mb (1,125,450 bytes)</span></div>
                <div class="row clear"><a class="button buttonlarge" href="/addons/start/302328">Download Now</a></div>
              </div>
            </body></html>
            """);
        playwright
            .Setup(service => service.FetchAndParsePersistentManyAsync(
                ModDBConstants.BrowserProfileName,
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, IDocument>(StringComparer.OrdinalIgnoreCase) { [pageUrl] = doc });
        var parser = new ModDBPageParser(playwright.Object, new Mock<ILogger<ModDBPageParser>>().Object);

        // Act
        var parsed = await parser.ParseAsync(pageUrl);

        // Assert
        var file = Assert.Single(parsed.Sections.OfType<DownloadableFile>());
        Assert.Equal("Lemuria_2026_Fixes.rar", file.Name);
        Assert.Equal("https://www.moddb.com/addons/start/302328", file.DownloadUrl);
        Assert.Equal("Singleplayer Map", file.Category);
        Assert.Equal(1_125_450, file.SizeBytes);
    }

    /// <summary>
    /// Game-scoped FileDetail URLs (from the ModDB downloads listing) have no parent /mods/ page
    /// to sweep, so the detail view must still populate Community from comments on the file page
    /// itself instead of leaving only a single Releases row.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ParseAsync_GameFileDetail_ExtractsOnPageCommentsWithoutParentSweepAsync()
    {
        // Arrange
        const string pageUrl = "https://www.moddb.com/games/cc-generals-zero-hour/downloads/genbigeditbig-editor";
        var playwright = CreatePlaywrightMock();
        var doc = await CreateDocumentAsync("""
            <html><body>
              <div id="downloadsinfo">
                <div class="row clear"><h5>Filename</h5><span class="summary">GenBigEdit.zip</span></div>
                <div class="row clear"><h5>Size</h5><span class="summary">174.33mb (182,801,143 bytes)</span></div>
                <div class="row clear"><a class="button buttonlarge" href="/downloads/start/310120">Download Now</a></div>
              </div>
              <div id="commentsbrowse">
                <div class="row rowcomment" id="comment501">
                  <div class="heading"><a href="/members/mah_boi">mah_boi</a> <span class="subheading">May 30 2026</span></div>
                  <div class="commentbody">Please, provide us the source code of this program.</div>
                </div>
              </div>
            </body></html>
            """);
        playwright
            .Setup(service => service.FetchAndParsePersistentManyAsync(
                ModDBConstants.BrowserProfileName,
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, IDocument>(StringComparer.OrdinalIgnoreCase) { [pageUrl] = doc });
        var parser = new ModDBPageParser(playwright.Object, new Mock<ILogger<ModDBPageParser>>().Object);

        // Act
        var parsed = await parser.ParseAsync(pageUrl);

        // Assert
        Assert.Equal(PageType.FileDetail, parsed.PageType);
        var file = Assert.Single(parsed.Sections.OfType<DownloadableFile>());
        Assert.Equal("GenBigEdit.zip", file.Name);
        Assert.Equal("https://www.moddb.com/downloads/start/310120", file.DownloadUrl);

        var comment = Assert.Single(parsed.Sections.OfType<Comment>());
        Assert.Equal("mah_boi", comment.Author);
        Assert.Equal("Please, provide us the source code of this program.", comment.Content);

        // Must not attempt a parent-mod section sweep for /games/... FileDetail URLs (fetches only the single URL).
        playwright.Verify(
            service => service.FetchAndParsePersistentManyAsync(
                ModDBConstants.BrowserProfileName,
                It.Is<IReadOnlyList<string>>(urls => urls.Count == 1 && urls[0] == pageUrl),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies an addons-list row retains its ModDB category so a map does not become a generic
    /// add-on later in the resolver and manifest pipeline.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ParseAsync_AddonsListRow_ExtractsSingleplayerMapCategoryAsync()
    {
        // Arrange
        const string pageUrl = "https://www.moddb.com/games/cc-generals-zero-hour/addons";
        var playwright = CreatePlaywrightMock();
        var doc = await CreateDocumentAsync("""
            <html><body>
              <div class="row rowcontent">
                <h4>Lemuria 2026</h4>
                <span class="category">Singleplayer Map</span>
                <span class="size">1.07 MB</span>
                <a href="/addons/start/302328">Download</a>
              </div>
            </body></html>
            """);
        playwright
            .Setup(service => service.FetchAndParsePersistentManyAsync(
                ModDBConstants.BrowserProfileName,
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, IDocument>(StringComparer.OrdinalIgnoreCase) { [pageUrl] = doc });
        var parser = new ModDBPageParser(playwright.Object, new Mock<ILogger<ModDBPageParser>>().Object);

        // Act
        var parsed = await parser.ParseAsync(pageUrl);

        // Assert
        var file = Assert.Single(parsed.Sections.OfType<DownloadableFile>());
        Assert.Equal("Singleplayer Map", file.Category);
        Assert.Equal(FileSectionType.Addons, file.FileSectionType);
        Assert.Equal("https://www.moddb.com/addons/start/302328", file.DownloadUrl);
    }

    /// <summary>
    /// Verifies that rich ModDB sections use the verified persistent Chromium profile instead of
    /// a separate headless browser that loses Cloudflare clearance.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ParseAsync_ModDetail_UsesPersistentProfileForDownloadsAndAddonsAsync()
    {
        // Arrange
        const string pageUrl = "https://www.moddb.com/mods/example-mod";
        var documents = new Dictionary<string, IDocument>(StringComparer.OrdinalIgnoreCase)
        {
            [pageUrl] = await CreateDocumentAsync("<html><body><h1>Example Mod</h1></body></html>"),
            [pageUrl + "/downloads"] = await CreateDocumentAsync("""
                <div class="row file"><h4>Example Release</h4><span class="size">4 MB</span><a href="/downloads/start/100">Download</a></div>
                """),
            [pageUrl + "/addons"] = await CreateDocumentAsync("""
                <div class="row file"><h4>Example Addon</h4><span class="size">2 MB</span><a href="/addons/start/200">Download</a></div>
                """),
            [pageUrl + "/videos"] = await CreateDocumentAsync("<html><body></body></html>"),
            [pageUrl + "/images"] = await CreateDocumentAsync("<html><body></body></html>"),
            [pageUrl + "/reviews"] = await CreateDocumentAsync("<html><body></body></html>"),
            [pageUrl + "/articles"] = await CreateDocumentAsync("<html><body></body></html>"),
        };
        var playwright = CreatePlaywrightMock();
        playwright
            .Setup(service => service.FetchAndParsePersistentManyAsync(
                ModDBConstants.BrowserProfileName,
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<CancellationToken>()))
            .Returns((string _, IReadOnlyList<string> urls, CancellationToken _) =>
            {
                var result = new Dictionary<string, IDocument>(StringComparer.OrdinalIgnoreCase);
                foreach (var url in urls)
                {
                    if (documents.TryGetValue(url, out var d))
                    {
                        result[url] = d;
                    }
                }

                return Task.FromResult<IReadOnlyDictionary<string, IDocument>>(result);
            });
        var parser = new ModDBPageParser(playwright.Object, new Mock<ILogger<ModDBPageParser>>().Object);

        // Act
        var parsed = await parser.ParseAsync(pageUrl);

        // Assert
        var files = parsed.Sections.OfType<DownloadableFile>().ToList();
        Assert.Contains(files, file => file.Name == "Example Release" && file.DownloadUrl == "https://www.moddb.com/downloads/start/100");
        Assert.Contains(files, file => file.Name == "Example Addon" && file.DownloadUrl == "https://www.moddb.com/addons/start/200");
        playwright.Verify(
            service => service.FetchAndParsePersistentManyAsync(
                ModDBConstants.BrowserProfileName,
                It.Is<IReadOnlyList<string>>(urls =>
                    urls.Contains(pageUrl) && urls.Contains(pageUrl + "/downloads") && urls.Contains(pageUrl + "/addons")),
                It.IsAny<CancellationToken>()),
            Times.Once);
        playwright.Verify(service => service.FetchAndParseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies the file-only acquisition path resolves a FileDetail download without fetching the
    /// parent mod's downloads/addons/videos/images/reviews/articles sections (the seven-page sweep
    /// that previously fired on every card download).
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ParseFileDetailAsync_FetchesOnlyFileDetailPageAndSkipsSectionSweepAsync()
    {
        // Arrange: the FileDetail page already carries a real (non-guest) icon, so the parent-mod
        // icon fallback fetch is skipped too — exactly one fetch total.
        const string pageUrl = "https://www.moddb.com/mods/genspeed/downloads/genspeed-v25";
        var playwright = CreatePlaywrightMock();
        playwright
            .Setup(service => service.FetchAndParsePersistentAsync(
                ModDBConstants.BrowserProfileName,
                pageUrl,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(await CreateDocumentAsync("""
                <html><body>
                  <h1><a>GenSpeed v2.5</a></h1>
                  <div class="sidecolumn"><div class="profilebox"><img class="icon" src="https://static.moddb.com/mods/genspeed/icon.png" /></div></div>
                  <div id="downloadsinfo">
                    <div class="row clear"><h5>Filename</h5><span class="summary">GenSpeed-v2.5.zip</span></div>
                    <div class="row clear"><h5>Size</h5><span class="summary">65.04mb (68,197,650 bytes)</span></div>
                    <div class="row clear"><a href="/downloads/start/311183">Download Now</a></div>
                  </div>
                </body></html>
                """));
        var parser = new ModDBPageParser(playwright.Object, new Mock<ILogger<ModDBPageParser>>().Object);

        // Act
        var parsed = await parser.ParseFileDetailAsync(pageUrl);

        // Assert: exactly one DownloadableFile, no section sweep, icon from the FileDetail page.
        var file = Assert.Single(parsed.Sections.OfType<DownloadableFile>());
        Assert.Equal("GenSpeed v2.5", file.Name);
        Assert.Equal("GenSpeed-v2.5.zip", file.Filename);
        Assert.Equal("https://www.moddb.com/downloads/start/311183", file.DownloadUrl);
        Assert.Equal(68_197_650, file.SizeBytes);
        Assert.Equal("https://static.moddb.com/mods/genspeed/icon.png", parsed.Context.IconUrl);

        playwright.Verify(
            service => service.FetchAndParsePersistentAsync(
                ModDBConstants.BrowserProfileName,
                It.Is<string>(url => url != pageUrl),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Verifies that ParseFileDetailAsync performs only a single page fetch for file details without
    /// secondary parent mod fetches or section sweeps.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ParseFileDetailAsync_WithGuestIcon_FetchesOnlyFileDetailPageAsync()
    {
        // Arrange
        const string pageUrl = "https://www.moddb.com/mods/genspeed/downloads/genspeed-v25";
        var fetchedUrls = new List<string>();
        var playwright = CreatePlaywrightMock();
        var fileDoc = await CreateDocumentAsync("""
            <html><body>
              <h1><a>GenSpeed v2.5</a></h1>
              <div class="sidecolumn"><img class="icon" src="https://static.moddb.com/html/cutoff/images/guest/guest4.png" /></div>
              <div id="downloadsinfo">
                <div class="row clear"><h5>Filename</h5><span class="summary">GenSpeed-v2.5.zip</span></div>
                <div class="row clear"><a href="/downloads/start/311183">Download Now</a></div>
              </div>
            </body></html>
            """);

        playwright
            .Setup(service => service.FetchAndParsePersistentAsync(
                ModDBConstants.BrowserProfileName,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns((string _, string url, CancellationToken _) =>
            {
                fetchedUrls.Add(url);
                return Task.FromResult(fileDoc);
            });
        var parser = new ModDBPageParser(playwright.Object, new Mock<ILogger<ModDBPageParser>>().Object);

        // Act
        var parsed = await parser.ParseFileDetailAsync(pageUrl);

        // Assert: exactly one fetch (FileDetail), never parent mod or section pages.
        Assert.Equal(new[] { pageUrl }, fetchedUrls);
        Assert.Contains(parsed.Sections.OfType<DownloadableFile>(), f => f.Filename == "GenSpeed-v2.5.zip");
    }

    /// <summary>
    /// Verifies that comment parsing creates nested reply threads with correct author attribution
    /// and cleans out ModDB action text like 'Reply Good karma Bad karma+1 vote'.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ParseAsync_NestedComments_ParsesThreadHierarchyAndCleansActionTextAsync()
    {
        // Arrange
        const string pageUrl = "https://www.moddb.com/mods/example-mod/comments";
        var playwright = CreatePlaywrightMock();
        var doc = await CreateDocumentAsync("""
            <html><body>
              <div id="commentsbrowse">
                <div class="row rowcomment" id="comment101">
                  <div class="heading"><a href="/members/scorpionwins">Scorpionwins</a> <span class="subheading">Jul 31 2026</span></div>
                  <div class="commentbody">
                    How to activate additional weapons?
                    <div class="actions">Reply Good karma Bad karma+1 vote</div>
                  </div>
                  <div class="children">
                    <div class="row rowcomment" id="comment102">
                      <div class="heading"><a href="/members/bagaturkhan">BagaturKhan</a> <span class="subheading">Jul 31 2026</span></div>
                      <div class="commentbody">
                        If you are talking about stolen tech, train your infiltrator.
                        <div class="actions">Reply Good karma Bad karma+1 vote</div>
                      </div>
                    </div>
                  </div>
                </div>
              </div>
            </body></html>
            """);
        playwright
            .Setup(service => service.FetchAndParsePersistentManyAsync(
                ModDBConstants.BrowserProfileName,
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, IDocument>(StringComparer.OrdinalIgnoreCase) { [pageUrl] = doc });
        var parser = new ModDBPageParser(playwright.Object, new Mock<ILogger<ModDBPageParser>>().Object);

        // Act
        var parsed = await parser.ParseAsync(pageUrl);

        // Assert
        var topLevelComments = parsed.Sections.OfType<Comment>().ToList();
        var parentComment = Assert.Single(topLevelComments);
        Assert.Equal("Scorpionwins", parentComment.Author);
        Assert.Equal("How to activate additional weapons?", parentComment.Content);
        Assert.Equal(0, parentComment.IndentLevel);

        var reply = Assert.Single(parentComment.Replies!);
        Assert.Equal("BagaturKhan", reply.Author);
        Assert.Equal("If you are talking about stolen tech, train your infiltrator.", reply.Content);
        Assert.Equal(1, reply.IndentLevel);
    }

    /// <summary>
    /// Verifies reply markup nested inside <c>.commentbody</c> does not inflate the parent content
    /// into a huge whitespace block (the layout bug seen in the Community tab).
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ParseAsync_NestedCommentsInsideCommentBody_DoesNotPolluteParentContentAsync()
    {
        // Arrange
        const string pageUrl = "https://www.moddb.com/mods/example-mod/comments";
        var playwright = CreatePlaywrightMock();
        var doc = await CreateDocumentAsync("""
            <html><body>
              <div id="commentsbrowse">
                <div class="row rowcomment" id="comment201">
                  <div class="heading"><a href="/members/scorpionwins">Scorpionwins</a> <span class="subheading">Jul 31 2026</span></div>
                  <div class="commentbody">


                    How to activate additional weapons?
                    <div class="actions">Reply Good karma Bad karma+1 vote</div>
                    <div class="children">
                      <div class="row rowcomment" id="comment202">
                        <div class="heading"><a href="/members/bagaturkhan">BagaturKhan</a> <span class="subheading">Jul 31 2026</span></div>
                        <div class="commentbody">
                          Train your infiltrator.
                          <div class="actions">Reply</div>
                        </div>
                      </div>
                    </div>
                  </div>
                </div>
              </div>
            </body></html>
            """);
        playwright
            .Setup(service => service.FetchAndParsePersistentManyAsync(
                ModDBConstants.BrowserProfileName,
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, IDocument>(StringComparer.OrdinalIgnoreCase) { [pageUrl] = doc });
        var parser = new ModDBPageParser(playwright.Object, new Mock<ILogger<ModDBPageParser>>().Object);

        // Act
        var parsed = await parser.ParseAsync(pageUrl);

        // Assert
        var parentComment = Assert.Single(parsed.Sections.OfType<Comment>());
        Assert.Equal("How to activate additional weapons?", parentComment.Content);
        Assert.DoesNotContain("BagaturKhan", parentComment.Content);
        Assert.DoesNotContain("infiltrator", parentComment.Content, StringComparison.OrdinalIgnoreCase);

        var reply = Assert.Single(parentComment.Replies!);
        Assert.Equal("BagaturKhan", reply.Author);
        Assert.Equal("Train your infiltrator.", reply.Content);
    }

    /// <summary>
    /// Verifies rating widgets without author/body are not surfaced as empty Community review cards.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ParseAsync_BareRatingWidget_IsNotTreatedAsReviewAsync()
    {
        // Arrange
        const string pageUrl = "https://www.moddb.com/mods/example-mod/reviews";
        var playwright = CreatePlaywrightMock();
        var doc = await CreateDocumentAsync("""
            <html><body>
              <div class="rating"><span class="score">9.0</span><span class="helpful">people found this helpful</span></div>
              <div class="review">
                <a href="/members/alice">Alice</a>
                <div class="content">Solid patch for ROTR.</div>
                <span class="score">8.5</span>
              </div>
            </body></html>
            """);
        playwright
            .Setup(service => service.FetchAndParsePersistentManyAsync(
                ModDBConstants.BrowserProfileName,
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, IDocument>(StringComparer.OrdinalIgnoreCase) { [pageUrl] = doc });
        var parser = new ModDBPageParser(playwright.Object, new Mock<ILogger<ModDBPageParser>>().Object);

        // Act
        var parsed = await parser.ParseAsync(pageUrl);

        // Assert
        var review = Assert.Single(parsed.Sections.OfType<Review>());
        Assert.Equal("Alice", review.Author);
        Assert.Equal("Solid patch for ROTR.", review.Content);
    }

    /// <summary>
    /// The live ModDB composer (<c>#commentform</c> plus guest/email rows and injected CSS) must
    /// not appear as Community comments.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ParseAsync_CommentComposer_IsNotTreatedAsCommentsAsync()
    {
        const string pageUrl = "https://www.moddb.com/mods/cc-generals-undone/downloads/cc-generals-undone";
        var playwright = CreatePlaywrightMock();
        var doc = await CreateDocumentAsync("""
            <html><body>
              <h1>C&amp;C Generals Undone file</h1>
              <h2>C&amp;C Generals Undone</h2>
              <div id="downloadsinfo">
                <div class="row clear"><h5>Filename</h5><span class="summary">GeneralsUndone_v1.0.zip</span></div>
                <div class="row clear"><a class="button buttonlarge" href="/downloads/start/313719">Download Now</a></div>
              </div>
              <div class="normalbox formbox" id="commentform">
                <div id="commentform" class="body">
                  <div class="row rowcommentguest clear">
                    <p>Your comment will be anonymous unless you join the community.
                    <style>span.ffbcbbfaadacfformouter span { display: none; }</style></p>
                  </div>
                  <div class="row rowcommentsummary clear" id="commentsummary"><textarea></textarea></div>
                  <div class="row rowcommentemail clear" id="commentemail"><input /></div>
                </div>
              </div>
            </body></html>
            """);
        playwright
            .Setup(service => service.FetchAndParsePersistentManyAsync(
                ModDBConstants.BrowserProfileName,
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, IDocument>(StringComparer.OrdinalIgnoreCase) { [pageUrl] = doc });
        var parser = new ModDBPageParser(playwright.Object, new Mock<ILogger<ModDBPageParser>>().Object);

        var parsed = await parser.ParseAsync(pageUrl);

        Assert.Empty(parsed.Sections.OfType<Comment>());
    }

    /// <summary>
    /// File-page chrome (game icon, developer avatar, download title art) must not appear in Media.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ParseAsync_FileDetailChromeImages_AreNotGalleryMediaAsync()
    {
        const string pageUrl = "https://www.moddb.com/mods/cc-generals-undone/downloads/cc-generals-undone";
        var playwright = CreatePlaywrightMock();
        var doc = await CreateDocumentAsync("""
            <html><body>
              <h2>C&amp;C Generals Undone</h2>
              <div id="downloadsinfo">
                <div class="row clear"><h5>Filename</h5><span class="summary">GeneralsUndone_v1.0.zip</span></div>
                <div class="row clear"><a href="/downloads/start/313719">Download Now</a></div>
              </div>
              <a class="thickbox" href="https://media.moddb.com/images/downloads/1/314/313719/Title.png">
                <img alt="C&amp;C Generals Undone" src="https://media.moddb.com/cache/images/downloads/1/314/313719/thumb_620x2000/Title.png" />
              </a>
              <div id="modsinfo">
                <a href="https://www.moddb.com/games/cc-generals-zero-hour">
                  <img alt="C&amp;C: Generals Zero Hour" src="https://media.moddb.com/images/games/1/1/184/icon.gif" />
                </a>
                <a href="https://www.moddb.com/mods/cc-generals-undone">
                  <img alt="C&amp;C Generals Undone" src="https://media.moddb.com/cache/images/mods/1/73/72174/crop_120x90/Screenshot.png" />
                </a>
                <a href="https://www.moddb.com/company/whiteskull9044">
                  <img alt="WhiteSkull#9044" src="https://media.moddb.com/cache/images/groups/1/49/48231/crop_120x90/Screenshot.png" />
                </a>
              </div>
            </body></html>
            """);
        playwright
            .Setup(service => service.FetchAndParsePersistentManyAsync(
                ModDBConstants.BrowserProfileName,
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, IDocument>(StringComparer.OrdinalIgnoreCase) { [pageUrl] = doc });
        var parser = new ModDBPageParser(playwright.Object, new Mock<ILogger<ModDBPageParser>>().Object);

        var parsed = await parser.ParseAsync(pageUrl);

        Assert.Empty(parsed.Sections.OfType<Image>());
    }

    /// <summary>
    /// The images tab should yield unique gallery shots, not share icons or duplicate featured thumbs.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ParseAsync_ImagesPage_ExtractsUniqueGalleryShotsAsync()
    {
        const string pageUrl = "https://www.moddb.com/mods/cc-generals-undone/images";
        var playwright = CreatePlaywrightMock();
        var doc = await CreateDocumentAsync("""
            <html><body>
              <div id="imagebox">
                <a href="/mods/cc-generals-undone/images/icbm-powtruck#imagebox">
                  <img alt="View media" src="https://media.moddb.com/cache/images/mods/1/73/72174/crop_120x90/ICBM_POWTruck.png" />
                </a>
                <a href="/mods/cc-generals-undone/images/spectre#imagebox">
                  <img alt="View media" src="https://media.moddb.com/cache/images/mods/1/73/72174/crop_120x90/Spectre.png" />
                </a>
                <div class="media"><div class="holder">
                  <img alt="ICBM POWTruck" src="https://media.moddb.com/cache/images/mods/1/73/72174/thumb_620x2000/ICBM_POWTruck.png" />
                </div></div>
                <a href="https://www.facebook.com/share"><img alt="Share on Facebook" src="data:image/png;base64,aaaa" /></a>
              </div>
            </body></html>
            """);
        playwright
            .Setup(service => service.FetchAndParsePersistentManyAsync(
                ModDBConstants.BrowserProfileName,
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, IDocument>(StringComparer.OrdinalIgnoreCase) { [pageUrl] = doc });
        var parser = new ModDBPageParser(playwright.Object, new Mock<ILogger<ModDBPageParser>>().Object);

        var parsed = await parser.ParseAsync(pageUrl);

        var images = parsed.Sections.OfType<Image>().ToList();
        Assert.Equal(2, images.Count);
        Assert.Contains(images, image => image.Title.Contains("ICBM", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(images, image => image.Title.Contains("Spectre", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(images, image => image.Title.Contains("Share", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(images, image => image.ThumbnailUrl?.StartsWith("data:", StringComparison.OrdinalIgnoreCase) == true);
        Assert.All(images, image => Assert.Contains("crop_", image.ThumbnailUrl ?? string.Empty));
        Assert.All(images, image => Assert.Contains("/cache/", image.ThumbnailUrl ?? string.Empty));
        Assert.All(images, image => Assert.DoesNotContain("crop_", image.FullSizeUrl ?? string.Empty));
        Assert.All(images, image => Assert.DoesNotContain("/cache/", image.FullSizeUrl ?? string.Empty));
    }

    /// <summary>
    /// Image titles with CamelCase or raw filenames should be formatted with clean spaces.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ParseAsync_ImageTitles_FormatsCamelCaseAndFilenamesAsync()
    {
        const string pageUrl = "https://www.moddb.com/mods/test-mod/images";
        var playwright = CreatePlaywrightMock();
        var doc = await CreateDocumentAsync("""
            <html><body>
              <div id="imagebox">
                <a href="/mods/test-mod/images/shot1">
                  <img alt="LifeOfBRRRRTTT" src="https://media.moddb.com/cache/images/mods/1/73/72174/crop_120x90/LifeOfBRRRRTTT.png" />
                </a>
                <a href="/mods/test-mod/images/shot2">
                  <img alt="BASSBASSBASSASS" src="https://media.moddb.com/cache/images/mods/1/73/72174/crop_120x90/BASSBASSBASSASS.png" />
                </a>
              </div>
            </body></html>
            """);
        playwright
            .Setup(service => service.FetchAndParsePersistentManyAsync(
                ModDBConstants.BrowserProfileName,
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, IDocument>(StringComparer.OrdinalIgnoreCase) { [pageUrl] = doc });
        var parser = new ModDBPageParser(playwright.Object, new Mock<ILogger<ModDBPageParser>>().Object);

        var parsed = await parser.ParseAsync(pageUrl);

        var images = parsed.Sections.OfType<Image>().ToList();
        Assert.Equal(2, images.Count);
        Assert.Equal("Life Of BRRRRTTT", images[0].Title);
        Assert.Equal("BASSBASSBASSASS", images[1].Title);
        Assert.Equal("https://media.moddb.com/cache/images/mods/1/73/72174/crop_120x90/LifeOfBRRRRTTT.png", images[0].ThumbnailUrl);
        Assert.Equal("https://media.moddb.com/images/mods/1/73/72174/LifeOfBRRRRTTT.png", images[0].FullSizeUrl);
    }

    /// <summary>
    /// FileDetail filename plus the parent downloads listing of the same start URL must collapse
    /// to one release, keeping the human listing name.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ParseAsync_FileDetailAndParentDownloads_DedupesSameBinaryAsync()
    {
        const string pageUrl = "https://www.moddb.com/mods/cc-generals-undone/downloads/cc-generals-undone";
        const string parentUrl = "https://www.moddb.com/mods/cc-generals-undone";
        var documents = new Dictionary<string, IDocument>(StringComparer.OrdinalIgnoreCase)
        {
            [pageUrl] = await CreateDocumentAsync("""
                <html><body>
                  <h1>C&amp;C Generals Undone file</h1>
                  <h2>C&amp;C Generals Undone</h2>
                  <a href="/members/register">register</a>
                  <div id="modsinfo"><a href="/company/whiteskull9044">WhiteSkull#9044</a></div>
                  <span class="summary">Games : C&amp;C: Generals Zero Hour : Mods : C&amp;C Generals Undone : Files</span>
                  <p id="downloadsummary">This is the first version of Undone, and I know it's still very much in development.</p>
                  <div id="downloadsinfo">
                    <div class="row clear"><h5>Filename</h5><span class="summary">GeneralsUndone_v1.0.zip</span></div>
                    <div class="row clear"><a href="/downloads/start/313719">Download Now</a></div>
                  </div>
                </body></html>
                """),
            [parentUrl] = await CreateDocumentAsync("<html><body><h2>C&amp;C Generals Undone</h2></body></html>"),
            [parentUrl + "/downloads"] = await CreateDocumentAsync("""
                <div class="row rowcontent">
                  <h4>C&amp;C Generals Undone</h4>
                  <span class="size">289.6 MB</span>
                  <a href="/mods/cc-generals-undone/downloads/cc-generals-undone" class="button download">Download</a>
                </div>
                <div class="row rowcontent">
                  <h4>Generals Undone v1.01 Patch</h4>
                  <span class="size">1 MB</span>
                  <a href="/downloads/start/314093">Download</a>
                </div>
                """),
            [parentUrl + "/addons"] = await CreateDocumentAsync("<html><body></body></html>"),
            [parentUrl + "/videos"] = await CreateDocumentAsync("<html><body></body></html>"),
            [parentUrl + "/images"] = await CreateDocumentAsync("<html><body></body></html>"),
            [parentUrl + "/reviews"] = await CreateDocumentAsync("<html><body></body></html>"),
            [parentUrl + "/articles"] = await CreateDocumentAsync("<html><body></body></html>"),
        };
        var playwright = CreatePlaywrightMock();
        playwright
            .Setup(service => service.FetchAndParsePersistentManyAsync(
                ModDBConstants.BrowserProfileName,
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<CancellationToken>()))
            .Returns((string _, IReadOnlyList<string> urls, CancellationToken _) =>
            {
                var result = new Dictionary<string, IDocument>(StringComparer.OrdinalIgnoreCase);
                foreach (var url in urls)
                {
                    if (documents.TryGetValue(url, out var doc))
                    {
                        result[url] = doc;
                    }
                }

                return Task.FromResult<IReadOnlyDictionary<string, IDocument>>(result);
            });
        var parser = new ModDBPageParser(playwright.Object, new Mock<ILogger<ModDBPageParser>>().Object);

        var parsed = await parser.ParseAsync(pageUrl);

        var files = parsed.Sections.OfType<DownloadableFile>().ToList();
        Assert.Equal(2, files.Count);
        Assert.Contains(files, file => file.Name == "C&C Generals Undone" && file.DownloadUrl == "https://www.moddb.com/downloads/start/313719");
        Assert.Contains(files, file => file.Name == "Generals Undone v1.01 Patch");
        Assert.DoesNotContain(files, file => file.Name == "GeneralsUndone_v1.0.zip");
        Assert.Equal("C&C Generals Undone", parsed.Context.Title);
        Assert.Equal("WhiteSkull#9044", parsed.Context.Developer);
        Assert.Contains("first version of Undone", parsed.Context.Description, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Games :", parsed.Context.Description, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that ParseFileDetailAsync correctly parses metadata when ModDB uses alternative label names
    /// such as "File Name", "File Size", "Uploaded By", "MD5 Checksum", and "Total Downloads".
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ParseFileDetailAsync_WithAlternativeLabels_ParsesMd5ChecksumTotalDownloadsAndUploaderAsync()
    {
        const string pageUrl = "https://www.moddb.com/mods/cc-generals-undone/downloads/generals-undone-v101-patch";
        var playwright = CreatePlaywrightMock();
        playwright
            .Setup(service => service.FetchAndParsePersistentAsync(
                ModDBConstants.BrowserProfileName,
                pageUrl,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(await CreateDocumentAsync("""
                <html><body>
                  <div id="downloadsinfo">
                    <div class="row clear"><h5>File Name</h5><span class="summary">GeneralsUndone_v1.01.csf</span></div>
                    <div class="row clear"><h5>Category</h5><span class="summary">Patch</span></div>
                    <div class="row clear"><h5>Uploaded By</h5><span class="summary">WhiteSkull#9044</span></div>
                    <div class="row clear"><h5>File Size</h5><span class="summary">289.6mb (303,663,235 bytes)</span></div>
                    <div class="row clear"><h5>MD5 Checksum</h5><span class="summary">6e5b1fd58fc7a58cf21af86933116942</span></div>
                    <div class="row clear"><h5>Total Downloads</h5><span class="summary">185</span></div>
                    <div class="row clear"><a class="button buttonlarge" href="/downloads/start/313720">Download Now</a></div>
                  </div>
                </body></html>
                """));
        var parser = new ModDBPageParser(playwright.Object, new Mock<ILogger<ModDBPageParser>>().Object);

        var parsed = await parser.ParseFileDetailAsync(pageUrl);

        var file = Assert.Single(parsed.Sections.OfType<DownloadableFile>());
        Assert.Equal("GeneralsUndone_v1.01.csf", file.Filename);
        Assert.Equal("Patch", file.Category);
        Assert.Equal("WhiteSkull#9044", file.Uploader);
        Assert.Equal(303_663_235, file.SizeBytes);
        Assert.Equal("6e5b1fd58fc7a58cf21af86933116942", file.Md5Hash);
        Assert.Equal(185, file.DownloadCount);
        Assert.Equal("https://www.moddb.com/downloads/start/313720", file.DownloadUrl);
    }

    /// <summary>
    /// Verifies that ModDB download listing rows with subheading metadata (size in subheading, button class)
    /// extract size, category, uploader, details URL, and download URL correctly.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ParseAsync_ModernModDBDownloadsListing_ExtractsSubheadingSizeAndLinksAsync()
    {
        const string pageUrl = "https://www.moddb.com/mods/cc-generals-undone/downloads/cc-generals-undone";
        const string parentUrl = "https://www.moddb.com/mods/cc-generals-undone";
        var documents = new Dictionary<string, IDocument>(StringComparer.OrdinalIgnoreCase)
        {
            [pageUrl] = await CreateDocumentAsync("""
                <html><body>
                  <h1>C&amp;C Generals Undone file</h1>
                  <h2>C&amp;C Generals Undone</h2>
                  <div id="modsinfo"><a href="/company/whiteskull9044">WhiteSkull#9044</a></div>
                  <p id="downloadsummary">First release of Undone.</p>
                  <div id="downloadsinfo">
                    <div class="row clear"><h5>Filename</h5><span class="summary">GeneralsUndone_v1.0.zip</span></div>
                    <div class="row clear"><h5>Category</h5><span class="summary">Full Version</span></div>
                    <div class="row clear"><h5>Size</h5><span class="summary">289.6mb (303,663,235 bytes)</span></div>
                    <div class="row clear"><h5>MD5 Hash</h5><span class="summary">6e5b3fcf30fc7a58ef21af869551bb942</span></div>
                    <div class="row clear"><a class="button buttonlarge" href="/downloads/start/313719">Download Now</a></div>
                  </div>
                </body></html>
                """),
            [parentUrl] = await CreateDocumentAsync("<html><body><h2>C&amp;C Generals Undone</h2></body></html>"),
            [parentUrl + "/downloads"] = await CreateDocumentAsync("""
                <div class="row rowcontent clear">
                  <div class="heading">
                    <h4><a href="/mods/cc-generals-undone/downloads/cc-generals-undone">C&amp;C Generals Undone</a></h4>
                    <span class="subheading"><time datetime="2026-08-02T12:00:00+00:00">Aug 2nd, 2026</time> - Full Version, 289.6mb</span>
                  </div>
                  <div class="actions">
                    <a href="/mods/cc-generals-undone/downloads/cc-generals-undone" class="button">Download</a>
                  </div>
                </div>
                <div class="row rowcontent clear">
                  <div class="heading">
                    <h4><a href="/mods/cc-generals-undone/downloads/generals-undone-v101-patch">Generals Undone v1.01 Patch</a></h4>
                    <span class="subheading"><time datetime="2026-08-07T12:00:00+00:00">Aug 7th, 2026</time> - Patch, 1 MB</span>
                  </div>
                  <div class="actions">
                    <a href="/mods/cc-generals-undone/downloads/generals-undone-v101-patch" class="button">Download</a>
                  </div>
                </div>
                """),
            [parentUrl + "/addons"] = await CreateDocumentAsync("<html><body></body></html>"),
            [parentUrl + "/videos"] = await CreateDocumentAsync("<html><body></body></html>"),
            [parentUrl + "/images"] = await CreateDocumentAsync("<html><body></body></html>"),
            [parentUrl + "/reviews"] = await CreateDocumentAsync("<html><body></body></html>"),
            [parentUrl + "/articles"] = await CreateDocumentAsync("<html><body></body></html>"),
        };
        var playwright = CreatePlaywrightMock();
        playwright
            .Setup(service => service.FetchAndParsePersistentManyAsync(
                ModDBConstants.BrowserProfileName,
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<CancellationToken>()))
            .Returns((string _, IReadOnlyList<string> urls, CancellationToken _) =>
            {
                var result = new Dictionary<string, IDocument>(StringComparer.OrdinalIgnoreCase);
                foreach (var url in urls)
                {
                    if (documents.TryGetValue(url, out var doc))
                    {
                        result[url] = doc;
                    }
                }

                return Task.FromResult<IReadOnlyDictionary<string, IDocument>>(result);
            });
        var parser = new ModDBPageParser(playwright.Object, new Mock<ILogger<ModDBPageParser>>().Object);

        var parsed = await parser.ParseAsync(pageUrl);

        var files = parsed.Sections.OfType<DownloadableFile>().ToList();
        Assert.Equal(2, files.Count);

        var mainRelease = Assert.Single(files, f => f.Name == "C&C Generals Undone");
        Assert.Equal("GeneralsUndone_v1.0.zip", mainRelease.Filename);
        Assert.Equal("https://www.moddb.com/downloads/start/313719", mainRelease.DownloadUrl);
        Assert.Equal("https://www.moddb.com/mods/cc-generals-undone/downloads/cc-generals-undone", mainRelease.DetailsUrl);
        Assert.Equal("Full Version", mainRelease.Category);
        Assert.Equal(303_663_235, mainRelease.SizeBytes);
        Assert.Equal("6e5b3fcf30fc7a58ef21af869551bb942", mainRelease.Md5Hash);

        var patchRelease = Assert.Single(files, f => f.Name == "Generals Undone v1.01 Patch");
        Assert.Equal("https://www.moddb.com/mods/cc-generals-undone/downloads/generals-undone-v101-patch", patchRelease.DetailsUrl);
        Assert.Equal("Patch", patchRelease.Category);
        Assert.Equal(1048576, patchRelease.SizeBytes);
        Assert.Equal("1 MB", patchRelease.SizeDisplay);
    }

    /// <summary>
    /// Verifies that embedded YouTube iframes on mod pages have their title, thumbnail, platform,
    /// and normalized embed URL properly extracted.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ParseAsync_WithYouTubeIframe_ExtractsTitlePlatformThumbnailAndEmbedUrlAsync()
    {
        // Arrange
        const string pageUrl = "https://www.moddb.com/mods/korean-war-2";
        var doc = await CreateDocumentAsync("""
            <html><body>
              <div class="headerbox"><h1>Korean War 2</h1></div>
              <div class="video-container">
                <iframe src="https://www.youtube.com/embed/fW_O3G_Z4qg" title="Korean War 2 Official Trailer" frameborder="0" allowfullscreen></iframe>
              </div>
              <div class="row">
                <h3>Gameplay Teaser</h3>
                <iframe src="https://www.youtube-nocookie.com/embed/dQw4w9WgXcQ?rel=0"></iframe>
              </div>
            </body></html>
            """);
        var playwright = CreatePlaywrightMock();
        playwright
            .Setup(service => service.FetchAndParsePersistentManyAsync(
                ModDBConstants.BrowserProfileName,
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, IDocument>(StringComparer.OrdinalIgnoreCase) { [pageUrl] = doc });

        var parser = new ModDBPageParser(playwright.Object, new Mock<ILogger<ModDBPageParser>>().Object);

        // Act
        var parsed = await parser.ParseAsync(pageUrl);

        // Assert
        var videos = parsed.Sections.OfType<Video>().ToList();
        Assert.Equal(2, videos.Count);

        var trailer = Assert.Single(videos, v => v.Title == "Korean War 2 Official Trailer");
        Assert.Equal("YouTube", trailer.Platform);
        Assert.Equal("https://img.youtube.com/vi/fW_O3G_Z4qg/hqdefault.jpg", trailer.ThumbnailUrl);
        Assert.Equal("https://www.youtube.com/embed/fW_O3G_Z4qg", trailer.EmbedUrl);

        var teaser = Assert.Single(videos, v => v.Title == "Gameplay Teaser");
        Assert.Equal("YouTube", teaser.Platform);
        Assert.Equal("https://img.youtube.com/vi/dQw4w9WgXcQ/hqdefault.jpg", teaser.ThumbnailUrl);
        Assert.Equal("https://www.youtube.com/embed/dQw4w9WgXcQ", teaser.EmbedUrl);
    }

    /// <summary>
    /// Verifies that untitled iframe embeds (YouTube, Vimeo) and HTML5 video elements without titles
    /// are not discarded, but are extracted with platform-derived fallback titles.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ParseAsync_WithUntitledVideoEmbeds_ExtractsWithPlatformFallbackTitleAsync()
    {
        // Arrange
        const string pageUrl = "https://www.moddb.com/mods/test-mod";
        var playwright = CreatePlaywrightMock();
        var doc = await CreateDocumentAsync("""
            <html><body>
              <div class="media">
                <iframe src="https://www.youtube.com/embed/dQw4w9WgXcQ"></iframe>
              </div>
              <div class="media">
                <iframe src="https://player.vimeo.com/video/123456789"></iframe>
              </div>
              <div class="media">
                <video src="https://media.moddb.com/videos/custom-clip.mp4" poster="https://media.moddb.com/videos/poster.jpg"></video>
              </div>
            </body></html>
            """);

        playwright
            .Setup(service => service.FetchAndParsePersistentManyAsync(
                ModDBConstants.BrowserProfileName,
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, IDocument>(StringComparer.OrdinalIgnoreCase) { [pageUrl] = doc });

        var parser = new ModDBPageParser(playwright.Object, new Mock<ILogger<ModDBPageParser>>().Object);

        // Act
        var parsed = await parser.ParseAsync(pageUrl);

        // Assert
        var videos = parsed.Sections.OfType<Video>().ToList();
        Assert.Equal(3, videos.Count);

        var yt = Assert.Single(videos, v => v.Platform == "YouTube");
        Assert.Equal("YouTube Video", yt.Title);
        Assert.Equal("https://www.youtube.com/embed/dQw4w9WgXcQ", yt.EmbedUrl);

        var vimeo = Assert.Single(videos, v => v.Platform == "Vimeo");
        Assert.Equal("Vimeo Video", vimeo.Title);
        Assert.Equal("https://player.vimeo.com/video/123456789", vimeo.EmbedUrl);

        var html5 = Assert.Single(videos, v => v.Platform == "ModDB");
        Assert.Equal("ModDB Video", html5.Title);
        Assert.Equal("https://media.moddb.com/videos/custom-clip.mp4", html5.EmbedUrl);
        Assert.Equal("https://media.moddb.com/videos/poster.jpg", html5.ThumbnailUrl);
    }

    /// <summary>
    /// Verifies that ModDB /videos gallery cards are extracted from the videos section with titles,
    /// thumbnails, platforms, and page links.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ParseAsync_WithVideosSectionListing_ExtractsGalleryVideosAsync()
    {
        // Arrange
        const string pageUrl = "https://www.moddb.com/mods/korean-war-2/downloads/korean-war-2-v020";
        const string parentUrl = "https://www.moddb.com/mods/korean-war-2";
        var documents = new Dictionary<string, IDocument>(StringComparer.OrdinalIgnoreCase)
        {
            [pageUrl] = await CreateDocumentAsync("""
                <html><body>
                  <div id="downloadsinfo">
                    <div class="row clear"><h5>Filename</h5><span class="summary">KoreanWar2V020.zip</span></div>
                    <div class="row clear"><h5>Size</h5><span class="summary">360mb</span></div>
                    <div class="row clear"><a class="button buttonlarge" href="/downloads/start/309442">Download Now</a></div>
                  </div>
                </body></html>
                """),
            [parentUrl] = await CreateDocumentAsync("""
                <html><body>
                  <h1>Korean War 2</h1>
                  <div class="sidecolumn"><div class="avatar"><img src="https://media.moddb.com/images/members/1/1/icon.jpg" /></div></div>
                </body></html>
                """),
            [parentUrl + "/videos"] = await CreateDocumentAsync("""
                <html><body>
                  <div id="mediabrowse" class="table">
                    <div class="row mediarow rowcontent">
                      <div class="holder">
                        <a href="/mods/korean-war-2/videos/iran-safir-mrl-ingame" title="Iran Safir MRL Ingame">
                          <img src="https://media.moddb.com/cache/images/videos/1/12/11543/crop_120x90/preview.png" alt="Iran Safir MRL Ingame" />
                          <span class="playbutton"></span>
                        </a>
                        <span class="title"><a href="/mods/korean-war-2/videos/iran-safir-mrl-ingame">Iran Safir MRL Ingame</a></span>
                      </div>
                    </div>
                    <div class="row mediarow rowcontent">
                      <div class="holder">
                        <a href="/mods/korean-war-2/videos/iran-tabas-air-defense" title="Iran Tabas Air Defense System">
                          <img src="https://media.moddb.com/cache/images/videos/1/12/11544/crop_120x90/preview2.png" alt="Iran Tabas Air Defense System" />
                        </a>
                        <span class="title"><a href="/mods/korean-war-2/videos/iran-tabas-air-defense">Iran Tabas Air Defense System</a></span>
                      </div>
                    </div>
                  </div>
                </body></html>
                """),
        };

        var playwright = CreatePlaywrightMock();
        playwright
            .Setup(service => service.FetchAndParsePersistentManyAsync(
                ModDBConstants.BrowserProfileName,
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<CancellationToken>()))
            .Returns((string _, IReadOnlyList<string> urls, CancellationToken _) =>
            {
                var result = new Dictionary<string, IDocument>(StringComparer.OrdinalIgnoreCase);
                foreach (var url in urls)
                {
                    if (documents.TryGetValue(url, out var doc))
                    {
                        result[url] = doc;
                    }
                }

                return Task.FromResult<IReadOnlyDictionary<string, IDocument>>(result);
            });

        var parser = new ModDBPageParser(playwright.Object, new Mock<ILogger<ModDBPageParser>>().Object);

        // Act
        var parsed = await parser.ParseAsync(pageUrl);

        // Assert
        var videos = parsed.Sections.OfType<Video>().ToList();
        Assert.Equal(2, videos.Count);

        var video1 = Assert.Single(videos, v => v.Title == "Iran Safir MRL Ingame");
        Assert.Equal("ModDB", video1.Platform);
        Assert.Equal("https://www.moddb.com/mods/korean-war-2/videos/iran-safir-mrl-ingame", video1.EmbedUrl);
        Assert.Equal("https://media.moddb.com/cache/images/videos/1/12/11543/crop_120x90/preview.png", video1.ThumbnailUrl);

        var video2 = Assert.Single(videos, v => v.Title == "Iran Tabas Air Defense System");
        Assert.Equal("ModDB", video2.Platform);
        Assert.Equal("https://www.moddb.com/mods/korean-war-2/videos/iran-tabas-air-defense", video2.EmbedUrl);
        Assert.Equal("https://media.moddb.com/cache/images/videos/1/12/11544/crop_120x90/preview2.png", video2.ThumbnailUrl);
    }

    /// <summary>
    /// Verifies that video cards linking to /videos/ are not duplicated into the static image gallery.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ParseAsync_WithMediaBrowseContainingImagesAndVideos_SeparatesImagesAndVideosAsync()
    {
        // Arrange
        const string pageUrl = "https://www.moddb.com/mods/test-mod";
        var doc = await CreateDocumentAsync("""
            <html><body>
              <h1>Test Mod</h1>
              <div id="mediabrowse">
                <div class="row mediarow">
                  <div class="holder">
                    <a href="/mods/test-mod/images/screenshot-1" title="Ingame Base Screenshot">
                      <img src="https://media.moddb.com/images/mods/1/1/base.png" alt="Ingame Base Screenshot" />
                    </a>
                  </div>
                  <div class="holder">
                    <a href="/mods/test-mod/videos/trailer-1" title="Ingame Trailer Video">
                      <img src="https://media.moddb.com/cache/images/videos/1/1/trailer.png" alt="Ingame Trailer Video" />
                    </a>
                  </div>
                </div>
              </div>
            </body></html>
            """);
        var playwright = CreatePlaywrightMock();
        playwright
            .Setup(service => service.FetchAndParsePersistentManyAsync(
                ModDBConstants.BrowserProfileName,
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, IDocument>(StringComparer.OrdinalIgnoreCase) { [pageUrl] = doc });

        var parser = new ModDBPageParser(playwright.Object, new Mock<ILogger<ModDBPageParser>>().Object);

        // Act
        var parsed = await parser.ParseAsync(pageUrl);

        // Assert
        var images = parsed.Sections.OfType<Image>().ToList();
        var videos = parsed.Sections.OfType<Video>().ToList();

        var image = Assert.Single(images);
        Assert.Equal("Ingame Base Screenshot", image.Title);

        var video = Assert.Single(videos);
        Assert.Equal("Ingame Trailer Video", video.Title);
        Assert.Equal("https://www.moddb.com/mods/test-mod/videos/trailer-1", video.EmbedUrl);
        Assert.Equal("https://media.moddb.com/cache/images/videos/1/1/trailer.png", video.ThumbnailUrl);
    }

    /// <summary>
    /// Verifies that gallery videos in legitimate and substring-named containers are parsed
    /// while non-video iframes (like download widgets) and recommendation boxes are ignored.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ParseAsync_WithWidgetsAndRecommendations_ExtractsGalleryVideosAndIgnoresNonVideoWidgetsAsync()
    {
        // Arrange
        const string pageUrl = "https://www.moddb.com/mods/zhe/downloads/patch-1";
        const string parentUrl = "https://www.moddb.com/mods/zhe";
        var documents = new Dictionary<string, IDocument>(StringComparer.OrdinalIgnoreCase)
        {
            [pageUrl] = await CreateDocumentAsync("""
                <html><body>
                  <div id="downloadsinfo">
                    <div class="row clear"><h5>Filename</h5><span class="summary">ZHE_Patch.zip</span></div>
                    <div class="row clear"><h5>Size</h5><span class="summary">500kb</span></div>
                    <div class="row clear"><a class="button buttonlarge" href="/downloads/start/313875">Download Now</a></div>
                  </div>
                  <div class="video-container">
                    <iframe src="https://www.moddb.com/mods/zhe/downloads/zhe-installer/widget" width="468" height="60"></iframe>
                  </div>
                  <div id="mediaimage">
                    <img src="https://media.moddb.com/cache/images/downloads/1/314/313875/crop_120x90/Final.jpg" alt="Final Preview" />
                  </div>
                </body></html>
                """),
            [parentUrl] = await CreateDocumentAsync("""
                <html><body>
                  <h1>ZHE</h1>
                  <div class="sidecolumn">
                    <div class="avatar"><img src="https://static.moddb.com/html/cutoff/images/default/error_50x50.png" /></div>
                  </div>
                </body></html>
                """),
            [parentUrl + "/videos"] = await CreateDocumentAsync("""
                <html><body>
                  <div id="mediabrowse">
                    <div class="holder">
                      <a href="/mods/zhe/videos/real-trailer" title="Real Mod Trailer">
                        <img src="https://media.moddb.com/cache/images/videos/1/1/trailer.jpg" alt="Real Mod Trailer" />
                      </a>
                      <span class="title"><a href="/mods/zhe/videos/real-trailer">Real Mod Trailer</a></span>
                    </div>
                  </div>
                  <div class="similarity-note">
                    <div class="holder">
                      <a href="/mods/zhe/videos/similar-note-trailer" title="Similar Note Trailer">
                        <img src="https://media.moddb.com/cache/images/videos/3/3/similar.jpg" alt="Similar Note Trailer" />
                      </a>
                      <span class="title"><a href="/mods/zhe/videos/similar-note-trailer">Similar Note Trailer</a></span>
                    </div>
                  </div>
                  <div id="recommendations">
                    <h3>Recommended Content</h3>
                    <div class="holder">
                      <a href="/mods/other-mod/videos/other-trailer" title="Other Mod Trailer">
                        <img src="https://media.moddb.com/cache/images/videos/2/2/other.jpg" alt="Other Mod Trailer" />
                      </a>
                    </div>
                  </div>
                </body></html>
                """),
        };

        var playwright = CreatePlaywrightMock();
        playwright
            .Setup(service => service.FetchAndParsePersistentManyAsync(
                ModDBConstants.BrowserProfileName,
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<CancellationToken>()))
            .Returns((string _, IReadOnlyList<string> urls, CancellationToken _) =>
            {
                var result = new Dictionary<string, IDocument>(StringComparer.OrdinalIgnoreCase);
                foreach (var url in urls)
                {
                    if (documents.TryGetValue(url, out var doc))
                    {
                        result[url] = doc;
                    }
                }

                return Task.FromResult<IReadOnlyDictionary<string, IDocument>>(result);
            });

        var parser = new ModDBPageParser(playwright.Object, new Mock<ILogger<ModDBPageParser>>().Object);

        // Act
        var parsed = await parser.ParseAsync(pageUrl);

        // Assert
        var videos = parsed.Sections.OfType<Video>().ToList();
        var images = parsed.Sections.OfType<Image>().ToList();

        // Real trailer and non-recommendation substring container (e.g. similarity-note) should be extracted,
        // while the download widget and recommendation box must be excluded.
        Assert.Equal(2, videos.Count);
        Assert.Contains(videos, v => v.Title == "Real Mod Trailer" &&
            v.EmbedUrl == "https://www.moddb.com/mods/zhe/videos/real-trailer" &&
            v.ThumbnailUrl == "https://media.moddb.com/cache/images/videos/1/1/trailer.jpg");
        Assert.Contains(videos, v => v.Title == "Similar Note Trailer" &&
            v.EmbedUrl == "https://www.moddb.com/mods/zhe/videos/similar-note-trailer" &&
            v.ThumbnailUrl == "https://media.moddb.com/cache/images/videos/3/3/similar.jpg");
        Assert.DoesNotContain(videos, v => v.Title == "Other Mod Trailer");

        // Preview image from file detail page should be extracted into Images
        var image = Assert.Single(images);
        Assert.Equal("Final Preview", image.Title);
        Assert.Equal("https://media.moddb.com/cache/images/downloads/1/314/313875/crop_120x90/Final.jpg", image.ThumbnailUrl);
        Assert.Equal("https://media.moddb.com/images/downloads/1/314/313875/Final.jpg", image.FullSizeUrl);

        // Error placeholder icon must be ignored
        Assert.Null(parsed.Context.IconUrl);
    }

    /// <summary>
    /// Verifies that ParseFileDetailAsync extracts the file-specific heading rather than
    /// the parent mod title inside the .headerbox container.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ParseFileDetailAsync_WithHeaderBox_ExtractsSpecificFileHeadingNotParentModNameAsync()
    {
        const string pageUrl = "https://www.moddb.com/mods/cc-generals-undone/downloads/generalsundone-v101-patch";
        var playwright = CreatePlaywrightMock();
        playwright
            .Setup(service => service.FetchAndParsePersistentAsync(
                ModDBConstants.BrowserProfileName,
                pageUrl,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(await CreateDocumentAsync("""
                <html>
                  <head>
                    <title>Generals Undone v1.01 Patch file - C&amp;C Generals Undone mod for C&amp;C: Generals Zero Hour - ModDB</title>
                  </head>
                  <body>
                    <div class="headerbox">
                      <h1><a href="/mods/cc-generals-undone">C&amp;C Generals Undone</a></h1>
                      <h2>C&amp;C: Generals Zero Hour mod | Released Aug 1, 2026</h2>
                    </div>
                    <div class="midcolumn">
                      <div class="heading">
                        <h2>Generals Undone v1.01 Patch</h2>
                      </div>
                      <div id="downloadsinfo">
                        <div class="row clear"><h5>Filename</h5><span class="summary">GeneralsUndone_v1.01_Patch.1.zip</span></div>
                        <div class="row clear"><h5>Category</h5><span class="summary">Patch</span></div>
                        <div class="row clear"><h5>Uploader</h5><span class="summary">WhiteSkull#9044</span></div>
                        <div class="row clear"><h5>Size</h5><span class="summary">188.3kb (192,819 bytes)</span></div>
                        <div class="row clear"><h5>MD5 Hash</h5><span class="summary">5350e475319f2eaed0ab92d445b7099a</span></div>
                        <div class="row clear"><a class="button buttonlarge" href="/downloads/start/314093">Download Now</a></div>
                      </div>
                      <div id="downloaddescription">
                        <p>This is a patched .csf file that contains the entries for the USA supply truck.</p>
                        <p>To Install:</p>
                        <p>1. Extract the .zip folder</p>
                      </div>
                      <div id="downloadsmedia">
                        <a href="https://media.moddb.com/images/downloads/1/314/314093/preview_full.png">
                          <img src="https://media.moddb.com/cache/images/downloads/1/314/314093/crop_120x90/preview.png" />
                        </a>
                      </div>
                    </div>
                  </body>
                </html>
                """));

        var parser = new ModDBPageParser(playwright.Object, new Mock<ILogger<ModDBPageParser>>().Object);

        var parsed = await parser.ParseFileDetailAsync(pageUrl);

        var file = Assert.Single(parsed.Sections.OfType<DownloadableFile>());
        Assert.Equal("Generals Undone v1.01 Patch", file.Name);
        Assert.Equal("GeneralsUndone_v1.01_Patch.1.zip", file.Filename);
        Assert.Equal("Patch", file.Category);
        Assert.Equal("5350e475319f2eaed0ab92d445b7099a", file.Md5Hash);
        Assert.Equal(192_819, file.SizeBytes);
        Assert.NotNull(file.Description);
        Assert.Contains("This is a patched .csf file", file.Description, StringComparison.Ordinal);
        Assert.Contains("To Install:", file.Description, StringComparison.Ordinal);
        Assert.NotNull(file.PreviewImages);
        Assert.Contains("https://media.moddb.com/images/downloads/1/314/314093/preview_full.png", file.PreviewImages);
    }

    /// <summary>
    /// Verifies that DeduplicateDownloadableFiles keeps distinct release files separate when they have
    /// different details/download URLs rather than collapsing them.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ParseAsync_WithMultipleDistinctReleases_KeepsBothReleasesSeparateAsync()
    {
        const string pageUrl = "https://www.moddb.com/mods/cc-generals-undone/downloads/cc-generals-undone";
        const string parentUrl = "https://www.moddb.com/mods/cc-generals-undone";
        var documents = new Dictionary<string, IDocument>(StringComparer.OrdinalIgnoreCase)
        {
            [pageUrl] = await CreateDocumentAsync("""
                <html>
                  <head><title>C&amp;C Generals Undone file - ModDB</title></head>
                  <body>
                    <div class="headerbox">
                      <h1><a href="/mods/cc-generals-undone">C&amp;C Generals Undone</a></h1>
                    </div>
                    <div class="midcolumn">
                      <h2>C&amp;C Generals Undone</h2>
                      <div id="downloadsinfo">
                        <div class="row clear"><h5>Filename</h5><span class="summary">GeneralsUndone_v1.0.zip</span></div>
                        <div class="row clear"><h5>Category</h5><span class="summary">Full Version</span></div>
                        <div class="row clear"><h5>Size</h5><span class="summary">289.6mb (303,663,235 bytes)</span></div>
                        <div class="row clear"><a class="button buttonlarge" href="/downloads/start/313719">Download Now</a></div>
                      </div>
                      <div id="downloaddescription">
                        <p>This is the first version of Undone.</p>
                      </div>
                    </div>
                  </body>
                </html>
                """),
            [parentUrl] = await CreateDocumentAsync("<html><body><h2>C&amp;C Generals Undone</h2></body></html>"),
            [parentUrl + "/downloads"] = await CreateDocumentAsync("""
                <div class="row rowcontent clear">
                  <div class="heading">
                    <h4><a href="/mods/cc-generals-undone/downloads/cc-generals-undone">C&amp;C Generals Undone</a></h4>
                    <span class="subheading"><time datetime="2026-08-02T12:00:00+00:00">Aug 2nd, 2026</time> - Full Version, 289.6mb</span>
                  </div>
                  <div class="actions">
                    <a href="/mods/cc-generals-undone/downloads/cc-generals-undone" class="button">Download</a>
                  </div>
                </div>
                <div class="row rowcontent clear">
                  <div class="heading">
                    <h4><a href="/mods/cc-generals-undone/downloads/generalsundone-v101-patch">Generals Undone v1.01 Patch</a></h4>
                    <span class="subheading"><time datetime="2026-08-07T12:00:00+00:00">Aug 7th, 2026</time> - Patch, 188.3kb</span>
                  </div>
                  <div class="actions">
                    <a href="/mods/cc-generals-undone/downloads/generalsundone-v101-patch" class="button">Download</a>
                  </div>
                </div>
                """),
            [parentUrl + "/addons"] = await CreateDocumentAsync("<html><body></body></html>"),
            [parentUrl + "/videos"] = await CreateDocumentAsync("<html><body></body></html>"),
            [parentUrl + "/images"] = await CreateDocumentAsync("<html><body></body></html>"),
            [parentUrl + "/reviews"] = await CreateDocumentAsync("<html><body></body></html>"),
            [parentUrl + "/articles"] = await CreateDocumentAsync("<html><body></body></html>"),
        };

        var playwright = CreatePlaywrightMock();
        playwright
            .Setup(service => service.FetchAndParsePersistentManyAsync(
                ModDBConstants.BrowserProfileName,
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<CancellationToken>()))
            .Returns((string _, IReadOnlyList<string> urls, CancellationToken _) =>
            {
                var result = new Dictionary<string, IDocument>(StringComparer.OrdinalIgnoreCase);
                foreach (var url in urls)
                {
                    if (documents.TryGetValue(url, out var doc))
                    {
                        result[url] = doc;
                    }
                }

                return Task.FromResult<IReadOnlyDictionary<string, IDocument>>(result);
            });

        var parser = new ModDBPageParser(playwright.Object, new Mock<ILogger<ModDBPageParser>>().Object);

        var parsed = await parser.ParseAsync(pageUrl);

        var releases = parsed.Sections.OfType<DownloadableFile>().ToList();
        Assert.Equal(2, releases.Count);

        var patch = Assert.Single(releases, r => r.DetailsUrl?.Contains("generalsundone-v101-patch", StringComparison.OrdinalIgnoreCase) == true);
        Assert.Equal("Generals Undone v1.01 Patch", patch.Name);
        Assert.Equal("Patch", patch.Category);

        var full = Assert.Single(releases, r => r.DetailsUrl?.EndsWith("cc-generals-undone", StringComparison.OrdinalIgnoreCase) == true);
        Assert.Equal("C&C Generals Undone", full.Name);
        Assert.Equal("Full Version", full.Category);
        Assert.Equal("GeneralsUndone_v1.0.zip", full.Filename);
        Assert.Contains("This is the first version of Undone.", full.Description, StringComparison.Ordinal);
    }

    /// <summary>
    /// Gets test data for size parsing formats.
    /// </summary>
    public static TheoryData<string, long> SizeTestData => new()
    {
        { "188.3kb (192,819 bytes)", 192819L },
        { "188.3kb", 192819L },
        { "9,72 MB", (long)(9.72 * 1024 * 1024) },
        { "1.07mb (1,125,450 bytes)", 1125450L },
        { "289.6 MB", (long)(289.6 * 1024 * 1024) },
        { "1,234.56 MB", (long)(1234.56 * 1024 * 1024) },
    };

    /// <summary>
    /// Tests that ParseFileDetail handles various size formats correctly.
    /// </summary>
    /// <param name="sizeString">The raw size string to test.</param>
    /// <param name="expectedBytes">The expected parsed size in bytes.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Theory]
    [MemberData(nameof(SizeTestData))]
    public async Task ParseFileDetail_WithVariousSizeFormats_CorrectlyParsesSizeBytesAsync(string sizeString, long expectedBytes)
    {
        // Arrange
        var playwright = CreatePlaywrightMock();
        var pageUrl = "https://www.moddb.com/mods/test-mod/downloads/test-file";
        var doc = await CreateDocumentAsync($"""
            <html><body>
              <div id="downloadsinfo">
                <div class="row clear"><h5>Filename</h5><span class="summary">test_file.zip</span></div>
                <div class="row clear"><h5>Size</h5><span class="summary">{sizeString}</span></div>
                <div class="row clear"><a class="button buttonlarge" href="/downloads/start/12345">Download Now</a></div>
              </div>
            </body></html>
            """);

        playwright
            .Setup(service => service.FetchAndParsePersistentAsync(
                ModDBConstants.BrowserProfileName,
                pageUrl,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(doc);

        var parser = new ModDBPageParser(playwright.Object, new Mock<ILogger<ModDBPageParser>>().Object);

        // Act
        var parsed = await parser.ParseFileDetailAsync(pageUrl);

        // Assert
        Assert.NotNull(parsed);
        var file = Assert.Single(parsed.Sections.OfType<DownloadableFile>());
        Assert.NotNull(file.SizeBytes);

        // Allow within 5% tolerance for floating-point calculations where byte count was not explicit
        if (sizeString.Contains("bytes", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Equal(expectedBytes, file.SizeBytes.Value);
        }
        else
        {
            var diff = Math.Abs(file.SizeBytes.Value - expectedBytes);
            Assert.True(diff < expectedBytes * 0.05, $"Parsed size {file.SizeBytes.Value} was not within tolerance of {expectedBytes}");
        }
    }

    /// <summary>
    /// Verifies that ParseFileDetailsManyAsync requests all URLs via FetchAndParsePersistentManyAsync
    /// and parses each document returned into a ParsedWebPage.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ParseFileDetailsManyAsync_MultipleUrls_CallsFetchAndParsePersistentManyAsyncAndParsesAllAsync()
    {
        // Arrange
        const string url1 = "https://www.moddb.com/games/cc-generals-zero-hour/addons/addon1";
        const string url2 = "https://www.moddb.com/games/cc-generals-zero-hour/downloads/release1";

        var doc1 = await CreateDocumentAsync("""
            <html><body>
              <div id="downloadsinfo">
                <div class="row clear"><h5>Filename</h5><span class="summary">addon1.zip</span></div>
                <div class="row clear"><a class="button buttonlarge" href="/addons/start/101">Download</a></div>
              </div>
            </body></html>
            """);

        var doc2 = await CreateDocumentAsync("""
            <html><body>
              <div id="downloadsinfo">
                <div class="row clear"><h5>Filename</h5><span class="summary">release1.zip</span></div>
                <div class="row clear"><a class="button buttonlarge" href="/downloads/start/202">Download</a></div>
              </div>
            </body></html>
            """);

        var playwright = CreatePlaywrightMock();
        playwright
            .Setup(service => service.FetchAndParsePersistentManyAsync(
                ModDBConstants.BrowserProfileName,
                It.Is<IReadOnlyList<string>>(urls => urls.Contains(url1) && urls.Contains(url2)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, IDocument>(StringComparer.OrdinalIgnoreCase)
            {
                [url1] = doc1,
                [url2] = doc2,
            });

        var parser = new ModDBPageParser(playwright.Object, new Mock<ILogger<ModDBPageParser>>().Object);

        // Act
        var results = await parser.ParseFileDetailsManyAsync([url1, url2]);

        // Assert
        Assert.Equal(2, results.Count);
        Assert.True(results.TryGetValue(url1, out var page1));
        var file1 = Assert.Single(page1!.Sections.OfType<DownloadableFile>());
        Assert.Equal("addon1.zip", file1.Name);
        Assert.Equal("https://www.moddb.com/addons/start/101", file1.DownloadUrl);

        Assert.True(results.TryGetValue(url2, out var page2));
        var file2 = Assert.Single(page2!.Sections.OfType<DownloadableFile>());
        Assert.Equal("release1.zip", file2.Name);
        Assert.Equal("https://www.moddb.com/downloads/start/202", file2.DownloadUrl);
    }

    /// <summary>
    /// Verifies that ParseFileDetailsManyAsync upgrades HTTP URLs to HTTPS before requesting them via
    /// FetchAndParsePersistentManyAsync and preserves the original requested HTTP URLs as keys in the result dictionary.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ParseFileDetailsManyAsync_WithHttpUrls_UpgradesToHttpsAndPreservesOriginalKeysAsync()
    {
        // Arrange
        const string httpUrl = "http://www.moddb.com/games/cc-generals-zero-hour/downloads/release-http";
        const string httpsUrl = "https://www.moddb.com/games/cc-generals-zero-hour/downloads/release-http";

        var doc = await CreateDocumentAsync("""
            <html><body>
              <div id="downloadsinfo">
                <div class="row clear"><h5>Filename</h5><span class="summary">release.zip</span></div>
                <div class="row clear"><a class="button buttonlarge" href="/downloads/start/500">Download</a></div>
              </div>
            </body></html>
            """);

        var playwright = CreatePlaywrightMock();
        playwright
            .Setup(service => service.FetchAndParsePersistentManyAsync(
                ModDBConstants.BrowserProfileName,
                It.Is<IReadOnlyList<string>>(urls => urls.Contains(httpsUrl) && !urls.Contains(httpUrl)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, IDocument>(StringComparer.OrdinalIgnoreCase)
            {
                [httpsUrl] = doc,
            });

        var parser = new ModDBPageParser(playwright.Object, new Mock<ILogger<ModDBPageParser>>().Object);

        // Act
        var results = await parser.ParseFileDetailsManyAsync([httpUrl]);

        // Assert
        Assert.Single(results);
        Assert.True(results.TryGetValue(httpUrl, out var parsedPage));
        var file = Assert.Single(parsedPage!.Sections.OfType<DownloadableFile>());
        Assert.Equal("release.zip", file.Name);
    }

    /// <summary>
    /// Protocol-relative media URLs (starting with //media.moddb.com) should normalize to https:// without duplicating base domain.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ParseAsync_ProtocolRelativeMediaUrls_NormalizesToHttpsSchemeAsync()
    {
        // Arrange
        const string pageUrl = "https://www.moddb.com/mods/test-mod/images";
        var doc = await CreateDocumentAsync("""
            <html><body>
              <div id="imagebox">
                <a href="/mods/test-mod/images/shot1">
                  <img alt="Screenshot One" src="//media.moddb.com/cache/images/mods/1/73/72174/crop_120x90/ScreenshotOne.png" />
                </a>
              </div>
            </body></html>
            """);

        var playwright = CreatePlaywrightMock();
        playwright
            .Setup(service => service.FetchAndParsePersistentManyAsync(
                ModDBConstants.BrowserProfileName,
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, IDocument>(StringComparer.OrdinalIgnoreCase) { [pageUrl] = doc });

        var parser = new ModDBPageParser(playwright.Object, new Mock<ILogger<ModDBPageParser>>().Object);

        // Act
        var parsed = await parser.ParseAsync(pageUrl);

        // Assert
        var image = Assert.Single(parsed.Sections.OfType<Image>());
        Assert.Equal("Screenshot One", image.Title);
        Assert.Equal("https://media.moddb.com/cache/images/mods/1/73/72174/crop_120x90/ScreenshotOne.png", image.ThumbnailUrl);
        Assert.Equal("https://media.moddb.com/images/mods/1/73/72174/ScreenshotOne.png", image.FullSizeUrl);
        Assert.DoesNotContain("www.moddb.com//", image.ThumbnailUrl);
    }

    /// <summary>
    /// Pagination links, RSS feeds, and navigation controls on video pages must be excluded from extracted videos.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ParseAsync_VideosPage_ExcludesPaginationAndRssLinksAsync()
    {
        // Arrange
        const string pageUrl = "https://www.moddb.com/mods/test-mod/videos";
        var doc = await CreateDocumentAsync("""
            <html><body>
              <div id="videosbrowse">
                <div class="row mediarow">
                  <div class="holder">
                    <a href="/mods/test-mod/videos/gameplay-trailer" title="Gameplay Trailer">
                      <img src="//media.moddb.com/cache/images/videos/1/1/trailer.png" alt="Gameplay Trailer" />
                    </a>
                  </div>
                </div>
                <div class="pagination">
                  <a href="/mods/test-mod/videos/page/2#videosbrowse">Next Media</a>
                  <a href="/mods/test-mod/videos/rss/feed">RSS</a>
                  <a class="prev" href="/mods/test-mod/videos/page/1">Previous</a>
                </div>
              </div>
            </body></html>
            """);

        var playwright = CreatePlaywrightMock();
        playwright
            .Setup(service => service.FetchAndParsePersistentManyAsync(
                ModDBConstants.BrowserProfileName,
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, IDocument>(StringComparer.OrdinalIgnoreCase) { [pageUrl] = doc });

        var parser = new ModDBPageParser(playwright.Object, new Mock<ILogger<ModDBPageParser>>().Object);

        // Act
        var parsed = await parser.ParseAsync(pageUrl);

        // Assert
        var videos = parsed.Sections.OfType<Video>().ToList();
        var video = Assert.Single(videos);
        Assert.Equal("Gameplay Trailer", video.Title);
        Assert.Equal("https://media.moddb.com/cache/images/videos/1/1/trailer.png", video.ThumbnailUrl);
        Assert.DoesNotContain(videos, v => v.Title.Contains("Next", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(videos, v => v.Title.Contains("RSS", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(videos, v => v.Title.Contains("Previous", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies that when a mod has both releases and addons, ParseAsync enriches both top releases and top addons
    /// in the same session without dropping releases when addons have newer timestamps.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ParseAsync_ModWithReleasesAndAddons_EnrichesBothTopReleasesAndAddonsInSingleSessionAsync()
    {
        // Arrange
        const string pageUrl = "https://www.moddb.com/mods/rich-mod";
        const string release1Url = "https://www.moddb.com/mods/rich-mod/downloads/release-1";
        const string addon1Url = "https://www.moddb.com/mods/rich-mod/addons/addon-1";

        var documents = new Dictionary<string, IDocument>(StringComparer.OrdinalIgnoreCase)
        {
            [pageUrl] = await CreateDocumentAsync("<html><body><h1>Rich Mod</h1></body></html>"),
            [pageUrl + "/downloads"] = await CreateDocumentAsync("""
                <div class="row file">
                  <h4><a href="/mods/rich-mod/downloads/release-1">Release 1</a></h4>
                  <span class="size">500 MB</span>
                  <time datetime="2024-01-01">Jan 1 2024</time>
                  <a href="/downloads/start/1001">Download</a>
                </div>
                """),
            [pageUrl + "/addons"] = await CreateDocumentAsync("""
                <div class="row file">
                  <h4><a href="/mods/rich-mod/addons/addon-1">Addon 1</a></h4>
                  <span class="size">10 MB</span>
                  <time datetime="2026-06-01">Jun 1 2026</time>
                  <a href="/addons/start/2001">Download</a>
                </div>
                """),
            [pageUrl + "/videos"] = await CreateDocumentAsync("<html><body></body></html>"),
            [pageUrl + "/images"] = await CreateDocumentAsync("<html><body></body></html>"),
            [pageUrl + "/reviews"] = await CreateDocumentAsync("<html><body></body></html>"),
            [pageUrl + "/articles"] = await CreateDocumentAsync("<html><body></body></html>"),
            [release1Url] = await CreateDocumentAsync("""
                <html><body>
                  <h1><a>Release 1</a></h1>
                  <div id="downloadsinfo">
                    <div class="row clear"><h5>Filename</h5><span class="summary">release_1.zip</span></div>
                    <div class="row clear"><h5>MD5 Hash</h5><span class="summary">hash_release_1</span></div>
                    <div class="row clear"><h5>Size</h5><span class="summary">500mb (524,288,000 bytes)</span></div>
                    <div class="row clear"><a class="button" href="/downloads/start/1001">Download Now</a></div>
                  </div>
                  <div id="downloadsummary">Full release v1 description</div>
                </body></html>
                """),
            [addon1Url] = await CreateDocumentAsync("""
                <html><body>
                  <h1><a>Addon 1</a></h1>
                  <div id="downloadsinfo">
                    <div class="row clear"><h5>Filename</h5><span class="summary">addon_1.zip</span></div>
                    <div class="row clear"><h5>MD5 Hash</h5><span class="summary">hash_addon_1</span></div>
                    <div class="row clear"><h5>Size</h5><span class="summary">10mb (10,485,760 bytes)</span></div>
                    <div class="row clear"><a class="button" href="/addons/start/2001">Download Now</a></div>
                  </div>
                  <div id="downloadsummary">Addon 1 map pack description</div>
                </body></html>
                """),
        };

        var playwright = CreatePlaywrightMock();
        playwright
            .Setup(service => service.FetchAndParsePersistentManyAsync(
                ModDBConstants.BrowserProfileName,
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<CancellationToken>()))
            .Returns((string _, IReadOnlyList<string> urls, CancellationToken _) =>
            {
                var result = new Dictionary<string, IDocument>(StringComparer.OrdinalIgnoreCase);
                foreach (var url in urls)
                {
                    if (documents.TryGetValue(url, out var doc))
                    {
                        result[url] = doc;
                    }
                }

                return Task.FromResult<IReadOnlyDictionary<string, IDocument>>(result);
            });

        var parser = new ModDBPageParser(playwright.Object, new Mock<ILogger<ModDBPageParser>>().Object);

        // Act
        var parsed = await parser.ParseAsync(pageUrl);

        // Assert
        var files = parsed.Sections.OfType<DownloadableFile>().ToList();
        var release = Assert.Single(files, f => f.FileSectionType == FileSectionType.Downloads);
        Assert.Equal("Release 1", release.Name);
        Assert.Equal("release_1.zip", release.Filename);
        Assert.Equal("hash_release_1", release.Md5Hash);
        Assert.Equal("Full release v1 description", release.Description);

        var addon = Assert.Single(files, f => f.FileSectionType == FileSectionType.Addons);
        Assert.Equal("Addon 1", addon.Name);
        Assert.Equal("addon_1.zip", addon.Filename);
        Assert.Equal("hash_addon_1", addon.Md5Hash);
        Assert.Equal("Addon 1 map pack description", addon.Description);

        // Verify both the release and addon detail URLs were fetched during enrichment
        playwright.Verify(
            service => service.FetchAndParsePersistentManyAsync(
                ModDBConstants.BrowserProfileName,
                It.Is<IReadOnlyList<string>>(urls => urls.Contains(release1Url) && urls.Contains(addon1Url)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies CanParse accepts valid ModDB HTTP(S) domains and rejects invalid or attacker origins.
    /// </summary>
    /// <param name="url">The URL to test.</param>
    /// <param name="expected">The expected parseability result.</param>
    [Theory]
    [InlineData("https://www.moddb.com/mods/test-mod", true)]
    [InlineData("http://moddb.com/mods/test-mod", true)]
    [InlineData("https://media.moddb.com/downloads/test.zip", true)]
    [InlineData("https://localhost/?source=moddb.com", false)]
    [InlineData("https://moddb.com.attacker.example/mods/test", false)]
    [InlineData("https://attacker-moddb.com/mods/test", false)]
    [InlineData("not-a-valid-url", false)]
    [InlineData("ftp://www.moddb.com/mods/test", false)]
    public void CanParse_ValidatesSchemeAndHostCorrectly(string url, bool expected)
    {
        // Arrange
        var playwright = new Mock<IPlaywrightService>();
        var parser = new ModDBPageParser(playwright.Object, new Mock<ILogger<ModDBPageParser>>().Object);

        // Act
        var result = parser.CanParse(url);

        // Assert
        Assert.Equal(expected, result);
    }

    /// <summary>
    /// Verifies that IsDirectDownloadUrl fails closed, only returning true for valid ModDB start/mirror
    /// endpoints or trusted ModDB CDN domains, and false for listing pages, external domains, or attacker origins.
    /// </summary>
    /// <param name="url">The URL to test.</param>
    /// <param name="expected">The expected validation result.</param>
    [Theory]
    [InlineData("https://www.moddb.com/downloads/start/310120", true)]
    [InlineData("https://www.moddb.com/addons/start/302328", true)]
    [InlineData("https://www.moddb.com/downloads/mirror/310120", true)]
    [InlineData("https://www.moddb.com/addons/mirror/302328", true)]
    [InlineData("/downloads/start/310120", true)]
    [InlineData("/addons/start/302328", true)]
    [InlineData("https://media.moddb.com/images/downloads/1/1/file.zip", true)]
    [InlineData("https://files.moddb.com/downloads/1/1/file.zip", true)]
    [InlineData("https://downloads.moddb.com/files/file.zip", true)]
    [InlineData("https://www.moddb.com/mods/cool-mod/downloads/cool-file", false)]
    [InlineData("https://www.moddb.com/mods/cool-mod", false)]
    [InlineData("https://www.moddb.com/games/cc-generals-zero-hour/downloads", false)]
    [InlineData("https://moddb.com.attacker.example/downloads/start/310120", false)]
    [InlineData("https://moddb.com@attacker.example/downloads/start/310120", false)]
    [InlineData("https://external-site.com/downloads/start/310120", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsDirectDownloadUrl_ValidatesEndpointsAndHosts(string? url, bool expected)
    {
        // Act
        var result = ModDBPageParser.IsDirectDownloadUrl(url);

        // Assert
        Assert.Equal(expected, result);
    }

    /// <summary>
    /// Verifies that ParseAsync throws ArgumentException when given an unsupported or attacker origin.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ParseAsync_UnsupportedUrlOrigin_ThrowsArgumentExceptionAsync()
    {
        // Arrange
        var playwright = new Mock<IPlaywrightService>();
        var parser = new ModDBPageParser(playwright.Object, new Mock<ILogger<ModDBPageParser>>().Object);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => parser.ParseAsync("https://evil.example.com/mods/fake"));
    }

    /// <summary>
    /// Verifies that ParseFileDetailAsync throws ArgumentException when given an unsupported or attacker origin.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ParseFileDetailAsync_UnsupportedUrlOrigin_ThrowsArgumentExceptionAsync()
    {
        // Arrange
        var playwright = new Mock<IPlaywrightService>();
        var parser = new ModDBPageParser(playwright.Object, new Mock<ILogger<ModDBPageParser>>().Object);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => parser.ParseFileDetailAsync("https://evil.example.com/downloads/start/1"));
    }

    /// <summary>
    /// Verifies that ParseFileDetailsManyAsync throws ArgumentException when any URL has an unsupported origin.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ParseFileDetailsManyAsync_UnsupportedUrlOrigin_ThrowsArgumentExceptionAsync()
    {
        // Arrange
        var playwright = new Mock<IPlaywrightService>();
        var parser = new ModDBPageParser(playwright.Object, new Mock<ILogger<ModDBPageParser>>().Object);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => parser.ParseFileDetailsManyAsync(
            ["https://www.moddb.com/downloads/1", "https://attacker.example/downloads/2"]));
    }

    /// <summary>
    /// Verifies that ParseAsync(url, html) throws ArgumentException when given an unsupported origin.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ParseAsync_WithHtml_UnsupportedUrlOrigin_ThrowsArgumentExceptionAsync()
    {
        // Arrange
        var playwright = new Mock<IPlaywrightService>();
        var parser = new ModDBPageParser(playwright.Object, new Mock<ILogger<ModDBPageParser>>().Object);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => parser.ParseAsync("https://evil.example.com/mods/fake", "<html></html>"));
    }

    /// <summary>
    /// Verifies that ParseFileDetailsManyAsync treats missing or failed URLs in the Playwright response
    /// as soft failures, returning successful documents without throwing an exception.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ParseFileDetailsManyAsync_WithMissingOrFailedDocument_OmitsFailedUrlAndReturnsSuccessfulOnesAsync()
    {
        // Arrange
        const string goodUrl = "https://www.moddb.com/games/cc-generals-zero-hour/downloads/good-file";
        const string failedUrl = "https://www.moddb.com/games/cc-generals-zero-hour/downloads/failed-file";

        var goodDoc = await CreateDocumentAsync("""
            <html><body>
              <div id="downloadsinfo">
                <div class="row clear"><h5>Filename</h5><span class="summary">good.zip</span></div>
                <div class="row clear"><a class="button buttonlarge" href="/downloads/start/101">Download</a></div>
              </div>
            </body></html>
            """);

        var playwright = CreatePlaywrightMock();
        playwright
            .Setup(service => service.FetchAndParsePersistentManyAsync(
                ModDBConstants.BrowserProfileName,
                It.Is<IReadOnlyList<string>>(urls => urls.Contains(goodUrl) && urls.Contains(failedUrl)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, IDocument>(StringComparer.OrdinalIgnoreCase)
            {
                // failedUrl is intentionally missing from the returned fetched dictionary
                [goodUrl] = goodDoc,
            });

        var parser = new ModDBPageParser(playwright.Object, new Mock<ILogger<ModDBPageParser>>().Object);

        // Act
        var results = await parser.ParseFileDetailsManyAsync([goodUrl, failedUrl]);

        // Assert
        Assert.Single(results);
        Assert.True(results.ContainsKey(goodUrl));
        Assert.False(results.ContainsKey(failedUrl));
        var file = Assert.Single(results[goodUrl].Sections.OfType<DownloadableFile>());
        Assert.Equal("good.zip", file.Name);
    }

    /// <summary>
    /// Verifies that ParseAsync uses a single canonical base URL (stripping query parameters and fragments)
    /// so section URLs requested and fetched match consistently.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ParseAsync_CanonicalBaseUrl_StripsQueryParamsAndFragmentsForSectionSweepsAsync()
    {
        // Arrange
        const string pageUrl = "https://www.moddb.com/mods/example-mod?filter=t&sort=date-desc#main";
        const string canonicalBase = "https://www.moddb.com/mods/example-mod";

        var documents = new Dictionary<string, IDocument>(StringComparer.OrdinalIgnoreCase)
        {
            [pageUrl] = await CreateDocumentAsync("<html><body><h1>Example Mod</h1></body></html>"),
            [canonicalBase + "/downloads"] = await CreateDocumentAsync("""
                <div class="row file"><h4>Example File</h4><span class="size">5 MB</span><a href="/downloads/start/123">Download</a></div>
                """),
            [canonicalBase + "/addons"] = await CreateDocumentAsync("<html><body></body></html>"),
            [canonicalBase + "/videos"] = await CreateDocumentAsync("<html><body></body></html>"),
            [canonicalBase + "/images"] = await CreateDocumentAsync("<html><body></body></html>"),
            [canonicalBase + "/reviews"] = await CreateDocumentAsync("<html><body></body></html>"),
            [canonicalBase + "/articles"] = await CreateDocumentAsync("<html><body></body></html>"),
        };

        var playwright = CreatePlaywrightMock();
        playwright
            .Setup(service => service.FetchAndParsePersistentManyAsync(
                ModDBConstants.BrowserProfileName,
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<CancellationToken>()))
            .Returns((string _, IReadOnlyList<string> urls, CancellationToken _) =>
            {
                var result = new Dictionary<string, IDocument>(StringComparer.OrdinalIgnoreCase);
                foreach (var url in urls)
                {
                    if (documents.TryGetValue(url, out var doc))
                    {
                        result[url] = doc;
                    }
                }

                return Task.FromResult<IReadOnlyDictionary<string, IDocument>>(result);
            });

        var parser = new ModDBPageParser(playwright.Object, new Mock<ILogger<ModDBPageParser>>().Object);

        // Act
        var parsed = await parser.ParseAsync(pageUrl);

        // Assert
        var file = Assert.Single(parsed.Sections.OfType<DownloadableFile>());
        Assert.Equal("Example File", file.Name);
        Assert.Equal("https://www.moddb.com/downloads/start/123", file.DownloadUrl);
    }

    private static async Task<IDocument> CreateDocumentAsync(string html)
    {
        var browsingContext = BrowsingContext.New(Configuration.Default);
        return await browsingContext.OpenAsync(request => request.Content(html));
    }

    private static Mock<IPlaywrightService> CreatePlaywrightMock()
    {
        var playwright = new Mock<IPlaywrightService>(MockBehavior.Strict);
        playwright
            .Setup(service => service.ExecuteInPersistentContextAsync(
                It.IsAny<string>(),
                It.IsAny<Func<Task<ParsedWebPage>>>(),
                It.IsAny<CancellationToken>()))
            .Returns((string _, Func<Task<ParsedWebPage>> op, CancellationToken _) => op());
        return playwright;
    }
}
