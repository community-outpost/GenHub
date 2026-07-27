using System.Net;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Results;
using GenHub.Features.Content.Services.GeneralsOnline;
using GenHub.Tests.Core.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace GenHub.Tests.Core.Features.Content.Services.GeneralsOnline;

/// <summary>
/// Tests for <see cref="GeneralsOnlineUpdateService"/>.
/// </summary>
public class GeneralsOnlineUpdateServiceTests
{
    /// <summary>
    /// Verifies that update checks compare the CDN release with the newest installed
    /// Generals Online version rather than an arbitrary manifest-pool entry.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task CheckForUpdatesAsync_MultipleInstalledVersions_UsesNewestVersion()
    {
        var manifestPool = new Mock<IContentManifestPool>();
        manifestPool
            .Setup(pool => pool.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess(
            [
                CreateManifest("1.1215251.generalsonline.gameclient.60hz", "121525_QFE1"),
                CreateManifest("1.605261.generalsonline.gameclient.60hz", "060526_QFE1"),
            ]));

        var providerLoader = new Mock<IProviderDefinitionLoader>();
        providerLoader
            .Setup(loader => loader.GetProvider(GeneralsOnlineConstants.PublisherType))
            .Returns(new ProviderDefinition
            {
                ProviderId = GeneralsOnlineConstants.PublisherType,
                PublisherType = GeneralsOnlineConstants.PublisherType,
                VersionScheme = VersionSchemeConstants.MmddyyQfe,
                Endpoints = new ProviderEndpoints
                {
                    LatestVersionUrl = "https://example.test/latest.txt",
                },
            });

        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory
            .Setup(factory => factory.CreateClient(GeneralsOnlineConstants.PublisherType))
            .Returns(new HttpClient(new StaticResponseHandler("060526_QFE1")));

        using var service = new GeneralsOnlineUpdateService(
            NullLogger<GeneralsOnlineUpdateService>.Instance,
            manifestPool.Object,
            httpClientFactory.Object,
            providerLoader.Object,
            TestVersionComparer.CreateDefault());

        var result = await service.CheckForUpdatesAsync(CancellationToken.None);

        Assert.True(result.Success);
        Assert.False(result.IsUpdateAvailable);
        Assert.Equal("060526_QFE1", result.CurrentVersion);
        Assert.Equal("060526_QFE1", result.LatestVersion);
    }

    private static ContentManifest CreateManifest(string id, string version) => new()
    {
        Id = ManifestId.Create(id),
        Name = "Generals Online",
        Version = version,
        Publisher = new PublisherInfo
        {
            PublisherType = GeneralsOnlineConstants.PublisherType,
        },
    };

    private sealed class StaticResponseHandler(string version) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(version),
                RequestMessage = request,
            });
    }
}
