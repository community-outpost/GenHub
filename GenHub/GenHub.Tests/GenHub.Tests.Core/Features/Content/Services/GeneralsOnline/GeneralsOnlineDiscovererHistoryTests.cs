using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Interfaces.Tools.ReplayManager;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Tools.ReplayManager;
using GenHub.Features.Content.Services.GeneralsOnline;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Tests.Core.Features.Content.Services.GeneralsOnline;

/// <summary>
/// Tests for history handling in <see cref="GeneralsOnlineDiscoverer"/>.
/// </summary>
public class GeneralsOnlineDiscovererHistoryTests
{
    private sealed class StubHandler(string response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response),
            });
        }
    }

    private const string LatestVersion = "082826_QFE1";
    private const string HistoryVersion = "081326";

    /// <summary>
    /// Latest-only queries exclude archived CRC releases so setup flows acquire one version.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DiscoverAsync_WithoutOlderVersions_ExcludesHistoryAsync()
    {
        var discoverer = CreateDiscoverer(LatestVersion, [CreateEntry(HistoryVersion, "https://cdn.playgenerals.online/GeneralsOnline_portable_081326.zip")]);

        var result = await discoverer.DiscoverAsync(new ContentSearchQuery());

        Assert.True(result.Success);
        var versions = result.Data!.Items.Select(item => item.Version).ToList();
        Assert.DoesNotContain(HistoryVersion, versions);
    }

    /// <summary>
    /// Opt-in queries append archived CRC releases after the CDN latest.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DiscoverAsync_WithOlderVersions_AppendsHistoryAsync()
    {
        var discoverer = CreateDiscoverer(LatestVersion, [CreateEntry(HistoryVersion, "https://cdn.playgenerals.online/GeneralsOnline_portable_081326.zip")]);

        var result = await discoverer.DiscoverAsync(new ContentSearchQuery { IncludeOlderVersions = true });

        Assert.True(result.Success);
        var versions = result.Data!.Items.Select(item => item.Version).ToList();
        Assert.Contains(LatestVersion, versions);
        Assert.Contains(HistoryVersion, versions);
    }

    /// <summary>
    /// A search term that filters out the current CDN card still excludes its history twin.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DiscoverAsync_WithSearchTermFilteringCurrentCard_StillExcludesHistoryTwinAsync()
    {
        var discoverer = CreateDiscoverer(
            "042826_QFE2",
            [
                CreateEntry("042826_QFE2_EAC", "https://cdn.playgenerals.online/GeneralsOnline_portable_042826_QFE2_EAC.zip"),
            ]);

        var result = await discoverer.DiscoverAsync(new ContentSearchQuery { SearchTerm = "EAC", IncludeOlderVersions = true });

        Assert.True(result.Success);
        Assert.Empty(result.Data!.Items);
    }

    private static GeneralsOnlineDiscoverer CreateDiscoverer(string latestVersion, IReadOnlyList<CrcMappingEntry> historyEntries)
    {
        var catalogJson = $"{{\"Version\":\"{latestVersion}\",\"Download_Url\":\"https://cdn.playgenerals.online/GeneralsOnline_portable_{latestVersion}.zip\",\"Size\":42,\"Release_Notes\":\"Latest\"}}";

        var providerLoader = new Mock<IProviderDefinitionLoader>();
        providerLoader.Setup(loader => loader.GetProvider(GeneralsOnlineConstants.PublisherType)).Returns(new ProviderDefinition
        {
            PublisherType = GeneralsOnlineConstants.PublisherType,
            CatalogFormat = "generalsonline",
            Endpoints = new ProviderEndpoints
            {
                CatalogUrl = "https://cdn.playgenerals.online/catalog.json",
            },
        });

        var parserFactory = new Mock<ICatalogParserFactory>();
        parserFactory.Setup(factory => factory.GetParser(It.IsAny<string>()))
            .Returns(new GeneralsOnlineJsonCatalogParser(NullLogger<GeneralsOnlineJsonCatalogParser>.Instance));

        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory.Setup(factory => factory.CreateClient(It.IsAny<string>()))
            .Returns(new HttpClient(new StubHandler(catalogJson)));

        var registry = new Mock<ICrcMappingRegistry>();
        registry.Setup(r => r.GetAllEntries()).Returns(historyEntries);

        return new GeneralsOnlineDiscoverer(
            NullLogger<GeneralsOnlineDiscoverer>.Instance,
            providerLoader.Object,
            parserFactory.Object,
            httpClientFactory.Object,
            null,
            registry.Object);
    }

    private static CrcMappingEntry CreateEntry(string version, string cdnUrl)
    {
        return new CrcMappingEntry
        {
            ExeCrc = "0x2",
            IniCrc = "0x1",
            Publisher = "generalsonline",
            GameType = "ZeroHour",
            Version = version,
            BuildDate = "2026-08-28",
            Description = $"GeneralsOnline {version} portable release",
            ManifestId = "1.828261.generalsonline.gameclient.zerohour",
            CdnUrl = cdnUrl,
        };
    }
}
