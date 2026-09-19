using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Features.Content.Services;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Content.Services;

/// <summary>
/// Regression tests for per-manifest artwork persistence.
/// </summary>
public sealed class ContentArtworkServiceTests
{
    private sealed class StubHttpHandler : HttpMessageHandler
    {
        private readonly byte[] _bytes;

        public StubHttpHandler(byte[] bytes)
        {
            _bytes = bytes;
        }

        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(_bytes) });
        }
    }

    /// <summary>
    /// Verifies that probing unknown artwork returns null.
    /// </summary>
    [Fact]
    public void GetLocalArtworkPath_WhenNothingStored_ReturnsNull()
    {
        var root = CreateTempDir();
        try
        {
            var service = CreateService(root, new StubHttpHandler([1, 2, 3]));

            Assert.Null(service.GetLocalArtworkPath("1.20260101.test.mod.alpha", ContentArtworkKind.Icon));
            Assert.Null(service.GetLocalArtworkPath("1.20260101.test.mod.alpha", ContentArtworkKind.Cover));
        }
        finally
        {
            DeleteTempDir(root);
        }
    }

    /// <summary>
    /// Verifies that missing remote artwork is downloaded into per-manifest storage.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task PrefetchArtworkAsync_DownloadsMissingArtworkAsync()
    {
        var root = CreateTempDir();
        try
        {
            var handler = new StubHttpHandler([1, 2, 3, 4]);
            var service = CreateService(root, handler);

            var result = await service.PrefetchArtworkAsync(CreateManifest());

            Assert.True(result.Success);
            Assert.Equal(2, handler.CallCount);
            Assert.NotNull(service.GetLocalArtworkPath("1.20260101.test.mod.alpha", ContentArtworkKind.Icon));
            Assert.NotNull(service.GetLocalArtworkPath("1.20260101.test.mod.alpha", ContentArtworkKind.Cover));
        }
        finally
        {
            DeleteTempDir(root);
        }
    }

    /// <summary>
    /// Verifies that already-stored artwork is not downloaded again.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task PrefetchArtworkAsync_SkipsAlreadyStoredAsync()
    {
        var root = CreateTempDir();
        try
        {
            var handler = new StubHttpHandler([1, 2, 3, 4]);
            var service = CreateService(root, handler);
            await service.PrefetchArtworkAsync(CreateManifest());

            var result = await service.PrefetchArtworkAsync(CreateManifest());

            Assert.True(result.Success);
            Assert.Equal(2, handler.CallCount);
        }
        finally
        {
            DeleteTempDir(root);
        }
    }

    /// <summary>
    /// Verifies that unsafe or malformed URLs are skipped without network access.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task PrefetchArtworkAsync_WithUnsafeUrls_SkipsWithoutDownloadAsync()
    {
        var root = CreateTempDir();
        try
        {
            var handler = new StubHttpHandler([1, 2, 3, 4]);
            var service = CreateService(root, handler);
            var manifest = CreateManifest();
            manifest.Metadata.IconUrl = "http://localhost/icon.png";
            manifest.Metadata.CoverUrl = "not a url";

            var result = await service.PrefetchArtworkAsync(manifest);

            Assert.True(result.Success);
            Assert.Equal(0, handler.CallCount);
        }
        finally
        {
            DeleteTempDir(root);
        }
    }

    /// <summary>
    /// Verifies that purging removes a manifest's artwork directory.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task PurgeArtworkAsync_RemovesStoredDirectoryAsync()
    {
        var root = CreateTempDir();
        try
        {
            var service = CreateService(root, new StubHttpHandler([1, 2, 3, 4]));
            await service.PrefetchArtworkAsync(CreateManifest());

            var result = await service.PurgeArtworkAsync("1.20260101.test.mod.alpha");

            Assert.True(result.Success);
            Assert.Null(service.GetLocalArtworkPath("1.20260101.test.mod.alpha", ContentArtworkKind.Icon));
            Assert.False(Directory.Exists(Path.Combine(root, "Artwork", "1.20260101.test.mod.alpha")));
        }
        finally
        {
            DeleteTempDir(root);
        }
    }

    /// <summary>
    /// Verifies that purging unknown artwork succeeds without side effects.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task PurgeArtworkAsync_WhenNothingStored_SucceedsAsync()
    {
        var root = CreateTempDir();
        try
        {
            var service = CreateService(root, new StubHttpHandler([1, 2, 3, 4]));

            var result = await service.PurgeArtworkAsync("1.20260101.test.mod.alpha");

            Assert.True(result.Success);
        }
        finally
        {
            DeleteTempDir(root);
        }
    }

    private static ContentArtworkService CreateService(string appDataPath, StubHttpHandler handler)
    {
        var configuration = new Mock<IConfigurationProviderService>();
        configuration.Setup(config => config.GetApplicationDataPath()).Returns(appDataPath);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(creator => creator.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(handler));
        return new ContentArtworkService(
            configuration.Object,
            factory.Object,
            new Mock<ILogger<ContentArtworkService>>().Object);
    }

    private static ContentManifest CreateManifest()
    {
        var manifest = new ContentManifest
        {
            Id = ManifestId.Create("1.20260101.test.mod.alpha"),
            Name = "Alpha Mod",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
        };
        manifest.Metadata.IconUrl = "https://example.com/icon.png";
        manifest.Metadata.CoverUrl = "https://example.com/cover.jpg";
        return manifest;
    }

    private static string CreateTempDir()
    {
        var root = Path.Combine(Path.GetTempPath(), "genhub-artwork-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteTempDir(string root)
    {
        try
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
