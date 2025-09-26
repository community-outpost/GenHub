using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Features.Content.Services.ContentDiscoverers;
using Microsoft.Extensions.Logging;
using Moq;

namespace GenHub.Tests.Core.Features.Content;

/// <summary>
/// Unit tests for <see cref="CSVDiscoverer"/>.
/// </summary>
public class CSVDiscovererTests
{
    private readonly Mock<ILogger<CSVDiscoverer>> _loggerMock;
    private readonly Mock<IConfigurationProviderService> _configurationProviderMock;
    private readonly HttpClient _httpClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="CSVDiscovererTests"/> class.
    /// </summary>
    public CSVDiscovererTests()
    {
        _loggerMock = new Mock<ILogger<CSVDiscoverer>>();
        _configurationProviderMock = new Mock<IConfigurationProviderService>();
        _httpClient = new HttpClient();
    }

    /// <summary>
    /// Verifies that DiscoverAsync returns Generals results when TargetGame is Generals.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task DiscoverAsync_WithGeneralsQuery_ReturnsGeneralsResult()
    {
        // Arrange
        var discoverer = new CSVDiscoverer(
            _loggerMock.Object,
            _configurationProviderMock.Object,
            _httpClient);

        var query = new ContentSearchQuery { TargetGame = GameType.Generals };

        // Act
        var result = await discoverer.DiscoverAsync(query);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Single(result.Data);
        var searchResult = result.Data.First();
        Assert.Equal("Generals-1.08-All", searchResult.Id);
        Assert.Equal("Command & Conquer Generals 1.08 (All)", searchResult.Name);
        Assert.Equal(CsvConstants.CsvResolverId, searchResult.ResolverId);
        Assert.Equal("Generals", searchResult.ResolverMetadata["game"]);
        Assert.Equal("1.08", searchResult.ResolverMetadata["version"]);
        Assert.Equal("All", searchResult.ResolverMetadata["language"]);
        Assert.Equal(CsvConstants.DefaultGeneralsCsvUrl, searchResult.ResolverMetadata["csvUrl"]);
    }

    /// <summary>
    /// Verifies that DiscoverAsync returns Zero Hour results when TargetGame is ZeroHour.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task DiscoverAsync_WithZeroHourQuery_ReturnsZeroHourResult()
    {
        // Arrange
        var discoverer = new CSVDiscoverer(
            _loggerMock.Object,
            _configurationProviderMock.Object,
            _httpClient);

        var query = new ContentSearchQuery { TargetGame = GameType.ZeroHour };

        // Act
        var result = await discoverer.DiscoverAsync(query);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Single(result.Data);
        var searchResult = result.Data.First();
        Assert.Equal("ZeroHour-1.04-All", searchResult.Id);
        Assert.Equal("Command & Conquer Generals: Zero Hour 1.04 (All)", searchResult.Name);
        Assert.Equal(CsvConstants.CsvResolverId, searchResult.ResolverId);
        Assert.Equal("ZeroHour", searchResult.ResolverMetadata["game"]);
        Assert.Equal("1.04", searchResult.ResolverMetadata["version"]);
        Assert.Equal("All", searchResult.ResolverMetadata["language"]);
        Assert.Equal(CsvConstants.DefaultZeroHourCsvUrl, searchResult.ResolverMetadata["csvUrl"]);
    }

    /// <summary>
    /// Verifies that DiscoverAsync normalizes language to uppercase.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task DiscoverAsync_WithLowercaseLanguage_NormalizesToUppercase()
    {
        // Arrange
        var discoverer = new CSVDiscoverer(
            _loggerMock.Object,
            _configurationProviderMock.Object,
            _httpClient);

        var query = new ContentSearchQuery
        {
            TargetGame = GameType.Generals,
            Language = "en",
        };

        // Act
        var result = await discoverer.DiscoverAsync(query);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        var searchResult = result.Data.First();
        Assert.Equal("Generals-1.08-EN", searchResult.Id);
        Assert.Equal("Command & Conquer Generals 1.08 (EN)", searchResult.Name);
        Assert.Equal("EN", searchResult.ResolverMetadata["language"]);
    }

    /// <summary>
    /// Verifies that DiscoverAsync uses "All" as default when language is null.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task DiscoverAsync_WithNullLanguage_UsesAll()
    {
        // Arrange
        var discoverer = new CSVDiscoverer(
            _loggerMock.Object,
            _configurationProviderMock.Object,
            _httpClient);

        var query = new ContentSearchQuery
        {
            TargetGame = GameType.Generals,
            Language = null,
        };

        // Act
        var result = await discoverer.DiscoverAsync(query);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        var searchResult = result.Data.First();
        Assert.Equal("All", searchResult.ResolverMetadata["language"]);
    }

    /// <summary>
    /// Verifies that SourceName returns the correct value.
    /// </summary>
    [Fact]
    public void SourceName_ReturnsCorrectValue()
    {
        // Arrange
        var discoverer = new CSVDiscoverer(
            _loggerMock.Object,
            _configurationProviderMock.Object,
            _httpClient);

        // Assert
        Assert.Equal(CsvConstants.CsvSourceName, discoverer.SourceName);
    }

    /// <summary>
    /// Verifies that ResolverId returns the correct value.
    /// </summary>
    [Fact]
    public void ResolverId_ReturnsCorrectValue()
    {
        // Assert
        Assert.Equal(CsvConstants.CsvResolverId, CSVDiscoverer.ResolverId);
    }
}
