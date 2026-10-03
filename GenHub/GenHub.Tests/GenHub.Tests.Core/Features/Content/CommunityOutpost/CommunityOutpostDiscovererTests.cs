using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.CommunityOutpost;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Results;
using GenHub.Features.Content.Services.CommunityOutpost;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Features.Content.CommunityOutpost;

/// <summary>
/// Tests for CommunityOutpostDiscoverer to verify Community Patch discovery.
/// </summary>
public class CommunityOutpostDiscovererTests
{
    /// <summary>
    /// Verifies that the Community Patch regex pattern matches the Generals ZH date-based file pattern.
    /// </summary>
    [Fact]
    public void CommunityPatchRegex_MatchesGeneralsZhDateFilePattern()
    {
        // Arrange
        var htmlContent = @"<a href=""https://legi.cc/patch/generalszh-2026-01-28.zip"">Download Latest</a>";
        var regex = CommunityOutpostDiscoverer.CommunityPatchRegex();

        // Act
        var match = regex?.Match(htmlContent);

        // Assert
        Assert.NotNull(match);
        Assert.True(match.Success);
        Assert.Contains("generalszh-2026-01-28.zip", match.Groups[1].Value);
        Assert.Equal("2026-01-28", match.Groups[2].Value);
    }

    /// <summary>
    /// Verifies that the Community Patch regex pattern matches the weekly filename pattern.
    /// </summary>
    [Fact]
    public void CommunityPatchRegex_MatchesWeeklyFilenamePattern()
    {
        // Arrange
        var htmlContent = @"<a href=""generalszh-weekly-2026-01-28.zip"">Download</a>";
        var regex = CommunityOutpostDiscoverer.CommunityPatchRegex();

        // Act
        var match = regex?.Match(htmlContent);

        // Assert
        Assert.NotNull(match);
        Assert.True(match.Success);
        Assert.Contains("generalszh-weekly-2026-01-28.zip", match.Groups[1].Value);
        Assert.Equal("2026-01-28", match.Groups[2].Value);
    }

    /// <summary>
    /// Verifies that the Community Patch regex matches the retail underscore pattern hosted on legi.cc.
    /// </summary>
    [Fact]
    public void CommunityPatchRegex_MatchesRetailUnderscoreFilenamePattern()
    {
        // Arrange
        var htmlContent = @"<a href=""https://legi.cc/patch/generalszh_23-07-2026.zip"">Download Retail</a>";
        var regex = CommunityOutpostDiscoverer.CommunityPatchRegex();

        // Act
        var match = regex?.Match(htmlContent);

        // Assert
        Assert.NotNull(match);
        Assert.True(match.Success);
        Assert.Contains("generalszh_23-07-2026.zip", match.Groups[1].Value);
        Assert.Equal("23-07-2026", match.Groups[2].Value);
    }

    /// <summary>
    /// Verifies that the Community Patch regex matches the NonRet stream build filename pattern hosted on legi.cc.
    /// </summary>
    [Fact]
    public void CommunityPatchRegex_MatchesNonRetailFilenamePattern()
    {
        // Arrange
        var htmlContent = @"<a href=""https://legi.cc/patch/generalszh_11-09-2026_NonRet.zip"">Download Non-Retail</a>";
        var regex = CommunityOutpostDiscoverer.CommunityPatchRegex();

        // Act
        var match = regex?.Match(htmlContent);

        // Assert
        Assert.NotNull(match);
        Assert.True(match.Success);
        Assert.Contains("generalszh_11-09-2026_NonRet.zip", match.Groups[1].Value);
        Assert.Equal("11-09-2026", match.Groups[2].Value);
    }

    /// <summary>
    /// Verifies that the Community Patch ID follows the required five-segment format.
    /// </summary>
    [Fact]
    public void CommunityPatchIdFormat_SpecificationDocumentation()
    {
        // Arrange
        var versionDate = "2026-01-28";
        var providerName = CommunityOutpostConstants.PublisherType;
        var expectedId = $"1.{versionDate.Replace("-", string.Empty)}.{providerName}.gameclient.community-patch";

        // Act
        var segments = expectedId.Split('.');

        // Assert
        Assert.Equal(5, segments.Length);
        Assert.Equal("1", segments[0]); // schema version
        Assert.Equal("20260128", segments[1]); // user version (date)
        Assert.Equal("communityoutpost", segments[2]); // publisher
        Assert.Equal("gameclient", segments[3]); // content type
        Assert.Equal("community-patch", segments[4]); // content name
    }

    /// <summary>
    /// Verifies that the Non-Retail Community Patch ID follows the required five-segment format with the nonret suffix.
    /// </summary>
    [Fact]
    public void CommunityPatchNonRetIdFormat_SpecificationDocumentation()
    {
        // Arrange
        var versionDate = "11-09-2026";
        var providerName = CommunityOutpostConstants.PublisherType;
        var expectedId = $"1.{versionDate.Replace("-", string.Empty)}.{providerName}.gameclient.{CommunityOutpostConstants.CommunityPatchNonRetCode}";

        // Act
        var segments = expectedId.Split('.');

        // Assert
        Assert.Equal(5, segments.Length);
        Assert.Equal("1", segments[0]); // schema version
        Assert.Equal("11092026", segments[1]); // user version (date)
        Assert.Equal("communityoutpost", segments[2]); // publisher
        Assert.Equal("gameclient", segments[3]); // content type
        Assert.Equal("community-patch-nonret", segments[4]); // content name
    }

    /// <summary>
    /// Verifies that DiscoverAsync generates the correct ID for a discovered Community Patch.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DiscoverAsync_GeneratesCorrectIdForCommunityPatchAsync()
    {
        // Arrange
        var mockHttp = new Mock<IHttpClientFactory>();
        var mockLoader = new Mock<IProviderDefinitionLoader>();
        var mockParserFactory = new Mock<ICatalogParserFactory>();
        var mockLogger = new Mock<ILogger<CommunityOutpostDiscoverer>>();

        var provider = new ProviderDefinition
        {
            ProviderId = CommunityOutpostConstants.PublisherId,
            PublisherType = "communityoutpost",
            DisplayName = "Community Outpost",
        };
        provider.Endpoints.CatalogUrl = "https://example.com/dl.dat";
        provider.Endpoints.Mirrors.Add(new MirrorEndpoint { Name = "Main", Priority = 1 });
        provider.Endpoints.Custom["patchPageUrl"] = "https://example.com/patch";

        var htmlContent = @"<a href=""https://legi.cc/patch/generalszh-2026-01-28.zip"">Download Latest</a>";
        var handler = new Mock<HttpMessageHandler>();
        handler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = System.Net.HttpStatusCode.OK,
                Content = new StringContent(htmlContent),
            });

        var client = new HttpClient(handler.Object);
        mockHttp.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);

        mockLoader.Setup(l => l.GetProvider(It.IsAny<string>())).Returns(provider);

        var discoverer = new CommunityOutpostDiscoverer(
            mockHttp.Object,
            mockLoader.Object,
            mockParserFactory.Object,
            mockLogger.Object);

        var query = new ContentSearchQuery { SearchTerm = "Community Patch" };

        // Act
        var result = await discoverer.DiscoverAsync(query);

        // Assert
        Assert.True(result.Success, $"Discovery failed: {result.FirstError}");
        Assert.NotEmpty(result.Data.Items);
        var patch = result.Data.Items.FirstOrDefault(i => i.Id.Contains("community-patch"));
        Assert.NotNull(patch);
        var idParts = patch.Id.Split('.');
        Assert.Equal(5, idParts.Length);
        Assert.Equal("1", idParts[0]);
        Assert.Equal("communityoutpost", idParts[2]);
        Assert.Equal("gameclient", idParts[3]);
        Assert.Equal("community-patch", idParts[4]);
    }

    /// <summary>
    /// Verifies that DiscoverAsync discovers both retail and non-retail community patches when both are hosted.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DiscoverAsync_DiscoversBothRetailAndNonRetailCommunityPatchesAsync()
    {
        // Arrange
        var mockHttp = new Mock<IHttpClientFactory>();
        var mockLoader = new Mock<IProviderDefinitionLoader>();
        var mockParserFactory = new Mock<ICatalogParserFactory>();
        var mockLogger = new Mock<ILogger<CommunityOutpostDiscoverer>>();

        var provider = new ProviderDefinition
        {
            ProviderId = CommunityOutpostConstants.PublisherId,
            PublisherType = "communityoutpost",
            DisplayName = "Community Outpost",
        };
        provider.Endpoints.CatalogUrl = "https://example.com/dl.dat";
        provider.Endpoints.Mirrors.Add(new MirrorEndpoint { Name = "Main", Priority = 1 });
        provider.Endpoints.Custom["patchPageUrl"] = "https://example.com/patch";

        var htmlContent = @"
            <html>
                <body>
                    <a href=""https://legi.cc/patch/generalszh_11-09-2026_NonRet.zip"">Stream NonRet Build</a>
                    <a href=""https://legi.cc/patch/generalszh_23-07-2026.zip"">Retail Compatible Build</a>
                </body>
            </html>";

        var handler = new Mock<HttpMessageHandler>();
        handler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = System.Net.HttpStatusCode.OK,
                Content = new StringContent(htmlContent),
            });

        var client = new HttpClient(handler.Object);
        mockHttp.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);
        mockLoader.Setup(l => l.GetProvider(It.IsAny<string>())).Returns(provider);

        var discoverer = new CommunityOutpostDiscoverer(
            mockHttp.Object,
            mockLoader.Object,
            mockParserFactory.Object,
            mockLogger.Object);

        var query = new ContentSearchQuery { SearchTerm = "Community Patch" };

        // Act
        var result = await discoverer.DiscoverAsync(query);

        // Assert
        Assert.True(result.Success, $"Discovery failed: {result.FirstError}");
        Assert.Equal(2, result.Data.Items.Count());

        var nonRetItem = result.Data.Items.FirstOrDefault(i => i.Id.EndsWith(CommunityOutpostConstants.CommunityPatchNonRetCode, StringComparison.Ordinal));
        Assert.NotNull(nonRetItem);
        Assert.Equal(CommunityOutpostConstants.CommunityPatchNonRetDisplayName, nonRetItem.Name);
        Assert.Contains(CommunityOutpostConstants.NonRetailTag, nonRetItem.Tags);
        Assert.Contains(CommunityOutpostConstants.StreamTag, nonRetItem.Tags);
        Assert.Equal("11092026", nonRetItem.Id.Split('.')[1]);

        var retailItem = result.Data.Items.FirstOrDefault(i => i.Id.EndsWith(CommunityOutpostConstants.CommunityPatchTag, StringComparison.Ordinal));
        Assert.NotNull(retailItem);
        Assert.Equal(CommunityOutpostConstants.CommunityPatchRetailDisplayName, retailItem.Name);
        Assert.Contains(CommunityOutpostConstants.RetailCompatibleTag, retailItem.Tags);
        Assert.DoesNotContain(CommunityOutpostConstants.NonRetailTag, retailItem.Tags);
        Assert.Equal("23072026", retailItem.Id.Split('.')[1]);
    }

    /// <summary>
    /// Verifies that GetTagsForCategory returns the centralized tags from CommunityOutpostConstants for all categories.
    /// </summary>
    /// <param name="category">The content category under test.</param>
    [Theory]
    [InlineData(GenPatcherContentCategory.CommunityPatch)]
    [InlineData(GenPatcherContentCategory.OfficialPatch)]
    [InlineData(GenPatcherContentCategory.BaseGame)]
    [InlineData(GenPatcherContentCategory.ControlBar)]
    [InlineData(GenPatcherContentCategory.Hotkeys)]
    [InlineData(GenPatcherContentCategory.Camera)]
    [InlineData(GenPatcherContentCategory.Tools)]
    [InlineData(GenPatcherContentCategory.Maps)]
    [InlineData(GenPatcherContentCategory.Visuals)]
    [InlineData(GenPatcherContentCategory.Prerequisites)]
    [InlineData(GenPatcherContentCategory.Other)]
    public void GetTagsForCategory_ReturnsExpectedConstantsTags(GenPatcherContentCategory category)
    {
        // Act
        var tags = CommunityOutpostDiscoverer.GetTagsForCategory(category);

        // Assert
        var expected = category switch
        {
            GenPatcherContentCategory.CommunityPatch => CommunityOutpostConstants.CommunityPatchTags,
            GenPatcherContentCategory.OfficialPatch => CommunityOutpostConstants.OfficialPatchTags,
            GenPatcherContentCategory.BaseGame => CommunityOutpostConstants.BaseGameTags,
            GenPatcherContentCategory.ControlBar => CommunityOutpostConstants.ControlBarTags,
            GenPatcherContentCategory.Hotkeys => CommunityOutpostConstants.HotkeysTags,
            GenPatcherContentCategory.Camera => CommunityOutpostConstants.CameraTags,
            GenPatcherContentCategory.Tools => CommunityOutpostConstants.ToolsTags,
            GenPatcherContentCategory.Maps => CommunityOutpostConstants.MapsTags,
            GenPatcherContentCategory.Visuals => CommunityOutpostConstants.VisualsTags,
            GenPatcherContentCategory.Prerequisites => CommunityOutpostConstants.PrerequisitesTags,
            _ => CommunityOutpostConstants.AddonTags,
        };

        Assert.Same(expected, tags);
    }

    /// <summary>
    /// Verifies that IsNonRetailBuild identifies non-retail builds when the indicator is present only in link text.
    /// </summary>
    /// <param name="filename">The filename or URL of the build.</param>
    /// <param name="linkText">The anchor link text.</param>
    /// <param name="expected">Expected non-retail classification.</param>
    [Theory]
    [InlineData("generalszh_23-07-2026.zip", "Download Stream Build", true)]
    [InlineData("generalszh_23-07-2026.zip", "NonRet Version", true)]
    [InlineData("generalszh_23-07-2026.zip", "Non-Retail Build", true)]
    [InlineData("generalszh_23-07-2026.zip", "Regular Zero Hour Build", false)]
    public void IsNonRetailBuild_EvaluatesFilenameAndLinkText(string filename, string linkText, bool expected)
    {
        var result = CommunityOutpostDiscoverer.IsNonRetailBuild(filename, linkText);
        Assert.Equal(expected, result);
    }

    /// <summary>
    /// Verifies that DiscoverAsync classifies a patch as non-retail when only the link text contains the indicator.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DiscoverAsync_ClassifiesNonRetailBasedOnLinkTextAsync()
    {
        var mockHttp = new Mock<IHttpClientFactory>();
        var mockLoader = new Mock<IProviderDefinitionLoader>();
        var mockParserFactory = new Mock<ICatalogParserFactory>();
        var mockLogger = new Mock<ILogger<CommunityOutpostDiscoverer>>();

        var provider = new ProviderDefinition
        {
            ProviderId = CommunityOutpostConstants.PublisherId,
            PublisherType = "communityoutpost",
            DisplayName = "Community Outpost",
        };
        provider.Endpoints.CatalogUrl = "https://example.com/dl.dat";
        provider.Endpoints.Mirrors.Add(new MirrorEndpoint { Name = "Main", Priority = 1 });
        provider.Endpoints.Custom["patchPageUrl"] = "https://example.com/patch";

        var htmlContent = @"
            <html>
                <body>
                    <a href=""https://legi.cc/patch/generalszh_11-09-2026.zip"">Stream Build</a>
                </body>
            </html>";

        var handler = new Mock<HttpMessageHandler>();
        handler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = System.Net.HttpStatusCode.OK,
                Content = new StringContent(htmlContent),
            });

        var client = new HttpClient(handler.Object);
        mockHttp.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);
        mockLoader.Setup(l => l.GetProvider(It.IsAny<string>())).Returns(provider);

        var discoverer = new CommunityOutpostDiscoverer(
            mockHttp.Object,
            mockLoader.Object,
            mockParserFactory.Object,
            mockLogger.Object);

        var query = new ContentSearchQuery { SearchTerm = "Community Patch" };

        var result = await discoverer.DiscoverAsync(query);

        Assert.True(result.Success, $"Discovery failed: {result.FirstError}");
        var item = Assert.Single(result.Data.Items);
        Assert.EndsWith(CommunityOutpostConstants.CommunityPatchNonRetCode, item.Id, StringComparison.Ordinal);
        Assert.Equal(CommunityOutpostConstants.CommunityPatchNonRetDisplayName, item.Name);
        Assert.Contains(CommunityOutpostConstants.NonRetailTag, item.Tags);
        Assert.Contains(CommunityOutpostConstants.StreamTag, item.Tags);
    }

    /// <summary>
    /// Verifies that ResolveDownloadUrl resolves relative URLs within the page directory whether or not the base URL ends with a slash.
    /// </summary>
    /// <param name="rawUrl">The raw URL found in href.</param>
    /// <param name="baseUrl">The base page URL.</param>
    /// <param name="expected">The expected resolved URL.</param>
    [Theory]
    [InlineData("generalszh_23-07-2026.zip", "https://legi.cc/patch", "https://legi.cc/patch/generalszh_23-07-2026.zip")]
    [InlineData("generalszh_23-07-2026.zip", "https://legi.cc/patch/", "https://legi.cc/patch/generalszh_23-07-2026.zip")]
    [InlineData("https://legi.cc/patch/generalszh_23-07-2026.zip", "https://legi.cc/patch", "https://legi.cc/patch/generalszh_23-07-2026.zip")]
    [InlineData("/patch/generalszh_23-07-2026.zip", "https://legi.cc/patch", "https://legi.cc/patch/generalszh_23-07-2026.zip")]
    [InlineData("generalszh_11-09-2026_NonRet.zip", "https://legi.cc/downloads/genpatcher/", "https://legi.cc/downloads/genpatcher/generalszh_11-09-2026_NonRet.zip")]
    public void ResolveDownloadUrl_ResolvesCorrectly(string rawUrl, string baseUrl, string expected)
    {
        var result = CommunityOutpostDiscoverer.ResolveDownloadUrl(rawUrl, baseUrl);
        Assert.Equal(expected, result);
    }

    /// <summary>
    /// Verifies that DiscoverAsync correctly resolves relative URLs to the patch directory even when patchPageUrl lacks a trailing slash.
    /// Regression test for download failure where relative links resolved to domain root instead of patch directory.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DiscoverAsync_WithRelativeUrlsAndNoTrailingSlashBaseUrl_ResolvesToCorrectDirectoryAsync()
    {
        var mockHttp = new Mock<IHttpClientFactory>();
        var mockLoader = new Mock<IProviderDefinitionLoader>();
        var mockParserFactory = new Mock<ICatalogParserFactory>();
        var mockLogger = new Mock<ILogger<CommunityOutpostDiscoverer>>();

        var provider = new ProviderDefinition
        {
            ProviderId = CommunityOutpostConstants.PublisherId,
            PublisherType = "communityoutpost",
            DisplayName = "Community Outpost",
        };
        provider.Endpoints.CatalogUrl = "https://example.com/dl.dat";
        provider.Endpoints.Mirrors.Add(new MirrorEndpoint { Name = "Main", Priority = 1 });
        provider.Endpoints.Custom["patchPageUrl"] = "https://legi.cc/patch"; // No trailing slash

        var htmlContent = @"
            <html>
                <body>
                    <a href=""generalszh_11-09-2026_NonRet.zip"">Stream NonRet Build</a>
                    <a href=""generalszh_23-07-2026.zip"">Retail Compatible Build</a>
                </body>
            </html>";

        var handler = new Mock<HttpMessageHandler>();
        handler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = System.Net.HttpStatusCode.OK,
                Content = new StringContent(htmlContent),
            });

        var client = new HttpClient(handler.Object);
        mockHttp.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);
        mockLoader.Setup(l => l.GetProvider(It.IsAny<string>())).Returns(provider);

        var discoverer = new CommunityOutpostDiscoverer(
            mockHttp.Object,
            mockLoader.Object,
            mockParserFactory.Object,
            mockLogger.Object);

        var query = new ContentSearchQuery { SearchTerm = "Community Patch" };

        // Act
        var result = await discoverer.DiscoverAsync(query);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, result.Data.Items.Count());

        var retailItem = result.Data.Items.First(i => i.Id.EndsWith(CommunityOutpostConstants.CommunityPatchTag, StringComparison.Ordinal));
        Assert.Equal("https://legi.cc/patch/generalszh_23-07-2026.zip", retailItem.SourceUrl);
        Assert.Equal("https://legi.cc/patch/generalszh_23-07-2026.zip", retailItem.ResolverMetadata["downloadUrl"]);

        var nonRetItem = result.Data.Items.First(i => i.Id.EndsWith(CommunityOutpostConstants.CommunityPatchNonRetCode, StringComparison.Ordinal));
        Assert.Equal("https://legi.cc/patch/generalszh_11-09-2026_NonRet.zip", nonRetItem.SourceUrl);
        Assert.Equal("https://legi.cc/patch/generalszh_11-09-2026_NonRet.zip", nonRetItem.ResolverMetadata["downloadUrl"]);
    }
}
