using GenHub.Core.Constants;
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
    private sealed class CancelingHttpHandler(CancellationTokenSource cts, byte[] payload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var stream = new CancelOnDisposeStream(new MemoryStream(payload), cts);
            var content = new StreamContent(stream);
            content.Headers.ContentLength = payload.Length;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }

    private sealed class CancelOnDisposeStream(Stream inner, CancellationTokenSource cts) : Stream
    {
        public override bool CanRead => inner.CanRead;

        public override bool CanSeek => inner.CanSeek;

        public override bool CanWrite => inner.CanWrite;

        public override long Length => inner.Length;

        public override long Position { get => inner.Position; set => inner.Position = value; }

        public override void Flush() => inner.Flush();

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);

        public override void SetLength(long value) => inner.SetLength(value);

        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            inner.ReadAsync(buffer, offset, count, cancellationToken);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            inner.ReadAsync(buffer, cancellationToken);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                cts.Cancel();
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }

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
            Assert.False(Directory.Exists(Path.Combine(root, ContentArtworkConstants.ArtworkDirectoryName, "1.20260101.test.mod.alpha")));
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

    /// <summary>
    /// Verifies that if downloading artwork is cancelled, no temporary or partial files remain in storage.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task PrefetchArtworkAsync_WhenCancelled_CleansUpTempFilesAsync()
    {
        var root = CreateTempDir();
        try
        {
            var cts = new CancellationTokenSource();
            var handler = new CancelingHttpHandler(cts, [1, 2, 3, 4]);
            var service = CreateService(root, handler);

            var manifest = CreateManifest();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                await service.PrefetchArtworkAsync(manifest, cts.Token);
            });

            var manifestDir = Path.Combine(root, ContentArtworkConstants.ArtworkDirectoryName, manifest.Id.Value);
            if (Directory.Exists(manifestDir))
            {
                var files = Directory.GetFiles(manifestDir, "*.*", SearchOption.AllDirectories);
                Assert.Empty(files);
            }
        }
        finally
        {
            DeleteTempDir(root);
        }
    }

    /// <summary>
    /// Verifies that if committing the downloaded artwork to its final destination fails,
    /// the temporary in-flight file written to disk is deleted by cleanup logic.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task PrefetchArtworkAsync_WhenCommitFails_CleansUpTempFilesAsync()
    {
        var root = CreateTempDir();
        try
        {
            var payload = new byte[] { 1, 2, 3, 4 };
            var handler = new StubHttpHandler(payload);
            var service = CreateService(root, handler);

            var manifest = CreateManifest();
            var manifestDir = Path.Combine(root, ContentArtworkConstants.ArtworkDirectoryName, manifest.Id.Value);
            Directory.CreateDirectory(manifestDir);

            // Create target slot path as a directory so File.Move fails with IOException after temp file is written to disk
            var targetIconPath = Path.Combine(manifestDir, "icon.png");
            Directory.CreateDirectory(targetIconPath);

            var result = await service.PrefetchArtworkAsync(manifest);
            Assert.False(result.Success);

            // Temp files matching TempFilePrefix should have been cleaned up by the finally block
            var tempFiles = Directory.GetFiles(manifestDir, $"{ContentArtworkConstants.TempFilePrefix}*", SearchOption.AllDirectories);
            Assert.Empty(tempFiles);
        }
        finally
        {
            DeleteTempDir(root);
        }
    }

    private static ContentArtworkService CreateService(string appDataPath, HttpMessageHandler handler)
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
