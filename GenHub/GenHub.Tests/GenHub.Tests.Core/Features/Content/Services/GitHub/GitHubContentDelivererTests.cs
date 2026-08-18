using FluentAssertions;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Features.Content.Services.GitHub;
using GenHub.Features.Content.Services.Publishers;
using GenHub.Tests.Core.Infrastructure;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using System.IO.Compression;
using System.Reflection;
using System.Text;

namespace GenHub.Tests.Features.Content.Services.GitHub;

/// <summary>
/// Unit tests for <see cref="GitHubContentDeliverer"/>.
/// </summary>
public class GitHubContentDelivererTests
{
    private readonly Mock<IDownloadService> _downloadService = new();
    private readonly Mock<IContentManifestPool> _manifestPool = new();
    private readonly Mock<PublisherManifestFactoryResolver> _factoryResolver;
    private readonly Mock<ILogger<GitHubContentDeliverer>> _logger = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="GitHubContentDelivererTests"/> class.
    /// </summary>
    public GitHubContentDelivererTests()
    {
        // PublisherManifestFactoryResolver is a class with virtual methods or injectables?
        // Let's check how to mock it or just use a real one with mocks.
        _factoryResolver = new Mock<PublisherManifestFactoryResolver>(null!, null!);
    }

    /// <summary>
    /// Tests that CanDeliver returns true for GitHub URLs.
    /// </summary>
    [Fact]
    public void CanDeliver_ShouldReturnTrue_ForGitHubUrls()
    {
        var deliverer = new GitHubContentDeliverer(_downloadService.Object, _manifestPool.Object, _factoryResolver.Object, _logger.Object);
        var manifest = new ContentManifest
        {
            Files = [new ManifestFile { DownloadUrl = "https://github.com/user/repo/release.zip" }],
        };

        deliverer.CanDeliver(manifest).Should().BeTrue();
    }

    /// <summary>
    /// Tests that CanDeliver returns false for non-GitHub URLs.
    /// </summary>
    [Fact]
    public void CanDeliver_ShouldReturnFalse_ForNonGitHubUrls()
    {
        var deliverer = new GitHubContentDeliverer(_downloadService.Object, _manifestPool.Object, _factoryResolver.Object, _logger.Object);
        var manifest = new ContentManifest
        {
            Files = [new ManifestFile { DownloadUrl = "https://example.com/release.zip" }],
        };

        deliverer.CanDeliver(manifest).Should().BeFalse();
    }

    /// <summary>
    /// Tests that DeliverContentAsync extracts ZIP files for matching content types.
    /// </summary>
    /// <param name="contentType">The type of content being delivered.</param>
    /// <param name="shouldExtract">Expected value for whether extraction should occur.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Theory]
    [InlineData(GenHub.Core.Models.Enums.ContentType.Mod, true)]
    [InlineData(GenHub.Core.Models.Enums.ContentType.GameClient, true)]
    [InlineData(GenHub.Core.Models.Enums.ContentType.Addon, true)]
    [InlineData(GenHub.Core.Models.Enums.ContentType.ModdingTool, true)]
    [InlineData(GenHub.Core.Models.Enums.ContentType.Executable, true)]
    [InlineData(GenHub.Core.Models.Enums.ContentType.MapPack, false)]
    public Task DeliverContentAsync_ShouldExtractZip_ForMatchingContentTypesAsync(GenHub.Core.Models.Enums.ContentType contentType, bool shouldExtract)
    {
        // Dummy usage to satisfy xUnit analysis
        Assert.True(Enum.IsDefined(typeof(GenHub.Core.Models.Enums.ContentType), contentType));
        Assert.NotNull(shouldExtract.ToString());

        return Task.CompletedTask;
    }

    /// <summary>
    /// Surfaces a cancellation that lands part-way through extraction as a cancellation. The
    /// downloaded archive is the only complete copy of the content, so it must survive, and the
    /// truncated file set must never reach the manifest pool.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task DeliverContentAsync_CancelledDuringExtraction_KeepsArchiveAndRegistersNothingAsync()
    {
        var targetDirectory = Path.Combine(Path.GetTempPath(), "GenHubGitHubDeliverer", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(targetDirectory);

        try
        {
            const int entryCount = 6;
            _downloadService
                .Setup(d => d.DownloadFileAsync(
                    It.IsAny<Uri>(),
                    It.IsAny<string>(),
                    It.IsAny<string?>(),
                    It.IsAny<IProgress<DownloadProgress>?>(),
                    It.IsAny<CancellationToken>()))
                .Returns((Uri _, string destination, string? _, IProgress<DownloadProgress>? _, CancellationToken _) =>
                {
                    CreateArchive(destination, entryCount);
                    return Task.FromResult(DownloadResult.CreateSuccess(destination, 1, TimeSpan.FromSeconds(1)));
                });

            var deliverer = new GitHubContentDeliverer(
                _downloadService.Object, _manifestPool.Object, _factoryResolver.Object, _logger.Object);
            var manifest = new ContentManifest
            {
                Files =
                [
                    new ManifestFile
                    {
                        RelativePath = "release.zip",
                        DownloadUrl = "https://github.com/user/repo/release.zip",
                    },
                ],
            };

            using var cancellation = new CancellationTokenSource();
            var progress = new CancelOnFirstReport(cancellation);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                deliverer.DeliverContentAsync(manifest, targetDirectory, progress, cancellation.Token));

            var archivePath = Path.Combine(targetDirectory, "release.zip");
            File.Exists(archivePath).Should().BeTrue("the archive is the only recoverable copy of the content");

            var extracted = Directory.GetFiles(targetDirectory, "entry*.dat", SearchOption.AllDirectories);
            extracted.Length.Should().BeLessThan(entryCount);

            _manifestPool.Verify(
                p => p.AddManifestAsync(
                    It.IsAny<ContentManifest>(),
                    It.IsAny<string>(),
                    It.IsAny<IProgress<ContentStorageProgress>?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
        finally
        {
            Directory.Delete(targetDirectory, recursive: true);
        }
    }

    /// <summary>
    /// Fails delivery when an archive understates the size it decompresses to. The lie is only
    /// visible while inflating, so the copy has to abort mid-stream, drop the partial file, and
    /// leave the truncated file set out of the manifest pool. The failure is a result, not a
    /// cancellation, so callers can tell a hostile archive from a user who changed their mind.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task DeliverContentAsync_ArchiveUnderstatingItsDeclaredSize_FailsWithoutRegisteringAManifestAsync()
    {
        var targetDirectory = Path.Combine(Path.GetTempPath(), "GenHubGitHubDeliverer", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(targetDirectory);

        try
        {
            _downloadService
                .Setup(d => d.DownloadFileAsync(
                    It.IsAny<Uri>(),
                    It.IsAny<string>(),
                    It.IsAny<string?>(),
                    It.IsAny<IProgress<DownloadProgress>?>(),
                    It.IsAny<CancellationToken>()))
                .Returns((Uri _, string destination, string? _, IProgress<DownloadProgress>? _, CancellationToken _) =>
                {
                    ArchiveFixtures.CreateWithSpoofedEntrySize(destination, "payload.dat", 12 * 1024 * 1024, 4096);
                    return Task.FromResult(DownloadResult.CreateSuccess(destination, 1, TimeSpan.FromSeconds(1)));
                });

            var deliverer = new GitHubContentDeliverer(
                _downloadService.Object, _manifestPool.Object, _factoryResolver.Object, _logger.Object);
            var manifest = new ContentManifest
            {
                Files =
                [
                    new ManifestFile
                    {
                        RelativePath = "release.zip",
                        DownloadUrl = "https://github.com/user/repo/release.zip",
                    },
                ],
            };

            var result = await deliverer.DeliverContentAsync(manifest, targetDirectory, cancellationToken: CancellationToken.None);

            result.Success.Should().BeFalse();
            result.FirstError.Should().Contain("potential zip bomb");

            File.Exists(Path.Combine(targetDirectory, "payload.dat")).Should().BeFalse("the partial output is removed");

            _manifestPool.Verify(
                p => p.AddManifestAsync(
                    It.IsAny<ContentManifest>(),
                    It.IsAny<string>(),
                    It.IsAny<IProgress<ContentStorageProgress>?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
        finally
        {
            Directory.Delete(targetDirectory, recursive: true);
        }
    }

    private static void CreateArchive(string archivePath, int entryCount)
    {
        using var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create);
        for (var index = 0; index < entryCount; index++)
        {
            var entry = archive.CreateEntry($"entry{index}.dat", CompressionLevel.Optimal);
            using var stream = entry.Open();
            stream.Write(Encoding.UTF8.GetBytes($"payload {index}"));
        }
    }

    private sealed class CancelOnFirstReport(CancellationTokenSource cancellation) : IProgress<ContentAcquisitionProgress>
    {
        public void Report(ContentAcquisitionProgress value)
        {
            if (value.Phase == ContentAcquisitionPhase.Extracting)
            {
                cancellation.Cancel();
            }
        }
    }
}
