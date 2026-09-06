using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.GeneralsOnline;
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
    public async Task ParseAsync_WithPascalCaseJson_ParsesCorrectlyAsync()
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
    public async Task ParseAsync_WithCamelCaseJson_ParsesCorrectlyAsync()
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

    /// <summary>
    /// Tests that ParseAsync extracts the QFE version from download URL when API version lacks QFE.
    /// This prevents reconciler mismatch where latest.txt reports QFE but manifest.json version omitted it.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task ParseAsync_WithVersionLackingQfeAndDownloadUrlContainingQfe_ResolvesUrlVersionWithQfeAsync()
    {
        // Arrange
        var json = @"{
            ""version"": ""082826"",
            ""download_url"": ""https://cdn.playgenerals.online/GeneralsOnline_portable_082826_QFE1.zip"",
            ""size"": 30108752,
            ""sha256"": ""A3BBF2676F9B49B5EFD61345F4AE2BA562838DC7B273B2844873B1D4B9B60E3B"",
            ""release_notes"": ""www.playgenerals.online""
        }";

        var wrapper = $"{{\"source\":\"manifest\",\"data\":{json}}}";

        // Act
        var result = await _parser.ParseAsync(wrapper, _provider);

        // Assert
        Assert.True(result.Success);
        var item = result.Data.First();
        Assert.Equal("082826_QFE1", item.Version);
        var release = item.GetData<GeneralsOnlineRelease>();
        Assert.NotNull(release);
        Assert.Equal("082826_QFE1", release.Version);
        Assert.Equal("A3BBF2676F9B49B5EFD61345F4AE2BA562838DC7B273B2844873B1D4B9B60E3B", release.Sha256);
    }

    /// <summary>
    /// Tests that ResolveReleaseVersion correctly resolves canonical versions.
    /// </summary>
    /// <param name="apiVersion">The version in the API response.</param>
    /// <param name="downloadUrl">The download URL.</param>
    /// <param name="expected">The expected resolved version.</param>
    [Theory]
    [InlineData("082826", "https://cdn.playgenerals.online/GeneralsOnline_portable_082826_QFE1.zip", "082826_QFE1")]
    [InlineData("082826_QFE1", "https://cdn.playgenerals.online/GeneralsOnline_portable_082826_QFE1.zip", "082826_QFE1")]
    [InlineData("082826_QFE2", "https://cdn.playgenerals.online/GeneralsOnline_portable_082826_QFE1.zip", "082826_QFE2")]
    [InlineData("082826_QFE1", "https://cdn.playgenerals.online/GeneralsOnline_portable_082826_QFE2.zip", "082826_QFE2")]
    [InlineData("082826", "https://cdn.playgenerals.online/GeneralsOnline_portable_082826.zip", "082826")]
    [InlineData(null, "https://cdn.playgenerals.online/GeneralsOnline_portable_082826_QFE1.zip", "082826_QFE1")]
    [InlineData("082826_QFE1", null, "082826_QFE1")]
    public void ResolveReleaseVersion_WithVariousInputs_ResolvesExpectedVersion(
        string? apiVersion,
        string? downloadUrl,
        string expected)
    {
        var actual = GeneralsOnlineJsonCatalogParser.ResolveReleaseVersion(apiVersion, downloadUrl);
        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// Tests that ExtractVersionFromUrl extracts valid version substrings from candidate URLs.
    /// </summary>
    /// <param name="url">The download URL.</param>
    /// <param name="expected">The expected version substring.</param>
    [Theory]
    [InlineData("https://cdn.playgenerals.online/GeneralsOnline_portable_082826_QFE1.zip", "082826_QFE1")]
    [InlineData("https://cdn.playgenerals.online/GeneralsOnline_portable_042826_QFE3_EAC.zip?nocache=123", "042826_QFE3_EAC")]
    [InlineData("/GeneralsOnline_portable_082826_QFE1.zip", "082826_QFE1")]
    [InlineData("GeneralsOnline_portable_082826_QFE1.zip", "082826_QFE1")]
    [InlineData("https://cdn.playgenerals.online/other_file.zip", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void ExtractVersionFromUrl_WithVariousUrls_ExtractsExpected(string? url, string? expected)
    {
        var actual = GeneralsOnlineJsonCatalogParser.ExtractVersionFromUrl(url);
        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// Tests that ParseAsync correctly populates the SHA256 hash when present in the API response.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task ParseAsync_WithSha256_PopulatesSha256OnReleaseAsync()
    {
        // Arrange
        const string expectedSha256 = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
        var json = $@"{{
            ""version"": ""111825_QFE2"",
            ""download_url"": ""https://example.com/download.zip"",
            ""size"": 123456,
            ""sha256"": ""{expectedSha256}"",
            ""release_notes"": ""Fixes stuff""
        }}";

        var wrapper = $"{{\"source\":\"manifest\",\"data\":{json}}}";

        // Act
        var result = await _parser.ParseAsync(wrapper, _provider);

        // Assert
        Assert.True(result.Success);
        var item = result.Data.First();
        var release = item.GetData<GeneralsOnlineRelease>();
        Assert.NotNull(release);
        Assert.Equal(expectedSha256, release.Sha256);
    }
}
