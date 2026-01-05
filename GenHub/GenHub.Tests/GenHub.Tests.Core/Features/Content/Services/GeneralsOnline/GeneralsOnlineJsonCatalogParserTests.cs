using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.Providers;
using GenHub.Features.Content.Services.GeneralsOnline;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace GenHub.Tests.Core.Features.Content.Services.GeneralsOnline;

/// <summary>
/// Tests for <see cref="GeneralsOnlineJsonCatalogParser"/>.
/// </summary>
public class GeneralsOnlineJsonCatalogParserTests
{
    private readonly GeneralsOnlineJsonCatalogParser _parser;
    private readonly Mock<IProviderDefinitionLoader> _providerLoaderMock;
    private readonly ProviderDefinition _provider;

    /// <summary>
    /// Initializes a new instance of the <see cref="GeneralsOnlineJsonCatalogParserTests"/> class.
    /// </summary>
    public GeneralsOnlineJsonCatalogParserTests()
    {
        _parser = new GeneralsOnlineJsonCatalogParser(NullLogger<GeneralsOnlineJsonCatalogParser>.Instance);
        _providerLoaderMock = new Mock<IProviderDefinitionLoader>();

        _provider = new ProviderDefinition
        {
            PublisherType = GeneralsOnlineConstants.PublisherType,
            Endpoints = new ProviderEndpoints
            {
                Custom = new Dictionary<string, string>
                {
                    { "releasesUrl", "https://cdn.playgenerals.online/releases" },
                    { "downloadPageUrl", "https://www.playgenerals.online/download" },
                    { "iconUrl", "https://www.playgenerals.online/logo.png" },
                },
            },
        };
    }

    /// <summary>
    /// Tests that ParseAsync correctly parses PascalCase JSON.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task ParseAsync_WithPascalCaseJson_ParsesCorrectly()
    {
        // Arrange
        var json = @"{
            ""Version"": ""111825_QFE2"",
            ""Download_Url"": ""https://example.com/download.zip"",
            ""Size"": 123456,
            ""Release_Notes"": ""Fixes stuff""
        }";

        var wrapper = $"{{\"source\":\"manifest\",\"data\":{json}}}";

        // Act
        var result = await _parser.ParseAsync(wrapper, _provider);

        // Assert
        Assert.True(result.Success);
        var item = result.Data.First();
        Assert.Equal("111825_QFE2", item.Version);
    }

    /// <summary>
    /// Tests that ParseAsync correctly parses camelCase JSON.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task ParseAsync_WithCamelCaseJson_ParsesCorrectly()
    {
        // Arrange
        // Standard lowercase/camelCase that matches exact property names if attributes weren't there
        var json = @"{
            ""version"": ""111825_QFE2"",
            ""download_url"": ""https://example.com/download.zip"",
            ""size"": 123456,
            ""release_notes"": ""Fixes stuff""
        }";

        var wrapper = $"{{\"source\":\"manifest\",\"data\":{json}}}";

        // Act
        var result = await _parser.ParseAsync(wrapper, _provider);

        // Assert
        Assert.True(result.Success);
        var item = result.Data.First();
        Assert.Equal("111825_QFE2", item.Version);
    }
}
