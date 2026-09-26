using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Parsers;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.Content.Services.GenLauncher;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Content.GenLauncher;

/// <summary>
/// Unit tests for <see cref="GenLauncherDiscoverer"/>.
/// </summary>
public sealed class GenLauncherDiscovererTests
{
    private const string SampleRootManifest = @"
LauncherVersion: '1.0'
modDatas:
  - ModName: 'Shockwave'
    ModLink: 'https://raw.githubusercontent.com/test/shockwave.yaml'
    ModPatches:
      - 'https://raw.githubusercontent.com/test/shockwave-patch1.yaml'
    ModAddons: []
globalAddonsData:
  - 'https://raw.githubusercontent.com/test/global-addon.yaml'
";

    private const string SampleShockwaveYaml = @"
Name: 'Shockwave'
Version: '1.2'
ModificationType: 0
UIImageSourceLink: 'https://test.com/img.png'
NewsLink: 'https://test.com/news'
DiscordLink: 'https://discord.gg/test'
ModDBLink: 'https://moddb.com/test'
SupportLink: 'https://support.com'
SimpleDownloadLink: 'https://dropbox.com/s/test/mod.zip?dl=0'
";

    private const string SampleShockwavePatchYaml = @"
Name: 'Shockwave 1.2 Patch 1'
Version: '1.2.1'
ModificationType: 2
DependenceName: 'Shockwave'
SimpleDownloadLink: 'https://dropbox.com/s/test/patch.zip?dl=0'
";

    private const string SampleGlobalAddonYaml = @"
Name: 'Camera Height Addon'
Version: '1.0'
ModificationType: 1
SimpleDownloadLink: 'https://dropbox.com/s/test/camera.zip?dl=0'
";

    /// <summary>
    /// Tests that DiscoverAsync fetches and parses GenLauncher catalogs with variants.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task DiscoverAsync_FetchesAndParsesCatalogsWithVariants()
    {
        var mockHttp = new Mock<HttpMessageHandler>();

        // Root manifests
        mockHttp.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => r.RequestUri!.ToString().Contains("Generals")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("LauncherVersion: '1.0'\nmodDatas: []"),
            });

        mockHttp.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => r.RequestUri!.ToString().Contains("ZH")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(SampleRootManifest),
            });

        // Child manifests
        mockHttp.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => r.RequestUri!.ToString().Contains("shockwave.yaml")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(SampleShockwaveYaml),
            });

        mockHttp.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => r.RequestUri!.ToString().Contains("shockwave-patch1.yaml")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(SampleShockwavePatchYaml),
            });

        mockHttp.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => r.RequestUri!.ToString().Contains("global-addon.yaml")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(SampleGlobalAddonYaml),
            });

        var client = new HttpClient(mockHttp.Object);
        var mockFactory = new Mock<IHttpClientFactory>();
        mockFactory.Setup(f => f.CreateClient(PublisherTypeConstants.GenLauncher)).Returns(client);

        var mockLoader = new Mock<IProviderDefinitionLoader>();
        var providerDef = new ProviderDefinition
        {
            ProviderId = GenLauncherConstants.PublisherId,
            DisplayName = PublisherTypeConstants.GenLauncher,
            PublisherType = PublisherTypeConstants.GenLauncher,
        };
        mockLoader.Setup(l => l.GetProvider(It.IsAny<string>())).Returns(providerDef);

        var parser = new GenLauncherCatalogParser(NullLogger<GenLauncherCatalogParser>.Instance);
        var discoverer = new GenLauncherDiscoverer(
            mockFactory.Object,
            mockLoader.Object,
            parser,
            NullLogger<GenLauncherDiscoverer>.Instance);

        var query = new ContentSearchQuery
        {
            SearchTerm = string.Empty,
            TargetGame = GameType.ZeroHour,
            ProviderName = PublisherTypeConstants.GenLauncher,
            ContentType = null,
        };

        var result = await discoverer.DiscoverAsync(query, CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);

        var items = new List<ContentSearchResult>(result.Data.Items);
        Assert.Equal(3, items.Count); // 1 main mod, 1 patch, 1 global addon

        var mainMod = items.Find(i => i.Name == "Shockwave");
        Assert.NotNull(mainMod);
        Assert.NotNull(mainMod.VariantGroupId);
        Assert.NotNull(mainMod.Variants);
        Assert.Equal(2, mainMod.Variants.Count); // Main mod + patch

        var patchItem = items.Find(i => i.Name == "Shockwave 1.2 Patch 1");
        Assert.NotNull(patchItem);
        Assert.Equal("https://test.com/img.png", patchItem.IconUrl);

        var globalAddon = items.Find(i => i.Name == "Camera Height Addon");
        Assert.NotNull(globalAddon);
        Assert.Equal(ContentType.Addon, globalAddon.ContentType);
    }

    /// <summary>
    /// Tests that DiscoverAsync returns all catalog items without server-side truncation.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task DiscoverAsync_ReturnsAllCatalogItemsWithoutTruncation()
    {
        var mockHttp = new Mock<HttpMessageHandler>();

        mockHttp.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => r.RequestUri!.ToString().Contains("Generals")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("LauncherVersion: '1.0'\nmodDatas: []"),
            });

        mockHttp.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => r.RequestUri!.ToString().Contains("ZH")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(SampleRootManifest),
            });

        mockHttp.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => r.RequestUri!.ToString().Contains("shockwave.yaml")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(SampleShockwaveYaml),
            });

        mockHttp.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => r.RequestUri!.ToString().Contains("shockwave-patch1.yaml")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(SampleShockwavePatchYaml),
            });

        mockHttp.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => r.RequestUri!.ToString().Contains("global-addon.yaml")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(SampleGlobalAddonYaml),
            });

        var client = new HttpClient(mockHttp.Object);
        var mockFactory = new Mock<IHttpClientFactory>();
        mockFactory.Setup(f => f.CreateClient(PublisherTypeConstants.GenLauncher)).Returns(client);

        var mockLoader = new Mock<IProviderDefinitionLoader>();
        mockLoader.Setup(l => l.GetProvider(It.IsAny<string>())).Returns(new ProviderDefinition
        {
            ProviderId = GenLauncherConstants.PublisherId,
            DisplayName = PublisherTypeConstants.GenLauncher,
            PublisherType = PublisherTypeConstants.GenLauncher,
        });

        var parser = new GenLauncherCatalogParser(NullLogger<GenLauncherCatalogParser>.Instance);
        var discoverer = new GenLauncherDiscoverer(
            mockFactory.Object,
            mockLoader.Object,
            parser,
            NullLogger<GenLauncherDiscoverer>.Instance);

        var query = new ContentSearchQuery
        {
            TargetGame = GameType.ZeroHour,
            Skip = 0,
            Take = 1,
        };

        var result = await discoverer.DiscoverAsync(query, CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal(3, result.Data.TotalItems);
        Assert.Equal(3, result.Data.Items.Count());
        Assert.False(result.Data.HasMoreItems);
    }

    /// <summary>
    /// Tests that child manifests with dead Discord links inherit the parent mod icon.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task DiscoverAsync_ChildManifestWithDiscordIcon_InheritsParentIcon()
    {
        const string childWithDiscordIconYaml = @"
Name: 'Shockwave 1.2 Patch 1'
Version: '1.2.1'
ModificationType: 2
UIImageSourceLink: 'https://cdn.discordapp.com/attachments/123/456/broken.png'
DependenceName: 'Shockwave'
SimpleDownloadLink: 'https://dropbox.com/s/test/patch.zip?dl=0'
";

        var mockHttp = new Mock<HttpMessageHandler>();

        mockHttp.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => r.RequestUri!.ToString().Contains("Generals")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("LauncherVersion: '1.0'\nmodDatas: []"),
            });

        mockHttp.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => r.RequestUri!.ToString().Contains("ZH")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(SampleRootManifest),
            });

        mockHttp.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => r.RequestUri!.ToString().Contains("shockwave.yaml")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(SampleShockwaveYaml),
            });

        mockHttp.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => r.RequestUri!.ToString().Contains("shockwave-patch1.yaml")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(childWithDiscordIconYaml),
            });

        mockHttp.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => r.RequestUri!.ToString().Contains("global-addon.yaml")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(SampleGlobalAddonYaml),
            });

        var client = new HttpClient(mockHttp.Object);
        var mockFactory = new Mock<IHttpClientFactory>();
        mockFactory.Setup(f => f.CreateClient(PublisherTypeConstants.GenLauncher)).Returns(client);

        var mockLoader = new Mock<IProviderDefinitionLoader>();
        mockLoader.Setup(l => l.GetProvider(It.IsAny<string>())).Returns(new ProviderDefinition
        {
            ProviderId = GenLauncherConstants.PublisherId,
            DisplayName = PublisherTypeConstants.GenLauncher,
            PublisherType = PublisherTypeConstants.GenLauncher,
        });

        var parser = new GenLauncherCatalogParser(NullLogger<GenLauncherCatalogParser>.Instance);
        var discoverer = new GenLauncherDiscoverer(
            mockFactory.Object,
            mockLoader.Object,
            parser,
            NullLogger<GenLauncherDiscoverer>.Instance);

        var query = new ContentSearchQuery
        {
            TargetGame = GameType.ZeroHour,
        };

        var result = await discoverer.DiscoverAsync(query, CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);

        var items = new List<ContentSearchResult>(result.Data.Items);
        var patchItem = items.Find(i => i.Name == "Shockwave 1.2 Patch 1");
        Assert.NotNull(patchItem);
        Assert.Equal("https://test.com/img.png", patchItem.IconUrl);
    }

    /// <summary>
    /// Tests that DiscoverAsync rejects unsafe, loopback, or private URLs during discovery.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task DiscoverAsync_WithUnsafeUrls_RejectsAndSkipsUnsafeRequests()
    {
        const string unsafeManifest = @"
LauncherVersion: '1.0'
modDatas:
  - ModName: 'UnsafeMod'
    ModLink: 'http://169.254.169.254/latest/meta-data/'
    ModPatches:
      - 'http://127.0.0.1:8080/patch.yaml'
      - 'http://localhost:5000/internal.yaml'
    ModAddons: []
globalAddonsData:
  - 'http://192.168.1.100/addon.yaml'
";

        var mockHttp = new Mock<HttpMessageHandler>();
        mockHttp.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => r.RequestUri!.ToString().Contains("Generals")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("LauncherVersion: '1.0'\nmodDatas: []"),
            });

        mockHttp.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => r.RequestUri!.ToString().Contains("ZH")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(unsafeManifest),
            });

        var client = new HttpClient(mockHttp.Object);
        var mockFactory = new Mock<IHttpClientFactory>();
        mockFactory.Setup(f => f.CreateClient(PublisherTypeConstants.GenLauncher)).Returns(client);

        var mockLoader = new Mock<IProviderDefinitionLoader>();
        mockLoader.Setup(l => l.GetProvider(It.IsAny<string>())).Returns(new ProviderDefinition
        {
            ProviderId = GenLauncherConstants.PublisherId,
            DisplayName = PublisherTypeConstants.GenLauncher,
            PublisherType = PublisherTypeConstants.GenLauncher,
        });

        var parser = new GenLauncherCatalogParser(NullLogger<GenLauncherCatalogParser>.Instance);
        var discoverer = new GenLauncherDiscoverer(
            mockFactory.Object,
            mockLoader.Object,
            parser,
            NullLogger<GenLauncherDiscoverer>.Instance);

        var query = new ContentSearchQuery
        {
            TargetGame = GameType.ZeroHour,
        };

        var result = await discoverer.DiscoverAsync(query, CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Empty(result.Data.Items);

        // Verify none of the unsafe URLs were ever called via HttpClient
        mockHttp.Protected().Verify(
            "SendAsync",
            Times.Never(),
            ItExpr.Is<HttpRequestMessage>(r =>
                r.RequestUri!.ToString().Contains("169.254.169.254") ||
                r.RequestUri.ToString().Contains("127.0.0.1") ||
                r.RequestUri.ToString().Contains("localhost") ||
                r.RequestUri.ToString().Contains("192.168.1.100")),
            ItExpr.IsAny<CancellationToken>());
    }

    /// <summary>
    /// Tests that child manifests inheriting default modification types are mapped correctly when type is omitted.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task DiscoverAsync_WhenChildManifestOmitsModificationType_InheritsDefaultType()
    {
        var mockHttp = new Mock<HttpMessageHandler>();

        const string rootYaml = @"
LauncherVersion: '1.0'
modDatas:
  - ModName: 'Test Mod'
    ModLink: 'https://example.com/test-mod.yaml'
    ModPatches:
      - 'https://example.com/test-patch.yaml'
";

        const string parentYaml = @"
Name: 'Test Mod'
Version: '1.0.0'
ModificationType: 0
SimpleDownloadLink: 'https://example.com/test-mod.zip'
";

        // Omits ModificationType completely
        const string patchYaml = @"
Name: 'Test Patch'
Version: '1.0.1'
DependenceName: 'Test Mod'
SimpleDownloadLink: 'https://example.com/test-patch.zip'
";

        mockHttp.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => r.RequestUri!.ToString().Contains("Generals")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("LauncherVersion: '1.0'\nmodDatas: []"),
            });

        mockHttp.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => r.RequestUri!.ToString().Contains("ZH")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(rootYaml),
            });

        mockHttp.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => r.RequestUri!.ToString().Contains("test-mod.yaml")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(parentYaml),
            });

        mockHttp.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => r.RequestUri!.ToString().Contains("test-patch.yaml")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(patchYaml),
            });

        // HEAD requests
        mockHttp.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => r.Method == HttpMethod.Head),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK));

        var httpClient = new HttpClient(mockHttp.Object);
        var mockFactory = new Mock<IHttpClientFactory>();
        mockFactory.Setup(f => f.CreateClient(PublisherTypeConstants.GenLauncher)).Returns(httpClient);

        var mockLoader = new Mock<IProviderDefinitionLoader>();
        mockLoader.Setup(l => l.GetProvider(GenLauncherConstants.PublisherId)).Returns((ProviderDefinition?)null);

        var parser = new GenLauncherCatalogParser(NullLogger<GenLauncherCatalogParser>.Instance);
        var discoverer = new GenLauncherDiscoverer(
            mockFactory.Object,
            mockLoader.Object,
            parser,
            NullLogger<GenLauncherDiscoverer>.Instance);

        var query = new ContentSearchQuery
        {
            TargetGame = GameType.ZeroHour,
        };

        var result = await discoverer.DiscoverAsync(query, CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);

        var patchItem = result.Data.Items.FirstOrDefault(i => i.Name == "Test Patch");
        Assert.NotNull(patchItem);
        Assert.Equal(ContentType.Patch, patchItem.ContentType);
    }

    /// <summary>
    /// Tests that a plain-http download link redirecting to https still yields the real payload size.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task DiscoverAsync_WhenDownloadLinkRedirects_FollowsRedirectForSize()
    {
        var mockHttp = new Mock<HttpMessageHandler>();

        const string rootYaml = @"
LauncherVersion: '1.0'
modDatas:
  - ModName: 'Redirect Mod'
    ModLink: 'https://example.com/redirect-mod.yaml'
    ModPatches: []
    ModAddons: []
";

        const string parentYaml = @"
Name: 'Redirect Mod'
Version: '1.5'
ModificationType: 0
SimpleDownloadLink: 'http://cdn.example.com/redirect-mod.zip'
";

        mockHttp.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => r.Method == HttpMethod.Get && r.RequestUri!.ToString().Contains("ZH")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(rootYaml),
            });

        mockHttp.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => r.Method == HttpMethod.Get && r.RequestUri!.ToString().Contains("redirect-mod.yaml")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(parentYaml),
            });

        // Plain-http HEAD answers with a redirect to https, mirroring gen.insave.ovh mirrors
        var redirect = new HttpResponseMessage(HttpStatusCode.MovedPermanently)
        {
            Content = new ByteArrayContent([]),
        };
        redirect.Headers.Location = new Uri("https://cdn.example.com/redirect-mod.zip");
        mockHttp.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => r.Method == HttpMethod.Head && r.RequestUri!.Scheme == Uri.UriSchemeHttp),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(redirect);

        const long expectedSize = 551417137;
        var finalContent = new ByteArrayContent([]);
        finalContent.Headers.ContentLength = expectedSize;
        finalContent.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        mockHttp.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => r.Method == HttpMethod.Head && r.RequestUri!.Scheme == Uri.UriSchemeHttps),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = finalContent,
            });

        var httpClient = new HttpClient(mockHttp.Object);
        var mockFactory = new Mock<IHttpClientFactory>();
        mockFactory.Setup(f => f.CreateClient(PublisherTypeConstants.GenLauncher)).Returns(httpClient);

        var mockLoader = new Mock<IProviderDefinitionLoader>();
        mockLoader.Setup(l => l.GetProvider(GenLauncherConstants.PublisherId)).Returns((ProviderDefinition?)null);

        var parser = new GenLauncherCatalogParser(NullLogger<GenLauncherCatalogParser>.Instance);
        var discoverer = new GenLauncherDiscoverer(
            mockFactory.Object,
            mockLoader.Object,
            parser,
            NullLogger<GenLauncherDiscoverer>.Instance);

        var result = await discoverer.DiscoverAsync(
            new ContentSearchQuery { TargetGame = GameType.ZeroHour },
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);

        var item = result.Data.Items.FirstOrDefault(i => i.Name == "Redirect Mod");
        Assert.NotNull(item);
        Assert.Equal(expectedSize, item.DownloadSize);
    }

    /// <summary>
    /// Tests that OneDrive "/embed" share links fall back to name-based archive file names
    /// so the parent mod and its patches keep distinct file names instead of collapsing to "embed".
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task DiscoverAsync_WhenDownloadLinksAreOneDriveEmbeds_UsesDistinctArchiveFileNames()
    {
        var mockHttp = new Mock<HttpMessageHandler>();

        const string rootYaml = @"
LauncherVersion: '1.0'
modDatas:
  - ModName: 'Rise of the Reds'
    ModLink: 'https://example.com/rise-of-the-reds.yaml'
    ModPatches:
      - 'https://example.com/balance-patch.yaml'
    ModAddons: []
";

        const string parentYaml = @"
Name: 'Rise of the Reds'
Version: '1.87 Public Build 2.0'
ModificationType: 0
SimpleDownloadLink: 'https://onedrive.live.com/embed?cid=AFB01C08E053A64E&resid=AFB01C08E053A64E%21593&authkey=AMJHOwXKTTTErrI'
";

        const string patchYaml = @"
Name: 'Balance Patch'
Version: '2.999.06.5'
ModificationType: 2
DependenceName: 'Rise of the Reds'
SimpleDownloadLink: 'https://onedrive.live.com/embed?cid=0A88C98986A457EB&resid=A88C98986A457EB%21135&authkey=AE2ADilQfRS431o'
";

        mockHttp.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => r.Method == HttpMethod.Get && r.RequestUri!.ToString().Contains("ZH")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(rootYaml),
            });

        mockHttp.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => r.Method == HttpMethod.Get && r.RequestUri!.ToString().Contains("rise-of-the-reds.yaml")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(parentYaml),
            });

        mockHttp.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => r.Method == HttpMethod.Get && r.RequestUri!.ToString().Contains("balance-patch.yaml")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(patchYaml),
            });

        var httpClient = new HttpClient(mockHttp.Object);
        var mockFactory = new Mock<IHttpClientFactory>();
        mockFactory.Setup(f => f.CreateClient(PublisherTypeConstants.GenLauncher)).Returns(httpClient);

        var mockLoader = new Mock<IProviderDefinitionLoader>();
        mockLoader.Setup(l => l.GetProvider(GenLauncherConstants.PublisherId)).Returns((ProviderDefinition?)null);

        var parser = new GenLauncherCatalogParser(NullLogger<GenLauncherCatalogParser>.Instance);
        var discoverer = new GenLauncherDiscoverer(
            mockFactory.Object,
            mockLoader.Object,
            parser,
            NullLogger<GenLauncherDiscoverer>.Instance);

        var result = await discoverer.DiscoverAsync(
            new ContentSearchQuery { TargetGame = GameType.ZeroHour },
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);

        var parent = result.Data.Items.FirstOrDefault(i => i.Name == "Rise of the Reds");
        Assert.NotNull(parent);
        Assert.NotNull(parent.ParsedPageData);

        var files = parent.ParsedPageData.Sections.OfType<DownloadableFile>().ToList();
        Assert.Equal(2, files.Count);
        Assert.DoesNotContain(files, f => string.Equals(f.Filename, "embed", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(2, files.Select(f => f.Filename).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(files, f => Assert.EndsWith(".zip", f.Filename, StringComparison.OrdinalIgnoreCase));
    }
}
