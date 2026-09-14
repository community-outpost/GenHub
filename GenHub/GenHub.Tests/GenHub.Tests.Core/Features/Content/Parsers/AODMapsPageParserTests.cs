using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using GenHub.Core.Models.Parsers;
using GenHub.Features.Content.Services.Parsers;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GenHub.Tests.Core.Features.Content.Parsers;

/// <summary>
/// Regression tests for <see cref="AODMapsPageParser"/> URL resolution and markup extraction.
/// </summary>
public sealed class AODMapsPageParserTests
{
    private readonly AODMapsPageParser _parser;

    /// <summary>
    /// Initializes a new instance of the <see cref="AODMapsPageParserTests"/> class.
    /// </summary>
    public AODMapsPageParserTests()
    {
        var factoryMock = new Mock<IHttpClientFactory>();
        _parser = new AODMapsPageParser(factoryMock.Object, NullLogger<AODMapsPageParser>.Instance);
    }

    /// <summary>
    /// Verifies path-relative download URLs and thumbnails in gallery pages resolve against the current page URL.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ParseAsync_PathRelativeGalleryUrls_ResolvesAgainstPageUrlAsync()
    {
        // Arrange
        var pageUrl = "https://aodmaps.com/AOA/index.html";
        var html = """
            <html><body>
              <header id="header"><h1>AOA Maps</h1></header>
              <div id="gallery">
                <ul class="nospace clear">
                  <li>
                    <a href="ccount/click.php?id=4P_1_1" download>
                      <img src="preview.png" alt="">
                      <span class="name">4P AOD Map</span>
                    </a>
                  </li>
                </ul>
              </div>
            </body></html>
            """;

        // Act
        var result = await _parser.ParseAsync(pageUrl, html);

        // Assert
        var file = Assert.Single(result.Sections.OfType<DownloadableFile>());
        Assert.Equal("https://aodmaps.com/AOA/ccount/click.php?id=4P_1_1", file.DownloadUrl);
        Assert.Equal("https://aodmaps.com/AOA/preview.png", file.ThumbnailUrl);
        Assert.Equal("4P AOD Map", file.Name);
    }

    /// <summary>
    /// Verifies legacy pashacnc.com download links are rewritten to aodmaps.com.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ParseAsync_PashaCncDeadLinks_RewritesDomainToAodMapsAsync()
    {
        // Arrange
        var pageUrl = "https://aodmaps.com/gallery.html";
        var html = """
            <html><body>
              <div id="gallery">
                <ul class="nospace clear">
                  <li>
                    <a href="https://www.pashacnc.com/ccount/click.php?id=6P_3_29" download>
                      <span class="name">6P Pasha Fire Circle</span>
                    </a>
                  </li>
                </ul>
              </div>
            </body></html>
            """;

        // Act
        var result = await _parser.ParseAsync(pageUrl, html);

        // Assert
        var file = Assert.Single(result.Sections.OfType<DownloadableFile>());
        Assert.Equal("https://www.aodmaps.com/ccount/click.php?id=6P_3_29", file.DownloadUrl);
    }

    /// <summary>
    /// Verifies map maker container relative download and preview URLs resolve against the page URL.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ParseAsync_MapMakerRelativeUrls_ResolvesAgainstPageUrlAsync()
    {
        // Arrange
        var pageUrl = "https://aodmaps.com/mapmakers/MM_P/John/John.html";
        var html = """
            <html><body>
              <header id="header"><h1>John's AOD Maps</h1></header>
              <main class="hoc container clear">
                <div class="content">
                  <h1>Desert Storm</h1>
                  <img class="imgl borderedbox inspace-5" src="shots/desert.jpg" />
                  <p1>- Type: Survival<br>- Players: 4</p1>
                  <p><a href="downloads/desert_storm.zip" download>Download Map</a></p>
                </div>
              </main>
            </body></html>
            """;

        // Act
        var result = await _parser.ParseAsync(pageUrl, html);

        // Assert
        var file = Assert.Single(result.Sections.OfType<DownloadableFile>());
        Assert.Equal("https://aodmaps.com/mapmakers/MM_P/John/downloads/desert_storm.zip", file.DownloadUrl);
        Assert.Equal("https://aodmaps.com/mapmakers/MM_P/John/shots/desert.jpg", file.ThumbnailUrl);
        Assert.Equal("Desert Storm", file.Name);
        Assert.Equal("John", file.Uploader);
    }
}
