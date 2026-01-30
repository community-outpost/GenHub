using System.Text.Json;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.Providers;
using GenHub.Features.Content.Services.GeneralsOnline;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GenHub.Tests.Core.Features.Content.Services.GeneralsOnline;

/// <summary>
/// Tests for <see cref="GeneralsOnlineJsonCatalogParser"/>.
/// </summary>
public class GeneralsOnlineJsonCatalogParserTests
{
    private readonly GeneralsOnlineJsonCatalogParser _parser;
    private readonly ProviderDefinition _provider;

    /// <summary>
    /// Initializes a new instance of the <see cref="GeneralsOnlineJsonCatalogParserTests"/> class.
    /// </summary>
    public GeneralsOnlineJsonCatalogParserTests()
    {
        _parser = new GeneralsOnlineJsonCatalogParser(NullLogger<GeneralsOnlineJsonCatalogParser>.Instance);

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
    /// Tests that ParseAsync correctly parses PascalCase JSON (wrapper properties must be lowercase for parser, inner data can be PascalCase).
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task ParseAsync_WithPascalCaseJson_ParsesCorrectly()
    {
        // Arrange
        // Parser creates a JsonDocument and uses TryGetProperty("source"). This is CASE SENSITIVE.
        // So we must use "source" and "data".
        // The *inner* data deserialization expects snake_case fields.
        var wrapper = new
        {
            source = "manifest",
            data = new
            {
                version = "1.0",
                download_url = "url1",
                release_notes = "notes1",
                size = 100,
            },
        };
        var jsonString = JsonSerializer.Serialize(wrapper);

        // Act
        var result = await _parser.ParseAsync(jsonString, _provider);

        // Assert
        Assert.NotNull(result);
        if (!result.Success)
        {
             // Fail with helpful message
             Assert.Fail($"Parser failed: {result.FirstError}");
        }

        Assert.NotNull(result.Data);
        var item = Assert.Single(result.Data);
        Assert.Equal("GeneralsOnline_1.0", item.Id);
        Assert.Equal("url1", item.DownloadUrl);
        Assert.Equal("notes1", item.ReleaseNotes);
        Assert.Equal("Generals Online", item.Name);
    }

    /// <summary>
    /// Tests that ParseAsync correctly parses snake_case JSON.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task ParseAsync_WithSnakeCaseJson_ParsesCorrectly()
    {
        // Arrange
        var wrapper = new
        {
            source = "manifest",
            data = new
            {
                version = "1.0",
                download_url = "url1",
                release_notes = "notes1",
                size = 100,
            },
        };
        var jsonString = JsonSerializer.Serialize(wrapper);

        // Act
        var result = await _parser.ParseAsync(jsonString, _provider);

        // Assert
        Assert.NotNull(result);
        if (!result.Success) Assert.Fail($"Parser failed: {result.FirstError}");

        Assert.NotNull(result.Data);
        var item = Assert.Single(result.Data);
        Assert.Equal("GeneralsOnline_1.0", item.Id);
        Assert.Equal("url1", item.DownloadUrl);
        Assert.Equal("notes1", item.ReleaseNotes);
        Assert.Equal("Generals Online", item.Name);
    }
}
