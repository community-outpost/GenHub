using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.Content.Services.ContentDiscoverers;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace GenHub.Tests.Core.Features.Content;

// the whole file is made by AI

/// <summary>
/// Unit tests for <see cref="CSVDiscoverer"/>.
/// </summary>
public class CSVDiscovererTests
{
    /// <summary>
    /// Verifies that <see cref="CSVDiscoverer.DiscoverAsync"/> returns a valid result when querying for Generals.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DiscoverAsync_WithGeneralsQuery_ReturnsValidResult()
    {
        // Arrange
        var config = new CsvCatalogConfiguration
        {
            IndexFilePath = "non-existent-index.json",
            CsvValidationCatalogs = new List<CsvValidationCatalog>
            {
                new CsvValidationCatalog
                {
                    Url = "https://example.com/generals.csv",
                    GameType = "Generals",
                    Version = "1.08",
                    SupportedLanguages = new List<string> { "en" },
                    FileCount = 100,
                },
            },
        };

        var mockConfig = new Mock<IConfigurationProviderService>();
        mockConfig.Setup(o => o.GetCsvCatalogConfiguration()).Returns(config);

        var discoverer = new CSVDiscoverer(
            Mock.Of<ILogger<CSVDiscoverer>>(),
            mockConfig.Object);

        var query = new ContentSearchQuery { TargetGame = GameType.Generals };

        // Act
        var result = await discoverer.DiscoverAsync(query);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.Items.Should().NotBeEmpty();
        result.Data.Items.First().ResolverMetadata.Should().ContainKey("csvUrl");
        result.Data.Items.First().ResolverMetadata["csvUrl"].Should().Be("https://example.com/generals.csv");
    }

    /// <summary>
    /// Verifies that <see cref="CSVDiscoverer.DiscoverAsync"/> returns an empty result when the content type is not GameInstallation.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DiscoverAsync_WithNonGameInstallationContentType_ReturnsEmptyResult()
    {
        // Arrange
        var mockConfig = new Mock<IConfigurationProviderService>();
        mockConfig.Setup(o => o.GetCsvCatalogConfiguration()).Returns(new CsvCatalogConfiguration());

        var discoverer = new CSVDiscoverer(
            Mock.Of<ILogger<CSVDiscoverer>>(),
            mockConfig.Object);

        var query = new ContentSearchQuery { ContentType = GenHub.Core.Models.Enums.ContentType.Map };

        // Act
        var result = await discoverer.DiscoverAsync(query);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.Items.Should().BeEmpty();
    }

}

