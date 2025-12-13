using GenHub.Features.Content.Services.CommunityOutpost.Models;
using Microsoft.Extensions.Logging;
using Moq;

namespace GenHub.Tests.Core.Features.Content.CommunityOutpost;

/// <summary>
/// Tests for <see cref="GenPatcherDatParser"/>.
/// </summary>
public class GenPatcherDatParserTests
{
    private readonly Mock<ILogger> _loggerMock;
    private readonly GenPatcherDatParser _parser;

    /// <summary>
    /// Initializes a new instance of the <see cref="GenPatcherDatParserTests"/> class.
    /// </summary>
    public GenPatcherDatParserTests()
    {
        _loggerMock = new Mock<ILogger>();
        _parser = new GenPatcherDatParser(_loggerMock.Object);
    }

    /// <summary>
    /// Verifies that Parse correctly extracts the catalog version from the header line.
    /// </summary>
    [Fact]
    public void Parse_ExtractsCatalogVersion()
    {
        // Arrange
        var content = "2.13                ;;\r\n108e 019955034 gentool.net https://example.com/108e.dat";

        // Act
        var catalog = _parser.Parse(content);

        // Assert
        Assert.Equal("2.13", catalog.CatalogVersion);
    }

    /// <summary>
    /// Verifies that Parse correctly parses content items with all fields.
    /// </summary>
    [Fact]
    public void Parse_ParsesContentItemCorrectly()
    {
        // Arrange
        var content = "2.13                ;;\r\n108e 019955034 gentool.net https://example.com/108e.dat";

        // Act
        var catalog = _parser.Parse(content);

        // Assert
        Assert.Single(catalog.Items);
        var item = catalog.Items[0];
        Assert.Equal("108e", item.ContentCode);
        Assert.Equal(19955034L, item.FileSize);
        Assert.Single(item.Mirrors);
        Assert.Equal("gentool.net", item.Mirrors[0].Name);
        Assert.Equal("https://example.com/108e.dat", item.Mirrors[0].Url);
    }

    /// <summary>
    /// Verifies that Parse groups multiple mirrors for the same content code.
    /// </summary>
    [Fact]
    public void Parse_GroupsMirrorsForSameContentCode()
    {
        // Arrange
        var content = @"2.13                ;;
108e 019955034 gentool.net https://gentool.net/108e.dat
108e 019955034 legi.cc https://legi.cc/108e.dat
108e 019955034 drive.google.com https://drive.google.com/108e";

        // Act
        var catalog = _parser.Parse(content);

        // Assert
        Assert.Single(catalog.Items);
        Assert.Equal(3, catalog.Items[0].Mirrors.Count);
        Assert.Contains(catalog.Items[0].Mirrors, m => m.Name == "gentool.net");
        Assert.Contains(catalog.Items[0].Mirrors, m => m.Name == "legi.cc");
        Assert.Contains(catalog.Items[0].Mirrors, m => m.Name == "drive.google.com");
    }

    /// <summary>
    /// Verifies that Parse handles multiple different content codes.
    /// </summary>
    [Fact]
    public void Parse_HandlesMultipleContentCodes()
    {
        // Arrange
        var content = @"2.13                ;;
108e 019955034 gentool.net https://example.com/108e.dat
gent 003619277 gentool.net https://example.com/gent.dat
cbbs 003754194 legi.cc https://legi.cc/cbbs.dat";

        // Act
        var catalog = _parser.Parse(content);

        // Assert
        Assert.Equal(3, catalog.Items.Count);
        Assert.Contains(catalog.Items, i => i.ContentCode == "108e");
        Assert.Contains(catalog.Items, i => i.ContentCode == "gent");
        Assert.Contains(catalog.Items, i => i.ContentCode == "cbbs");
    }

    /// <summary>
    /// Verifies that Parse returns empty catalog for empty content.
    /// </summary>
    [Fact]
    public void Parse_ReturnsEmptyForEmptyContent()
    {
        // Act
        var catalog = _parser.Parse(string.Empty);

        // Assert
        Assert.Empty(catalog.Items);
        Assert.Equal("unknown", catalog.CatalogVersion);
    }

    /// <summary>
    /// Verifies that Parse handles content with only version header.
    /// </summary>
    [Fact]
    public void Parse_HandlesOnlyVersionHeader()
    {
        // Arrange
        var content = "2.13                ;;";

        // Act
        var catalog = _parser.Parse(content);

        // Assert
        Assert.Empty(catalog.Items);
        Assert.Equal("2.13", catalog.CatalogVersion);
    }

    /// <summary>
    /// Verifies that GetPreferredDownloadUrl prefers legi.cc mirrors.
    /// </summary>
    [Fact]
    public void GetPreferredDownloadUrl_PrefersLegiMirror()
    {
        // Arrange
        var item = new GenPatcherContentItem
        {
            ContentCode = "108e",
            FileSize = 19955034L,
            Mirrors = new()
            {
                new() { Name = "gentool.net", Url = "https://gentool.net/108e.dat" },
                new() { Name = "legi.cc", Url = "https://legi.cc/108e.dat" },
                new() { Name = "drive.google.com", Url = "https://drive.google.com/108e" },
            },
        };

        // Act
        var url = GenPatcherDatParser.GetPreferredDownloadUrl(item);

        // Assert
        Assert.Equal("https://legi.cc/108e.dat", url);
    }

    /// <summary>
    /// Verifies that GetPreferredDownloadUrl falls back to gentool.net when no legi.cc mirror.
    /// </summary>
    [Fact]
    public void GetPreferredDownloadUrl_FallsBackToGentool()
    {
        // Arrange
        var item = new GenPatcherContentItem
        {
            ContentCode = "drtx",
            FileSize = 100465954L,
            Mirrors = new()
            {
                new() { Name = "gentool.net", Url = "https://gentool.net/drtx.dat" },
                new() { Name = "drive.google.com", Url = "https://drive.google.com/drtx" },
            },
        };

        // Act
        var url = GenPatcherDatParser.GetPreferredDownloadUrl(item);

        // Assert
        Assert.Equal("https://gentool.net/drtx.dat", url);
    }

    /// <summary>
    /// Verifies that GetOrderedDownloadUrls returns URLs in preference order.
    /// </summary>
    [Fact]
    public void GetOrderedDownloadUrls_ReturnsInPreferenceOrder()
    {
        // Arrange
        var item = new GenPatcherContentItem
        {
            ContentCode = "108e",
            FileSize = 19955034L,
            Mirrors = new()
            {
                new() { Name = "drive.google.com", Url = "https://drive.google.com/108e" },
                new() { Name = "gentool.net", Url = "https://gentool.net/108e.dat" },
                new() { Name = "legi.cc", Url = "https://legi.cc/108e.dat" },
            },
        };

        // Act
        var urls = GenPatcherDatParser.GetOrderedDownloadUrls(item);

        // Assert
        Assert.Equal(3, urls.Count);
        Assert.Equal("https://legi.cc/108e.dat", urls[0]);
        Assert.Equal("https://gentool.net/108e.dat", urls[1]);
        Assert.Equal("https://drive.google.com/108e", urls[2]);
    }
}
