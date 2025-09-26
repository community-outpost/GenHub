using GenHub.Core.Constants;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Features.Content.Services.ContentResolvers;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using System.Net;
namespace GenHub.Tests.Core.Features.Content;

/// <summary>
/// Unit tests for <see cref="CSVResolver"/>.
/// </summary>
public class CSVResolverTests
{
    private readonly Mock<HttpMessageHandler> _httpMessageHandlerMock;
    private readonly HttpClient _httpClient;
    private readonly Mock<ILogger<CSVResolver>> _loggerMock;
    private readonly CSVResolver _resolver;

    /// <summary>
    /// Initializes a new instance of the <see cref="CSVResolverTests"/> class.
    /// </summary>
    public CSVResolverTests()
    {
        _httpMessageHandlerMock = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_httpMessageHandlerMock.Object);
        _loggerMock = new Mock<ILogger<CSVResolver>>();
        _resolver = new CSVResolver(_httpClient, _loggerMock.Object);
    }

    /// <summary>
    /// Verifies that ResolveAsync returns null discovered item failure.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task ResolveAsync_WithNullDiscoveredItem_ReturnsFailure()
    {
        // Act
        var result = await _resolver.ResolveAsync(null!);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("cannot be null", result.FirstError);
    }

    /// <summary>
    /// Verifies that ResolveAsync returns failure when CSV URL is missing.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task ResolveAsync_WithMissingCsvUrl_ReturnsFailure()
    {
        // Arrange
        var discoveredItem = new ContentSearchResult
        {
            Id = "test-id",
            Name = "Test Content",
        };

        // Act
        var result = await _resolver.ResolveAsync(discoveredItem);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(CsvConstants.CsvUrlNotProvidedError, result.FirstError);
    }

    /// <summary>
    /// Verifies that ResolveAsync successfully resolves CSV with Generals filtering.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task ResolveAsync_WithGeneralsGame_FiltersCorrectly()
    {
        // Arrange
        const string csvContent = @"relativePath,size,md5,sha256,gameType,language,isRequired,metadata
Data/INI/GameData.ini,12345,abc123,def456,Generals,All,true,""{""category"":""config""}""
Data/Lang/English/game.str,67890,f45123,a67890,Generals,EN,true,""{""category"":""language""}""
Data/INI/ZeroHour.ini,11111,zh111,zh222,ZeroHour,All,true,""{""category"":""config""}""";

        SetupHttpResponse(csvContent);

        var discoveredItem = new ContentSearchResult
        {
            Id = "Generals-1.08-All",
            Name = "Command & Conquer Generals 1.08 (All)",
            TargetGame = GameType.Generals,
        };
        discoveredItem.ResolverMetadata["csvUrl"] = "https://example.com/test.csv";
        discoveredItem.ResolverMetadata["game"] = "Generals";
        discoveredItem.ResolverMetadata["version"] = "1.08";
        discoveredItem.ResolverMetadata["language"] = "All";

        // Act
        var result = await _resolver.ResolveAsync(discoveredItem);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal(2, result.Data.Files.Count); // Should include Generals files, exclude ZeroHour
        Assert.Contains(result.Data.Files, f => f.RelativePath == "Data/INI/GameData.ini");
        Assert.Contains(result.Data.Files, f => f.RelativePath == "Data/Lang/English/game.str");
        Assert.DoesNotContain(result.Data.Files, f => f.RelativePath == "Data/INI/ZeroHour.ini");
    }

    /// <summary>
    /// Verifies that ResolveAsync successfully resolves CSV with language filtering.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task ResolveAsync_WithLanguageFilter_FiltersCorrectly()
    {
        // Arrange
        const string csvContent = @"relativePath,size,md5,sha256,gameType,language,isRequired,metadata
Data/INI/GameData.ini,12345,abc123,def456,Generals,All,true,""{""category"":""config""}""
Data/Lang/English/game.str,67890,f45123,a67890,Generals,EN,true,""{""category"":""language""}""
Data/Lang/German/game.str,78901,g45123,g67890,Generals,DE,true,""{""category"":""language""}""";

        SetupHttpResponse(csvContent);

        var discoveredItem = new ContentSearchResult
        {
            Id = "Generals-1.08-EN",
            Name = "Command & Conquer Generals 1.08 (EN)",
            TargetGame = GameType.Generals,
        };
        discoveredItem.ResolverMetadata["csvUrl"] = "https://example.com/test.csv";
        discoveredItem.ResolverMetadata["game"] = "Generals";
        discoveredItem.ResolverMetadata["version"] = "1.08";
        discoveredItem.ResolverMetadata["language"] = "EN";

        // Act
        var result = await _resolver.ResolveAsync(discoveredItem);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal(2, result.Data.Files.Count); // Should include All and EN files, exclude DE
        Assert.Contains(result.Data.Files, f => f.RelativePath == "Data/INI/GameData.ini"); // All
        Assert.Contains(result.Data.Files, f => f.RelativePath == "Data/Lang/English/game.str"); // EN
        Assert.DoesNotContain(result.Data.Files, f => f.RelativePath == "Data/Lang/German/game.str"); // DE excluded
    }

    /// <summary>
    /// Verifies that ResolverId returns the correct value.
    /// </summary>
    [Fact]
    public void ResolverId_ReturnsCorrectValue()
    {
        // Assert
        Assert.Equal(CsvConstants.CsvResolverId, _resolver.ResolverId);
    }

    private void SetupHttpResponse(string csvContent)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(csvContent),
        };

        _httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);
    }
}
