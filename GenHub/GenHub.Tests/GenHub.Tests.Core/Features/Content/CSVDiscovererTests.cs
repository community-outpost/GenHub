// Unit tests for CSVDiscoverer. the whole file is made by AI
// Copyright (C) 2026  GenHub & The Super Hackers
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.Content.Services.ContentDiscoverers;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace GenHub.Tests.Core.Features.Content;

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

    /// <summary>
    /// Verifies that <see cref="CSVDiscoverer.DiscoverAsync"/> matches language filters case-insensitively.
    /// </summary>
    /// <param name="filterLanguage">The language filter to test.</param>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Theory]
    [InlineData("de")]
    [InlineData("DE")]
    public async Task DiscoverAsync_WithLanguageFilter_MatchesCaseInsensitively(string filterLanguage)
    {
        // Arrange
        var config = new CsvCatalogConfiguration
        {
            CsvValidationCatalogs = new List<CsvValidationCatalog>
            {
                new CsvValidationCatalog
                {
                    Url = "https://example.com/de.csv",
                    GameType = "Generals",
                    Version = "1.0",
                    SupportedLanguages = new List<string> { "de" },
                },
            },
        };

        var mockConfig = new Mock<IConfigurationProviderService>();
        mockConfig.Setup(o => o.GetCsvCatalogConfiguration()).Returns(config);

        var discoverer = new CSVDiscoverer(Mock.Of<ILogger<CSVDiscoverer>>(), mockConfig.Object);
        var query = new ContentSearchQuery { Language = filterLanguage };

        // Act
        var result = await discoverer.DiscoverAsync(query);

        // Assert
        result.Data!.Items.Should().NotBeEmpty();
        result.Data.Items.First().ResolverMetadata["language"].Should().Be("de");
    }

    /// <summary>
    /// Verifies that <see cref="CSVDiscoverer.DiscoverAsync"/> returns all languages when using the all languages filter.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DiscoverAsync_WithAllLanguagesFilter_ReturnsAllLanguages()
    {
        // Arrange
        var config = new CsvCatalogConfiguration
        {
            CsvValidationCatalogs = new List<CsvValidationCatalog>
            {
                new CsvValidationCatalog
                {
                    Url = "https://example.com/multi.csv",
                    GameType = "Generals",
                    Version = "1.0",
                    SupportedLanguages = new List<string> { "en", "de" },
                },
            },
        };

        var mockConfig = new Mock<IConfigurationProviderService>();
        mockConfig.Setup(o => o.GetCsvCatalogConfiguration()).Returns(config);

        var discoverer = new CSVDiscoverer(Mock.Of<ILogger<CSVDiscoverer>>(), mockConfig.Object);
        var query = new ContentSearchQuery { Language = CsvConstants.AllLanguagesFilter };

        // Act
        var result = await discoverer.DiscoverAsync(query);

        // Assert
        result.Data!.Items.Should().HaveCount(2);
    }

    /// <summary>
    /// Verifies that <see cref="CSVDiscoverer.DiscoverAsync"/> returns one item per language when multiple languages are in the catalog.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DiscoverAsync_MultipleLanguagesInCatalog_ReturnsOneItemPerLanguage()
    {
        // Arrange
        var config = new CsvCatalogConfiguration
        {
            CsvValidationCatalogs = new List<CsvValidationCatalog>
            {
                new CsvValidationCatalog
                {
                    Url = "https://example.com/multi.csv",
                    GameType = "Generals",
                    Version = "1.0",
                    SupportedLanguages = new List<string> { "en", "de", "fr" },
                },
            },
        };

        var mockConfig = new Mock<IConfigurationProviderService>();
        mockConfig.Setup(o => o.GetCsvCatalogConfiguration()).Returns(config);

        var discoverer = new CSVDiscoverer(Mock.Of<ILogger<CSVDiscoverer>>(), mockConfig.Object);

        // Act
        var result = await discoverer.DiscoverAsync(new ContentSearchQuery());

        // Assert
        result.Data!.Items.Should().HaveCount(3);
        result.Data.Items.Should().Contain(i => i.ResolverMetadata["language"] == "en");
        result.Data.Items.Should().Contain(i => i.ResolverMetadata["language"] == "de");
        result.Data.Items.Should().Contain(i => i.ResolverMetadata["language"] == "fr");
    }

    /// <summary>
    /// Verifies that <see cref="CSVDiscoverer.DiscoverAsync"/> returns a graceful failure when the configuration service returns null.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DiscoverAsync_WhenConfigurationServiceReturnsNull_ReturnsGracefulFailure()
    {
        // Arrange
        var mockConfig = new Mock<IConfigurationProviderService>();
        mockConfig.Setup(o => o.GetCsvCatalogConfiguration()).Returns((CsvCatalogConfiguration)null!);

        var discoverer = new CSVDiscoverer(
            Mock.Of<ILogger<CSVDiscoverer>>(),
            mockConfig.Object);

        var query = new ContentSearchQuery();

        // Act
        var result = await discoverer.DiscoverAsync(query);

        // Assert
        result.Success.Should().BeFalse();
        result.FirstError.Should().Contain("Discovery failed");
    }

    /// <summary>
    /// Verifies that <see cref="CSVDiscoverer.DiscoverAsync"/> respects cancellation tokens.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DiscoverAsync_WithCanceledToken_RespectsCancellation()
    {
        // Arrange
        var mockConfig = new Mock<IConfigurationProviderService>();
        mockConfig.Setup(o => o.GetCsvCatalogConfiguration()).Returns(new CsvCatalogConfiguration());

        var discoverer = new CSVDiscoverer(
            Mock.Of<ILogger<CSVDiscoverer>>(),
            mockConfig.Object);

        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            discoverer.DiscoverAsync(new ContentSearchQuery(), cts.Token));
    }

    /// <summary>
    /// Verifies that <see cref="CSVDiscoverer.DiscoverAsync"/> uses the index file path over configuration when both are provided.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DiscoverAsync_WithIndexFilePath_UsesIndexFileOverConfiguration()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        try
        {
            var indexData = new CsvCatalogRegistryIndex
            {
                Entries = new List<CsvCatalogRegistryEntry>
                {
                    new CsvCatalogRegistryEntry
                    {
                        Url = "https://index.com/file.csv",
                        GameType = "Generals",
                        Version = "1.0",
                        SupportedLanguages = new List<string> { "en" },
                    },
                },
            };
            File.WriteAllText(tempFile, JsonSerializer.Serialize(indexData));

            var config = new CsvCatalogConfiguration
            {
                IndexFilePath = tempFile,
                CsvValidationCatalogs = new List<CsvValidationCatalog>
                {
                    new CsvValidationCatalog { Url = "https://config.com/file.csv" },
                },
            };

            var mockConfig = new Mock<IConfigurationProviderService>();
            mockConfig.Setup(o => o.GetCsvCatalogConfiguration()).Returns(config);

            var discoverer = new CSVDiscoverer(Mock.Of<ILogger<CSVDiscoverer>>(), mockConfig.Object);

            // Act
            var result = await discoverer.DiscoverAsync(new ContentSearchQuery());

            // Assert
            result.Data!.Items.Should().ContainSingle();
            result.Data.Items.First().ResolverMetadata["csvUrl"].Should().Be("https://index.com/file.csv");
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }
}
