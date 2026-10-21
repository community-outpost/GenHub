using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Providers;
using GenHub.Features.Content.Services.GeneralsOnline;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Tests.Core.Features.Content.Services.GeneralsOnline;

/// <summary>
/// Tests that Generals Online is not offered on hosts its provider definition does not list.
/// </summary>
public class GeneralsOnlinePlatformTests
{
    private const string LatestVersion = "082826_QFE1";

    /// <summary>
    /// Gets a platform that is not the current host, so tests behave the same on every CI runner.
    /// </summary>
    internal static ExecutablePlatform OtherPlatform =>
        HostPlatform.Current == ExecutablePlatform.Windows ? ExecutablePlatform.MacOS : ExecutablePlatform.Windows;

    /// <summary>
    /// Discovery returns nothing and never contacts the CDN when the host is not supported.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DiscoverAsync_OnUnsupportedHost_ReturnsNoReleasesWithoutFetchingAsync()
    {
        var handler = new CountingHandler();
        var discoverer = CreateDiscoverer([OtherPlatform], handler);

        var result = await discoverer.DiscoverAsync(new ContentSearchQuery());

        Assert.True(result.Success);
        Assert.Empty(result.Data!.Items);
        Assert.Equal(0, handler.Requests);
        Assert.False(discoverer.IsEnabled);
    }

    /// <summary>
    /// Discovery still offers the release on a supported host.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DiscoverAsync_OnSupportedHost_ReturnsLatestReleaseAsync()
    {
        var handler = new CountingHandler();
        var discoverer = CreateDiscoverer([HostPlatform.Current], handler);

        var result = await discoverer.DiscoverAsync(new ContentSearchQuery());

        Assert.True(result.Success);
        Assert.Contains(result.Data!.Items, item => item.Version == LatestVersion);
        Assert.True(discoverer.IsEnabled);
    }

    /// <summary>
    /// The provider is disabled on an unsupported host, so orchestrated search skips it.
    /// </summary>
    /// <param name="hostSupported">Whether the definition lists the current host.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Provider_IsEnabled_FollowsDefinitionPlatforms(bool hostSupported)
    {
        var loader = CreateLoader([hostSupported ? HostPlatform.Current : OtherPlatform]);
        var provider = new GeneralsOnlineProvider(
            loader.Object,
            [],
            [],
            [],
            Mock.Of<IContentValidator>(),
            Mock.Of<IInstallationInstructionsService>(),
            Mock.Of<IContentManifestPool>(),
            NullLogger<GeneralsOnlineProvider>.Instance);

        Assert.Equal(hostSupported, provider.IsEnabled);
    }

    /// <summary>
    /// A definition with <c>enabled</c> set to false disables the provider and the discoverer,
    /// even on a supported host.
    /// </summary>
    [Fact]
    public void IsEnabled_WithDisabledDefinitionOnSupportedHost_ReturnsFalse()
    {
        var loader = CreateLoader([HostPlatform.Current], enabled: false);
        var provider = new GeneralsOnlineProvider(
            loader.Object,
            [],
            [],
            [],
            Mock.Of<IContentValidator>(),
            Mock.Of<IInstallationInstructionsService>(),
            Mock.Of<IContentManifestPool>(),
            NullLogger<GeneralsOnlineProvider>.Instance);
        var discoverer = new GeneralsOnlineDiscoverer(
            NullLogger<GeneralsOnlineDiscoverer>.Instance,
            loader.Object,
            Mock.Of<ICatalogParserFactory>(),
            Mock.Of<IHttpClientFactory>());

        Assert.False(provider.IsEnabled);
        Assert.False(discoverer.IsEnabled);
    }

    /// <summary>
    /// Creates a discoverer whose provider definition lists <paramref name="platforms"/>.
    /// </summary>
    /// <param name="platforms">The supported platforms to declare.</param>
    /// <param name="handler">The HTTP handler serving the catalog.</param>
    /// <returns>The discoverer.</returns>
    internal static GeneralsOnlineDiscoverer CreateDiscoverer(List<ExecutablePlatform> platforms, HttpMessageHandler? handler = null)
    {
        handler ??= new CountingHandler();
        var parserFactory = new Mock<ICatalogParserFactory>();
        parserFactory.Setup(factory => factory.GetParser(It.IsAny<string>()))
            .Returns(new GeneralsOnlineJsonCatalogParser(NullLogger<GeneralsOnlineJsonCatalogParser>.Instance));

        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory.Setup(factory => factory.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient(handler, disposeHandler: false));

        return new GeneralsOnlineDiscoverer(
            NullLogger<GeneralsOnlineDiscoverer>.Instance,
            CreateLoader(platforms).Object,
            parserFactory.Object,
            httpClientFactory.Object);
    }

    private static Mock<IProviderDefinitionLoader> CreateLoader(List<ExecutablePlatform> platforms, bool enabled = true)
    {
        var loader = new Mock<IProviderDefinitionLoader>();
        loader.Setup(l => l.GetProvider(GeneralsOnlineConstants.PublisherType)).Returns(new ProviderDefinition
        {
            PublisherType = GeneralsOnlineConstants.PublisherType,
            CatalogFormat = "generalsonline",
            Endpoints = new ProviderEndpoints { CatalogUrl = "https://cdn.playgenerals.online/catalog.json" },
            SupportedPlatforms = platforms,
            Enabled = enabled,
        });
        return loader;
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            var json = $"{{\"Version\":\"{LatestVersion}\",\"Download_Url\":\"https://cdn.playgenerals.online/GeneralsOnline_portable_{LatestVersion}.zip\",\"Size\":42,\"Release_Notes\":\"Latest\"}}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
        }
    }
}
