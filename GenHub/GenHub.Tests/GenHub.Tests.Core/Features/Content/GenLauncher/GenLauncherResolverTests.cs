using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GenLauncher;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.Content.Services.GenLauncher;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Content.GenLauncher;

/// <summary>
/// Unit tests for <see cref="GenLauncherResolver"/>.
/// </summary>
public sealed class GenLauncherResolverTests
{
    private const string SampleS3Xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<ListBucketResult xmlns=""http://s3.amazonaws.com/doc/2006-03-01/"">
    <Name>genlauncher</Name>
    <Contents>
        <Key>Mods/Shockwave/Shockwave.big</Key>
        <ETag>&quot;0123456789abcdef0123456789abcdef&quot;</ETag>
        <Size>1024</Size>
    </Contents>
</ListBucketResult>";

    /// <summary>
    /// Tests that ResolveAsync parses S3 payloads into files and sets dependencies.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ResolveAsync_WithS3Payload_ParsesFilesAndDependencies()
    {
        var factoryMock = new Mock<IHttpClientFactory>();
        var handlerMock = new Mock<HttpMessageHandler>();

        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(SampleS3Xml),
            });

        var httpClient = new HttpClient(handlerMock.Object);
        factoryMock.Setup(f => f.CreateClient(PublisherTypeConstants.GenLauncher)).Returns(httpClient);

        var parser = new GenLauncherCatalogParser(Mock.Of<ILogger<GenLauncherCatalogParser>>());
        var loggerMock = new Mock<ILogger<GenLauncherResolver>>();

        var resolver = new GenLauncherResolver(factoryMock.Object, parser, loggerMock.Object);

        var searchResult = new ContentSearchResult
        {
            Id = "shockwave",
            Name = "Shockwave",
            Version = "1.2",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
            ResolverMetadata =
            {
                ["s3HostLink"] = "s3.amazonaws.com",
                ["s3BucketName"] = "genlauncher",
                ["s3FolderName"] = "Mods/Shockwave",
            },
        };
        searchResult.SetData(new GenLauncherVersionManifest
        {
            Name = "Shockwave",
            Version = "1.2",
            DependenceName = "ZeroHour",
            S3HostLink = "s3.amazonaws.com",
            S3BucketName = "genlauncher",
            S3FolderName = "Mods/Shockwave",
        });

        var result = await resolver.ResolveAsync(searchResult, CancellationToken.None);

        Assert.True(result.Success, result.FirstError);
        Assert.NotNull(result.Data);

        var manifest = result.Data;
        Assert.Equal("Shockwave", manifest.Name);
        Assert.Equal("1.2", manifest.Version);
        Assert.Equal(ContentType.Mod, manifest.ContentType);
        Assert.Equal(GameType.ZeroHour, manifest.TargetGame);

        Assert.Single(manifest.Files);
        Assert.Equal("Shockwave.big", manifest.Files[0].RelativePath);
        Assert.Equal("0123456789abcdef0123456789abcdef", manifest.Files[0].ETag);
        Assert.Equal(string.Empty, manifest.Files[0].Hash);

        // Verify dependencies
        Assert.Contains(manifest.Dependencies, d => d.DependencyType == ContentType.GameInstallation);
        Assert.Contains(manifest.Dependencies, d => d.Name == "ZeroHour");
        Assert.Equal("1.0.genlauncherzerohour.mod.shockwave", manifest.Id.Value);

        var parentDep = Assert.Single(manifest.Dependencies, d => d.DependencyType == ContentType.Mod);
        Assert.Equal("1.0.genlauncherzerohour.mod.zerohour", parentDep.Id.Value);
    }

    /// <summary>
    /// Tests that ResolveAsync resolves simple direct download links.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ResolveAsync_WithSimpleDownloadLink_ResolvesDirectDownload()
    {
        var factoryMock = new Mock<IHttpClientFactory>();
        var parser = new GenLauncherCatalogParser(Mock.Of<ILogger<GenLauncherCatalogParser>>());
        var loggerMock = new Mock<ILogger<GenLauncherResolver>>();

        var resolver = new GenLauncherResolver(factoryMock.Object, parser, loggerMock.Object);

        var searchResult = new ContentSearchResult
        {
            Id = "shockwave-patch",
            Name = "Shockwave Patch",
            Version = "1.0",
            ContentType = ContentType.Patch,
            TargetGame = GameType.ZeroHour,
            SourceUrl = "https://example.com/downloads/patch.zip",
        };

        var result = await resolver.ResolveAsync(searchResult, CancellationToken.None);

        Assert.True(result.Success, result.FirstError);
        Assert.NotNull(result.Data);

        var manifest = result.Data;
        Assert.Single(manifest.Files);
        Assert.Equal("patch.zip", manifest.Files[0].RelativePath);
        Assert.Equal("https://example.com/downloads/patch.zip", manifest.Files[0].DownloadUrl);
    }

    /// <summary>
    /// Tests that ResolveAsync throws when discovered item is null.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ResolveAsync_NullDiscoveredItem_ThrowsArgumentNullException()
    {
        var factoryMock = new Mock<IHttpClientFactory>();
        var parser = new GenLauncherCatalogParser(Mock.Of<ILogger<GenLauncherCatalogParser>>());
        var loggerMock = new Mock<ILogger<GenLauncherResolver>>();

        var resolver = new GenLauncherResolver(factoryMock.Object, parser, loggerMock.Object);

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            resolver.ResolveAsync(null!, CancellationToken.None));
    }

    /// <summary>
    /// Tests that ResolveAsync rejects loopback S3 host to prevent SSRF.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ResolveAsync_WithLoopbackS3Host_RejectsS3Resolution()
    {
        var factoryMock = new Mock<IHttpClientFactory>();
        var handlerMock = new Mock<HttpMessageHandler>();
        var httpClient = new HttpClient(handlerMock.Object);

        factoryMock.Setup(f => f.CreateClient(PublisherTypeConstants.GenLauncher))
            .Returns(httpClient);

        var parser = new GenLauncherCatalogParser(Mock.Of<ILogger<GenLauncherCatalogParser>>());
        var loggerMock = new Mock<ILogger<GenLauncherResolver>>();

        var resolver = new GenLauncherResolver(factoryMock.Object, parser, loggerMock.Object);

        var searchResult = new ContentSearchResult
        {
            Id = "shockwave-ssrf",
            Name = "Shockwave SSRF",
            Version = "1.0",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
            ResolverMetadata =
            {
                ["s3HostLink"] = "127.0.0.1:9000",
                ["s3BucketName"] = "internal-bucket",
                ["s3FolderName"] = "Mods/Test",
            },
        };

        var result = await resolver.ResolveAsync(searchResult, CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Empty(result.Data.Files);
        handlerMock.Protected().Verify(
            "SendAsync",
            Times.Never(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    /// <summary>
    /// Tests that ResolveAsync ignores YAML URLs as direct download archives to prevent corrupt downloads.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ResolveAsync_WithYamlSourceUrl_DoesNotSetYamlAsDownloadFile()
    {
        var factoryMock = new Mock<IHttpClientFactory>();
        var parser = new GenLauncherCatalogParser(Mock.Of<ILogger<GenLauncherCatalogParser>>());
        var loggerMock = new Mock<ILogger<GenLauncherResolver>>();

        var resolver = new GenLauncherResolver(factoryMock.Object, parser, loggerMock.Object);

        var searchResult = new ContentSearchResult
        {
            Id = "yaml-mod",
            Name = "YAML Mod",
            Version = "1.0",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
            SourceUrl = "https://raw.githubusercontent.com/test/mod/main/mod.yaml",
        };

        var result = await resolver.ResolveAsync(searchResult, CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Empty(result.Data.Files);
    }
}
