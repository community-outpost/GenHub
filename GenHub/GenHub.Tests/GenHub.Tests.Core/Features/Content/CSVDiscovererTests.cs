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
using System.Net;
using System.Net.Http;
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
using ContentType = GenHub.Core.Models.Enums.ContentType;

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
        using var indexFile = CreateIndexFile(CreateEntry("https://example.com/generals.csv", "Generals", "1.08", "EN"));
        var discoverer = CreateDiscoverer(new CsvCatalogConfiguration { IndexFilePath = indexFile.FilePath });

        var result = await discoverer.DiscoverAsync(new ContentSearchQuery { TargetGame = GameType.Generals });

        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.Items.Should().ContainSingle();
        result.Data.Items.First().ResolverMetadata[CsvConstants.CsvUrlMetadataKey].Should().Be("https://example.com/generals.csv");
    }

    /// <summary>
    /// Verifies that <see cref="CSVDiscoverer.DiscoverAsync"/> returns an empty result when the content type is not GameInstallation.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DiscoverAsync_WithNonGameInstallationContentType_ReturnsEmptyResult()
    {
        var discoverer = CreateDiscoverer(new CsvCatalogConfiguration());

        var result = await discoverer.DiscoverAsync(new ContentSearchQuery { ContentType = ContentType.Map });

        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.Items.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that <see cref="CSVDiscoverer.DiscoverAsync"/> matches language filters case-insensitively.
    /// </summary>
    /// <param name="filterLanguage">The language filter to apply to the query.</param>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Theory]
    [InlineData("de")]
    [InlineData("DE")]
    public async Task DiscoverAsync_WithLanguageFilter_MatchesCaseInsensitively(string filterLanguage)
    {
        using var indexFile = CreateIndexFile(CreateEntry("https://example.com/de.csv", "Generals", "1.0", "DE"));
        var discoverer = CreateDiscoverer(new CsvCatalogConfiguration { IndexFilePath = indexFile.FilePath });

        var result = await discoverer.DiscoverAsync(new ContentSearchQuery { Language = filterLanguage });

        result.Data!.Items.Should().ContainSingle();
        result.Data.Items.First().ResolverMetadata[CsvConstants.LanguageMetadataKey].Should().Be("DE");
    }

    /// <summary>
    /// Verifies that <see cref="CSVDiscoverer.DiscoverAsync"/> returns all languages when using the all languages filter.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DiscoverAsync_WithAllLanguagesFilter_ReturnsAllLanguages()
    {
        using var indexFile = CreateIndexFile(CreateEntry("https://example.com/multi.csv", "Generals", "1.0", "en", "de"));
        var discoverer = CreateDiscoverer(new CsvCatalogConfiguration { IndexFilePath = indexFile.FilePath });

        var result = await discoverer.DiscoverAsync(new ContentSearchQuery { Language = CsvConstants.AllLanguagesFilter });

        result.Data!.Items.Should().HaveCount(2);
    }

    /// <summary>
    /// Verifies that <see cref="CSVDiscoverer.DiscoverAsync"/> preserves the canonical All language token.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DiscoverAsync_WithUnifiedCatalog_PreservesCanonicalAllLanguage()
    {
        using var indexFile = CreateIndexFile(CreateEntry("https://example.com/unified.csv", "ZeroHour", "1.04", "All"));
        var discoverer = CreateDiscoverer(new CsvCatalogConfiguration { IndexFilePath = indexFile.FilePath });

        var result = await discoverer.DiscoverAsync(new ContentSearchQuery());

        result.Data!.Items.Should().ContainSingle();
        result.Data.Items.First().ResolverMetadata[CsvConstants.LanguageMetadataKey].Should().Be(CsvConstants.AllLanguagesFilter);
        result.Data.Items.First().Name.Should().Contain("(All)");
    }

    /// <summary>
    /// Verifies that <see cref="CSVDiscoverer.DiscoverAsync"/> returns one item per language when multiple languages are in the catalog.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DiscoverAsync_MultipleLanguagesInCatalog_ReturnsOneItemPerLanguage()
    {
        using var indexFile = CreateIndexFile(CreateEntry("https://example.com/multi.csv", "Generals", "1.0", "EN", "DE", "FR"));
        var discoverer = CreateDiscoverer(new CsvCatalogConfiguration { IndexFilePath = indexFile.FilePath });

        var result = await discoverer.DiscoverAsync(new ContentSearchQuery());

        result.Data!.Items.Should().HaveCount(3);
        result.Data.Items.Should().Contain(i => i.ResolverMetadata[CsvConstants.LanguageMetadataKey] == "EN");
        result.Data.Items.Should().Contain(i => i.ResolverMetadata[CsvConstants.LanguageMetadataKey] == "DE");
        result.Data.Items.Should().Contain(i => i.ResolverMetadata[CsvConstants.LanguageMetadataKey] == "FR");
    }

    /// <summary>
    /// Verifies that <see cref="CSVDiscoverer.DiscoverAsync"/> does not fail when the configuration service returns null.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DiscoverAsync_WhenConfigurationServiceReturnsNull_DoesNotFail()
    {
        var discoverer = CreateDiscoverer(null);

        var result = await discoverer.DiscoverAsync(new ContentSearchQuery());

        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
    }

    /// <summary>
    /// Verifies that <see cref="CSVDiscoverer.DiscoverAsync"/> respects cancellation tokens.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DiscoverAsync_WithCanceledToken_RespectsCancellation()
    {
        var discoverer = CreateDiscoverer(new CsvCatalogConfiguration());
        var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            discoverer.DiscoverAsync(new ContentSearchQuery(), cts.Token));
    }

    /// <summary>
    /// Verifies that <see cref="CSVDiscoverer.DiscoverAsync"/> returns an empty result when the index file cannot be loaded.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DiscoverAsync_WhenIndexFileIsMissing_ReturnsEmptyResult()
    {
        var discoverer = CreateDiscoverer(new CsvCatalogConfiguration { IndexFilePath = "non-existent-index.json" });

        var result = await discoverer.DiscoverAsync(new ContentSearchQuery());

        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.Items.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that <see cref="CSVDiscoverer.DiscoverAsync"/> prefers the default GitHub index URL before configured index sources.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DiscoverAsync_WhenDefaultGitHubIndexHasEntries_UsesItBeforeConfiguredIndex()
    {
        var configuredUrl = "https://example.com/custom-index.json";
        var githubJson = JsonSerializer.Serialize(new CsvCatalogRegistryIndex
        {
            Entries =
            [
                CreateEntry("https://index.com/github.csv", "Generals", "1.0", "EN"),
            ],
        });
        var configuredJson = JsonSerializer.Serialize(new CsvCatalogRegistryIndex
        {
            Entries =
            [
                CreateEntry("https://index.com/configured.csv", "Generals", "1.0", "EN"),
            ],
        });

        var discoverer = CreateDiscoverer(
            new CsvCatalogConfiguration { IndexFilePath = configuredUrl },
            new MultiResponseHttpMessageHandler(
                new Dictionary<string, HttpResponseMessage>
                {
                    [CsvConstants.DefaultIndexFileUrl] = new(HttpStatusCode.OK)
                    {
                        Content = new StringContent(githubJson),
                    },
                    [configuredUrl] = new(HttpStatusCode.OK)
                    {
                        Content = new StringContent(configuredJson),
                    },
                }));

        var result = await discoverer.DiscoverAsync(new ContentSearchQuery());

        result.Data!.Items.Should().ContainSingle();
        result.Data.Items.First().ResolverMetadata[CsvConstants.CsvUrlMetadataKey].Should().Be("https://index.com/github.csv");
    }

    /// <summary>
    /// Verifies that <see cref="CSVDiscoverer.DiscoverAsync"/> uses entries from the configured index file path.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DiscoverAsync_WithIndexFilePath_UsesIndexFileEntries()
    {
        using var indexFile = CreateIndexFile(CreateEntry("https://index.com/file.csv", "Generals", "1.0", "EN"));
        var discoverer = CreateDiscoverer(new CsvCatalogConfiguration { IndexFilePath = indexFile.FilePath });

        var result = await discoverer.DiscoverAsync(new ContentSearchQuery());

        result.Data!.Items.Should().ContainSingle();
        result.Data.Items.First().ResolverMetadata[CsvConstants.CsvUrlMetadataKey].Should().Be("https://index.com/file.csv");
    }

    /// <summary>
    /// Verifies that <see cref="CSVDiscoverer.DiscoverAsync"/> uses entries from a remote index URL.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DiscoverAsync_WithRemoteIndexUrl_UsesRemoteEntries()
    {
        var remoteIndexUrl = "https://example.com/index.json";
        var remoteIndexJson = JsonSerializer.Serialize(new CsvCatalogRegistryIndex
        {
            Entries =
            [
                CreateEntry("https://index.com/remote.csv", "Generals", "1.0", "EN"),
            ],
        });

        var discoverer = CreateDiscoverer(
            new CsvCatalogConfiguration { IndexFilePath = remoteIndexUrl },
            new StubHttpMessageHandler(remoteIndexUrl, remoteIndexJson, HttpStatusCode.OK));

        var result = await discoverer.DiscoverAsync(new ContentSearchQuery());

        result.Data!.Items.Should().ContainSingle();
        result.Data.Items.First().ResolverMetadata[CsvConstants.CsvUrlMetadataKey].Should().Be("https://index.com/remote.csv");
    }

    /// <summary>
    /// Verifies that <see cref="CSVDiscoverer.DiscoverAsync"/> falls back to configured catalogs after index sources return no entries.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DiscoverAsync_WhenIndexSourcesHaveNoEntries_UsesConfiguredCatalogs()
    {
        var discoverer = CreateDiscoverer(new CsvCatalogConfiguration
        {
            IndexFilePath = "non-existent-index.json",
            CsvValidationCatalogs =
            [
                new CsvCatalogRegistryEntry
                {
                    Url = "https://config.com/fallback.csv",
                    GameType = "Generals",
                    Version = "1.08",
                    SupportedLanguages = ["EN"],
                },
            ],
        });

        var result = await discoverer.DiscoverAsync(new ContentSearchQuery());

        result.Data!.Items.Should().ContainSingle();
        result.Data.Items.First().ResolverMetadata[CsvConstants.CsvUrlMetadataKey].Should().Be("https://config.com/fallback.csv");
    }

    /// <summary>
    /// Verifies that <see cref="CSVDiscoverer.DiscoverAsync"/> returns no items for unsupported target games.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DiscoverAsync_WithUnsupportedTargetGame_ReturnsEmpty()
    {
        using var indexFile = CreateIndexFile(CreateEntry("https://example.com/generals.csv", "Generals", "1.08", "EN"));
        var discoverer = CreateDiscoverer(new CsvCatalogConfiguration { IndexFilePath = indexFile.FilePath });

        var result = await discoverer.DiscoverAsync(new ContentSearchQuery { TargetGame = (GameType)999 });

        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.Items.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that <see cref="CSVDiscoverer.DiscoverAsync"/> handles Zero Hour game type correctly.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DiscoverAsync_WithZeroHourQuery_ReturnsValidResult()
    {
        using var indexFile = CreateIndexFile(CreateEntry("https://example.com/zerohour.csv", "ZeroHour", "1.04", "EN"));
        var discoverer = CreateDiscoverer(new CsvCatalogConfiguration { IndexFilePath = indexFile.FilePath });

        var result = await discoverer.DiscoverAsync(new ContentSearchQuery { TargetGame = GameType.ZeroHour });

        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.Items.Should().ContainSingle();
        result.Data.Items.First().TargetGame.Should().Be(GameType.ZeroHour);
    }

    /// <summary>
    /// Verifies that <see cref="CSVDiscoverer.DiscoverAsync"/> returns empty when no matching game type is found.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DiscoverAsync_WithNonMatchingGameType_ReturnsEmpty()
    {
        using var indexFile = CreateIndexFile(CreateEntry("https://example.com/generals.csv", "Generals", "1.08", "EN"));
        var discoverer = CreateDiscoverer(new CsvCatalogConfiguration { IndexFilePath = indexFile.FilePath });

        var result = await discoverer.DiscoverAsync(new ContentSearchQuery { TargetGame = GameType.ZeroHour });

        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.Items.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that <see cref="CSVDiscoverer.SourceName"/> returns the correct source name.
    /// </summary>
    [Fact]
    public void SourceName_ReturnsExpectedValue()
    {
        var discoverer = CreateDiscoverer(new CsvCatalogConfiguration());

        discoverer.SourceName.Should().Be(CsvConstants.SourceName);
    }

    /// <summary>
    /// Verifies that <see cref="CSVDiscoverer.Description"/> returns the correct description.
    /// </summary>
    [Fact]
    public void Description_ReturnsExpectedValue()
    {
        var discoverer = CreateDiscoverer(new CsvCatalogConfiguration());

        discoverer.Description.Should().Be(CsvConstants.Description);
    }

    /// <summary>
    /// Verifies that <see cref="CSVDiscoverer.IsEnabled"/> returns true.
    /// </summary>
    [Fact]
    public void IsEnabled_ReturnsTrue()
    {
        var discoverer = CreateDiscoverer(new CsvCatalogConfiguration());

        discoverer.IsEnabled.Should().BeTrue();
    }

    /// <summary>
    /// Verifies that <see cref="CSVDiscoverer.Capabilities"/> returns DirectSearch.
    /// </summary>
    [Fact]
    public void Capabilities_ReturnsDirectSearch()
    {
        var discoverer = CreateDiscoverer(new CsvCatalogConfiguration());

        discoverer.Capabilities.Should().Be(ContentSourceCapabilities.DirectSearch);
    }

    private static CSVDiscoverer CreateDiscoverer(CsvCatalogConfiguration? config, HttpMessageHandler? httpMessageHandler = null)
    {
        var mockConfig = new Mock<IConfigurationProviderService>();
        mockConfig.Setup(o => o.GetCsvCatalogConfiguration()).Returns(config!);
        var mockHttpClientFactory = new Mock<IHttpClientFactory>();
        mockHttpClientFactory
            .Setup(o => o.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient(httpMessageHandler ?? new StubHttpMessageHandler()));

        return new CSVDiscoverer(Mock.Of<ILogger<CSVDiscoverer>>(), mockConfig.Object, mockHttpClientFactory.Object);
    }

    private static TempIndexFile CreateIndexFile(params CsvCatalogRegistryEntry[] entries)
    {
        return new TempIndexFile(entries);
    }

    private static CsvCatalogRegistryEntry CreateEntry(string url, string gameType, string version, params string[] languages)
    {
        return new CsvCatalogRegistryEntry
        {
            Url = url,
            GameType = gameType,
            Version = version,
            SupportedLanguages = languages.ToList(),
            IsActive = true,
        };
    }

    private sealed class TempIndexFile : IDisposable
    {
        public TempIndexFile(IEnumerable<CsvCatalogRegistryEntry> entries)
        {
            FilePath = Path.GetTempFileName();
            var index = new CsvCatalogRegistryIndex
            {
                Entries = entries.ToList(),
            };

            File.WriteAllText(FilePath, JsonSerializer.Serialize(index));
        }

        public string FilePath { get; }

        public void Dispose()
        {
            if (File.Exists(FilePath))
            {
                File.Delete(FilePath);
            }
        }
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly string? _expectedUrl;
        private readonly string _content;
        private readonly HttpStatusCode _statusCode;

        public StubHttpMessageHandler(
            string? expectedUrl = null,
            string content = "",
            HttpStatusCode statusCode = HttpStatusCode.NotFound)
        {
            _expectedUrl = expectedUrl;
            _content = content;
            _statusCode = statusCode;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var statusCode = _expectedUrl == null || request.RequestUri?.AbsoluteUri == _expectedUrl
                ? _statusCode
                : HttpStatusCode.NotFound;
            var content = statusCode == HttpStatusCode.OK ? _content : string.Empty;
            var response = new HttpResponseMessage(statusCode)
            {
                RequestMessage = request,
                Content = new StringContent(content),
            };

            return Task.FromResult(response);
        }
    }

    private sealed class MultiResponseHttpMessageHandler(IDictionary<string, HttpResponseMessage> responses) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var requestUrl = request.RequestUri?.AbsoluteUri ?? string.Empty;
            if (!responses.TryGetValue(requestUrl, out var response))
            {
                response = new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }
}
