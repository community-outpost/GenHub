using GenHub.Core.Constants;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GenLauncher;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.Content.Services.GenLauncher;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using System;
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
        var (resolver, _) = CreateResolverWithHandler(new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(SampleS3Xml),
        });

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
        var resolver = CreateResolver();

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
    /// Tests that ResolveAsync falls back to a slug-based archive name when the direct
    /// download link is an extensionless endpoint URL such as a OneDrive "/embed" share link.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ResolveAsync_WithOneDriveEmbedLink_FallsBackToSlugArchiveName()
    {
        var resolver = CreateResolver();

        var searchResult = new ContentSearchResult
        {
            Id = "balance-patch",
            Name = "Balance Patch",
            Version = "2.999.06.5",
            ContentType = ContentType.Patch,
            TargetGame = GameType.ZeroHour,
            SourceUrl = "https://onedrive.live.com/embed?cid=0A88C98986A457EB&resid=A88C98986A457EB%21135&authkey=AE2ADilQfRS431o",
        };

        var result = await resolver.ResolveAsync(searchResult, CancellationToken.None);

        Assert.True(result.Success, result.FirstError);
        Assert.NotNull(result.Data);

        var manifest = result.Data;
        Assert.Single(manifest.Files);
        Assert.Equal("balance-patch.zip", manifest.Files[0].RelativePath);
    }

    /// <summary>
    /// Tests that ResolveAsync appends .zip when the URL path or fallback name ends without a known archive extension.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ResolveAsync_WithNonArchiveExtensionInUrl_AppendsZipFallback()
    {
        var resolver = CreateResolver();

        var searchResult = new ContentSearchResult
        {
            Id = "version-tag-mod",
            Name = "Shockwave-Release-v1.2",
            Version = "1.2",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
            SourceUrl = "https://example.com/releases/download/v1.2",
        };

        var result = await resolver.ResolveAsync(searchResult, CancellationToken.None);

        Assert.True(result.Success, result.FirstError);
        Assert.NotNull(result.Data);

        var manifest = result.Data;
        Assert.Single(manifest.Files);
        Assert.Equal("Shockwave-Release-v1.2.zip", manifest.Files[0].RelativePath);
    }

    /// <summary>
    /// Tests that ResolveAsync throws when discovered item is null.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ResolveAsync_NullDiscoveredItem_ThrowsArgumentNullException()
    {
        var resolver = CreateResolver();

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
        var handlerMock = new Mock<HttpMessageHandler>();
        var httpClient = new HttpClient(handlerMock.Object);
        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(f => f.CreateClient(PublisherTypeConstants.GenLauncher)).Returns(httpClient);
        var resolver = CreateResolver(factoryMock.Object);

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
        var (resolver, handlerMock) = CreateResolverWithHandler(new HttpResponseMessage(HttpStatusCode.NotFound));

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
        handlerMock.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.Is<HttpRequestMessage>(req => req.RequestUri == new Uri("https://raw.githubusercontent.com/test/mod/main/mod.yaml")),
            ItExpr.IsAny<CancellationToken>());
    }

    /// <summary>
    /// Tests that ResolveAsync with game-prefixed VariantGroupId produces matching parent and dependency IDs.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ResolveAsync_WithGamePrefixedVariantGroupId_ProducesMatchingParentAndDependencyIds()
    {
        var resolver = CreateResolver();

        // Parent Mod with game-prefixed VariantGroupId as produced by GenLauncherDiscoverer
        var parentSearchResult = new ContentSearchResult
        {
            Id = "gl-zh-shockwave",
            Name = "Shockwave",
            Version = "1.2",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
            VariantGroupId = "zerohour-shockwave",
        };

        // Addon with game-prefixed VariantGroupId and DependenceName pointing to parent
        var addonSearchResult = new ContentSearchResult
        {
            Id = "gl-zh-shockwave-patch",
            Name = "Patch 1.201",
            Version = "1.201",
            ContentType = ContentType.Patch,
            TargetGame = GameType.ZeroHour,
            VariantGroupId = "zerohour-shockwave",
            ResolverMetadata =
            {
                [GenLauncherConstants.DependenceNameMetadataKey] = "Shockwave",
            },
        };

        var parentResult = await resolver.ResolveAsync(parentSearchResult, CancellationToken.None);
        var addonResult = await resolver.ResolveAsync(addonSearchResult, CancellationToken.None);

        Assert.True(parentResult.Success);
        Assert.True(addonResult.Success);

        var parentManifest = parentResult.Data!;
        var addonManifest = addonResult.Data!;

        // The parent manifest ID should not contain duplicate game/slug tokens
        Assert.Equal("1.0.genlauncherzerohour.mod.shockwave", parentManifest.Id.Value);

        // The addon should depend on the parent mod, and the dependency ID must equal the parent manifest ID
        var parentDependency = addonManifest.Dependencies.Find(d => d.DependencyType == ContentType.Mod && d.Name == "Shockwave");
        Assert.NotNull(parentDependency);
        Assert.Equal(parentManifest.Id, parentDependency.Id);
    }

    /// <summary>
    /// Tests that ResolveAsync prioritizes direct download URL for child content items with ParentContentId,
    /// falls back to discoveredItem.Name when URL lacks an archive extension,
    /// and does not make S3 network calls.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ResolveAsync_WithChildContentDirectDownload_PrioritizesDirectDownloadAndDoesNotQueryS3()
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        var httpClient = new HttpClient(handlerMock.Object);
        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(f => f.CreateClient(PublisherTypeConstants.GenLauncher)).Returns(httpClient);
        var resolver = CreateResolver(factoryMock.Object);

        var searchResult = new ContentSearchResult
        {
            Id = "shockwave-patch",
            Name = "ShockWave_Balance_Patch_2.999.06.5.zip",
            Version = "2.999.06.5",
            ContentType = ContentType.Patch,
            TargetGame = GameType.ZeroHour,
            SelectedDownloadUrl = "https://onedrive.live.com/download?cid=0A88C98986A457EB&resid=A88C98986A457EB%21135&authkey=AE2ADilQfRS431o",
            ResolverMetadata =
            {
                [ContentConstants.ParentContentIdMetadataKey] = "shockwave-main",
                [GenLauncherConstants.S3HostMetadataKey] = "s3.example.com",
                [GenLauncherConstants.S3BucketMetadataKey] = "mod-bucket",
                [GenLauncherConstants.S3FolderMetadataKey] = "Mods/Shockwave",
            },
        };

        var result = await resolver.ResolveAsync(searchResult, CancellationToken.None);

        Assert.True(result.Success, result.FirstError);
        Assert.NotNull(result.Data);

        var manifest = result.Data;
        Assert.Single(manifest.Files);
        Assert.Equal("ShockWave_Balance_Patch_2.999.06.5.zip", manifest.Files[0].RelativePath);
        Assert.Equal("https://onedrive.live.com/download?cid=0A88C98986A457EB&resid=A88C98986A457EB%21135&authkey=AE2ADilQfRS431o", manifest.Files[0].DownloadUrl);

        // Ensure no S3 HTTP requests were made
        handlerMock.Protected().Verify(
            "SendAsync",
            Times.Never(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    /// <summary>
    /// Tests that ResolveAsync prioritizes SelectedDownloadUrl over SimpleDownloadLink metadata for child items.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ResolveAsync_ChildModWithConflictingSimpleDownloadLinkAndSelectedDownloadUrl_PrioritizesSelectedDownloadUrl()
    {
        var resolver = CreateResolver();

        var searchResult = new ContentSearchResult
        {
            Id = "child-patch",
            Name = "Patch.zip",
            Version = "1.1",
            ContentType = ContentType.Patch,
            TargetGame = GameType.ZeroHour,
            SelectedDownloadUrl = "https://mirror1.example.com/patch-selected.zip",
            ResolverMetadata =
            {
                [ContentConstants.ParentContentIdMetadataKey] = "parent-mod",
                [GenLauncherConstants.SimpleDownloadLinkMetadataKey] = "https://mirror2.example.com/patch-simple.zip",
            },
        };

        var result = await resolver.ResolveAsync(searchResult, CancellationToken.None);

        Assert.True(result.Success, result.FirstError);
        Assert.NotNull(result.Data);

        var manifest = result.Data;
        Assert.Single(manifest.Files);
        Assert.Equal("https://mirror1.example.com/patch-selected.zip", manifest.Files[0].DownloadUrl);
        Assert.Equal("patch-selected.zip", manifest.Files[0].RelativePath);
    }

    /// <summary>
    /// Tests that ResolveAsync falls back to S3 storage if direct download candidates yield zero files.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ResolveAsync_ChildModWithInvalidDirectUrl_FallsBackToS3()
    {
        var (resolver, handlerMock) = CreateResolverWithHandler(new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(SampleS3Xml),
        });

        var searchResult = new ContentSearchResult
        {
            Id = "child-mod",
            Name = "Shockwave Addon",
            Version = "1.0",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,

            // Invalid candidate: loopback URL is rejected as unsafe
            SelectedDownloadUrl = "http://127.0.0.1/malicious.zip",
            ResolverMetadata =
            {
                [ContentConstants.ParentContentIdMetadataKey] = "parent-mod",
                [GenLauncherConstants.S3HostMetadataKey] = "s3.amazonaws.com",
                [GenLauncherConstants.S3BucketMetadataKey] = "genlauncher",
                [GenLauncherConstants.S3FolderMetadataKey] = "Mods/Shockwave",
            },
        };

        var result = await resolver.ResolveAsync(searchResult, CancellationToken.None);

        Assert.True(result.Success, result.FirstError);
        Assert.NotNull(result.Data);

        var manifest = result.Data;
        Assert.Single(manifest.Files);
        Assert.Equal("Shockwave.big", manifest.Files[0].RelativePath);

        handlerMock.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    /// <summary>
    /// Tests that ResolveAsync prioritizes S3 storage over SimpleDownloadLink for main catalog mods.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ResolveAsync_MainModWithS3MetadataAndSimpleDownloadLink_PrioritizesS3Storage()
    {
        var (resolver, handlerMock) = CreateResolverWithHandler(new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(SampleS3Xml),
        });

        var searchResult = new ContentSearchResult
        {
            Id = "shockwave",
            Name = "Shockwave",
            Version = "1.2",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
            SelectedDownloadUrl = "https://onedrive.live.com/download?cid=0A88C98986A457EB",
            ResolverMetadata =
            {
                [GenLauncherConstants.SimpleDownloadLinkMetadataKey] = "https://onedrive.live.com/download?cid=0A88C98986A457EB",
                [GenLauncherConstants.S3HostMetadataKey] = "s3.amazonaws.com",
                [GenLauncherConstants.S3BucketMetadataKey] = "genlauncher",
                [GenLauncherConstants.S3FolderMetadataKey] = "Mods/Shockwave",
            },
        };

        var result = await resolver.ResolveAsync(searchResult, CancellationToken.None);

        Assert.True(result.Success, result.FirstError);
        Assert.NotNull(result.Data);

        var manifest = result.Data;
        Assert.Single(manifest.Files);
        Assert.Equal("Shockwave.big", manifest.Files[0].RelativePath);

        handlerMock.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    /// <summary>
    /// Tests that ResolveAsync skips an unsafe SelectedDownloadUrl and falls back to a safe SimpleDownloadLink.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ResolveAsync_WithUnsafeSelectedDownloadUrl_FallsBackToSafeSimpleDownloadLinkAsync()
    {
        var resolver = CreateResolver();

        var searchResult = new ContentSearchResult
        {
            Id = "fallback-mod",
            Name = "Fallback Mod",
            Version = "1.0",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
            SelectedDownloadUrl = "http://127.0.0.1:8080/unsafe.zip",
            ResolverMetadata =
            {
                [GenLauncherConstants.SimpleDownloadLinkMetadataKey] = "https://example.com/safe.zip",
            },
        };

        var result = await resolver.ResolveAsync(searchResult, CancellationToken.None);

        Assert.True(result.Success, result.FirstError);
        Assert.NotNull(result.Data);
        var file = Assert.Single(result.Data.Files);
        Assert.Equal("https://example.com/safe.zip", file.DownloadUrl);
    }

    private static GenLauncherResolver CreateResolver(
        IHttpClientFactory? httpClientFactory = null,
        ILogger<GenLauncherResolver>? logger = null)
    {
        var factory = httpClientFactory ?? Mock.Of<IHttpClientFactory>();
        var parser = new GenLauncherCatalogParser(Mock.Of<ILogger<GenLauncherCatalogParser>>());
        var log = logger ?? Mock.Of<ILogger<GenLauncherResolver>>();
        return new GenLauncherResolver(factory, parser, log);
    }

    private static (GenLauncherResolver Resolver, Mock<HttpMessageHandler> HandlerMock) CreateResolverWithHandler(
        HttpResponseMessage response)
    {
        var responseContent = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        var statusCode = response.StatusCode;

        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() => new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseContent),
            });

        var httpClient = new HttpClient(handlerMock.Object);
        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(f => f.CreateClient(PublisherTypeConstants.GenLauncher)).Returns(httpClient);

        var parser = new GenLauncherCatalogParser(Mock.Of<ILogger<GenLauncherCatalogParser>>());
        var loggerMock = new Mock<ILogger<GenLauncherResolver>>();
        return (new GenLauncherResolver(factoryMock.Object, parser, loggerMock.Object), handlerMock);
    }
}
