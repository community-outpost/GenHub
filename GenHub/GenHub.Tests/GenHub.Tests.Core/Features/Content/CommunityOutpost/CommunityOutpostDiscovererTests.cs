using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Results;
using GenHub.Features.Content.Services.CommunityOutpost;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
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
    /// Verifies that DiscoverAsync generates the correct ID for a discovered Community Patch.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DiscoverAsync_GeneratesCorrectIdForCommunityPatch()
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
        provider.Endpoints.CatalogUrl = "http://example.com/dl.dat";
        provider.Endpoints.Mirrors.Add(new MirrorEndpoint { Name = "Main", Priority = 1 });
        provider.Endpoints.Custom["patchPageUrl"] = "http://example.com/patch";

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
}
