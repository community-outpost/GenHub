using FluentAssertions;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Tools;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Features.Content.Services.ContentDeliverers;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Content.Services.ContentDeliverers;

/// <summary>
/// Unit tests for <see cref="HttpContentDeliverer"/>.
/// </summary>
public class HttpContentDelivererTests
{
    /// <summary>
    /// Verifies that delivery preserves the authoritative manifest and file metadata.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DeliverContentAsync_WithRemoteFile_PreservesManifestAndFileMetadataAsync()
    {
        var targetDirectory = CreateTargetDirectory();
        const string relativePath = "data/game.dat";
        const string expectedHash = "0123456789abcdef";
        var manifest = CreateManifest("generals-1.08-en", "1.08", relativePath, expectedHash);
        var downloadService = CreateSuccessfulDownloadService();
        var deliverer = CreateDeliverer(downloadService.Object);
        var expectedDestinationPath = Path.GetFullPath(relativePath, Path.GetFullPath(targetDirectory));

        try
        {
            var result = await deliverer.DeliverContentAsync(manifest, targetDirectory);

            result.Success.Should().BeTrue();
            result.Data.Should().BeSameAs(manifest);
            result.Data!.Id.Should().Be(manifest.Id);
            result.Data.Version.Should().Be("1.08");
            result.Data.Files.Should().ContainSingle();
            result.Data.Files[0].SourceType.Should().Be(ContentSourceType.RemoteDownload);
            result.Data.Files[0].Hash.Should().Be(expectedHash);
            result.Data.Files[0].Size.Should().Be(7);
            result.Data.Files[0].IsRequired.Should().BeFalse();
            result.Data.Files[0].InstallTarget.Should().Be(ContentInstallTarget.Workspace);
            File.Exists(expectedDestinationPath).Should().BeTrue();

            downloadService.Verify(
                d => d.DownloadFileAsync(
                    It.Is<DownloadConfiguration>(c =>
                        c.Url == new Uri("https://example.com/game.dat") &&
                        c.DestinationPath == expectedDestinationPath &&
                        c.ExpectedHash == expectedHash &&
                        c.PublisherId == CsvConstants.SourceName &&
                        c.Author == CsvConstants.SourceName &&
                        c.ContentName == "generals-1.08-en" &&
                        c.ContentId == manifest.Id.Value),
                    It.IsAny<IProgress<DownloadProgress>?>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
        finally
        {
            Directory.Delete(targetDirectory, recursive: true);
        }
    }

    /// <summary>
    /// Verifies that DBolical CDN download URLs are routed through <see cref="IPlaywrightService"/> instead of <see cref="IDownloadService"/>.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DeliverContentAsync_WithDBolicalUrl_RoutesThroughPlaywrightServiceAsync()
    {
        var targetDirectory = CreateTargetDirectory();
        const string relativePath = "mod.zip";
        const string dbolicalUrl = "https://fmt1.dl.dbolical.com/dl/2026/08/01/GeneralsUndone_v1.0.zip?st=token&e=123";
        var manifest = new ContentManifest
        {
            Id = new ManifestId("dbolical-mod"),
            Name = "DBolical Test Mod",
            Version = "1.0",
            Files =
            [
                new ManifestFile
                {
                    RelativePath = relativePath,
                    DownloadUrl = dbolicalUrl,
                    SourceType = ContentSourceType.RemoteDownload,
                }
            ],
        };

        var downloadService = new Mock<IDownloadService>(MockBehavior.Strict);
        var playwrightService = new Mock<IPlaywrightService>();
        var expectedDestinationPath = Path.GetFullPath(relativePath, Path.GetFullPath(targetDirectory));

        playwrightService
            .Setup(p => p.DownloadFileAsync(
                It.Is<DownloadConfiguration>(c => c.Url == new Uri(dbolicalUrl) && c.DestinationPath == expectedDestinationPath),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((DownloadConfiguration config, CancellationToken _) =>
            {
                File.WriteAllText(config.DestinationPath, "dbolical content");
                return DownloadResult.CreateSuccess(
                    config.DestinationPath,
                    16,
                    TimeSpan.FromMilliseconds(100),
                    hashVerified: false);
            });

        var deliverer = new HttpContentDeliverer(
            downloadService.Object,
            Mock.Of<ILogger<HttpContentDeliverer>>(),
            playwrightService.Object);

        try
        {
            var result = await deliverer.DeliverContentAsync(manifest, targetDirectory);

            result.Success.Should().BeTrue();
            File.Exists(expectedDestinationPath).Should().BeTrue();
            playwrightService.Verify(
                p => p.DownloadFileAsync(
                    It.Is<DownloadConfiguration>(c => c.Url == new Uri(dbolicalUrl)),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
        finally
        {
            Directory.Delete(targetDirectory, recursive: true);
        }
    }

    /// <summary>
    /// Verifies that repeated delivery calls return only their own manifest state.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DeliverContentAsync_CalledRepeatedly_DoesNotShareManifestStateAsync()
    {
        var targetDirectory = CreateTargetDirectory();
        var firstManifest = CreateManifest("generals-1.08-en", "1.08", "first.dat", "first-hash");
        var secondManifest = CreateManifest("zerohour-1.04-en", "1.04", "second.dat", "second-hash");
        var deliverer = CreateDeliverer(CreateSuccessfulDownloadService().Object);

        try
        {
            var firstResult = await deliverer.DeliverContentAsync(firstManifest, targetDirectory);
            var secondResult = await deliverer.DeliverContentAsync(secondManifest, targetDirectory);

            firstResult.Data.Should().BeSameAs(firstManifest);
            secondResult.Data.Should().BeSameAs(secondManifest);
            secondResult.Data!.Files.Should().ContainSingle(f => f.RelativePath == "second.dat");
            secondResult.Data.Files.Should().NotContain(f => f.RelativePath == "first.dat");
        }
        finally
        {
            Directory.Delete(targetDirectory, recursive: true);
        }
    }

    /// <summary>
    /// Verifies that ModDB download URLs are routed through <see cref="IPlaywrightService"/> instead of <see cref="IDownloadService"/>.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DeliverContentAsync_WithModDbUrl_RoutesThroughPlaywrightServiceAsync()
    {
        var targetDirectory = CreateTargetDirectory();
        const string relativePath = "mod.zip";
        var manifest = new ContentManifest
        {
            Id = new ManifestId("moddb-mod"),
            Name = "ModDB Test Mod",
            Version = "1.0",
            Files =
            [
                new ManifestFile
                {
                    RelativePath = relativePath,
                    DownloadUrl = "https://www.moddb.com/downloads/start/12345",
                    Hash = "expected-sha256-hash",
                    SourceType = ContentSourceType.RemoteDownload,
                }
            ],
        };

        var downloadService = new Mock<IDownloadService>(MockBehavior.Strict);
        var playwrightService = new Mock<IPlaywrightService>();
        var expectedDestinationPath = Path.GetFullPath(relativePath, Path.GetFullPath(targetDirectory));

        playwrightService
            .Setup(p => p.DownloadFileAsync(
                It.Is<DownloadConfiguration>(c => c.Url == new Uri("https://www.moddb.com/downloads/start/12345") && c.DestinationPath == expectedDestinationPath),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((DownloadConfiguration config, CancellationToken _) =>
            {
                File.WriteAllText(config.DestinationPath, "mod content");
                return DownloadResult.CreateSuccess(
                    config.DestinationPath,
                    11,
                    TimeSpan.FromMilliseconds(100),
                    hashVerified: false);
            });

        var deliverer = new HttpContentDeliverer(
            downloadService.Object,
            Mock.Of<ILogger<HttpContentDeliverer>>(),
            playwrightService.Object);

        try
        {
            var result = await deliverer.DeliverContentAsync(manifest, targetDirectory);

            result.Success.Should().BeTrue();
            File.Exists(expectedDestinationPath).Should().BeTrue();
            playwrightService.Verify(
                p => p.DownloadFileAsync(
                    It.Is<DownloadConfiguration>(c =>
                        c.Url == new Uri("https://www.moddb.com/downloads/start/12345") &&
                        c.ExpectedHash == "expected-sha256-hash"),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
        finally
        {
            Directory.Delete(targetDirectory, recursive: true);
        }
    }

    /// <summary>
    /// Verifies that ModDB download URLs fall back to <see cref="IDownloadService"/> when <see cref="IPlaywrightService"/> is null.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DeliverContentAsync_WithModDbUrlAndNullPlaywright_FallsBackToDownloadServiceAsync()
    {
        var targetDirectory = CreateTargetDirectory();
        const string relativePath = "fallback.zip";
        var manifest = new ContentManifest
        {
            Id = new ManifestId("moddb-mod-fallback"),
            Name = "ModDB Fallback Mod",
            Version = "1.0",
            Files =
            [
                new ManifestFile
                {
                    RelativePath = relativePath,
                    DownloadUrl = "https://www.moddb.com/downloads/start/99999",
                    SourceType = ContentSourceType.RemoteDownload,
                }
            ],
        };

        var downloadService = CreateSuccessfulDownloadService();
        var deliverer = new HttpContentDeliverer(
            downloadService.Object,
            Mock.Of<ILogger<HttpContentDeliverer>>(),
            playwrightService: null);
        var expectedDestinationPath = Path.GetFullPath(relativePath, Path.GetFullPath(targetDirectory));

        try
        {
            var result = await deliverer.DeliverContentAsync(manifest, targetDirectory);

            result.Success.Should().BeTrue();
            File.Exists(expectedDestinationPath).Should().BeTrue();
            downloadService.Verify(
                d => d.DownloadFileAsync(
                    It.Is<DownloadConfiguration>(c =>
                        c.Url == new Uri("https://www.moddb.com/downloads/start/99999") &&
                        c.DestinationPath == expectedDestinationPath),
                    It.IsAny<IProgress<DownloadProgress>?>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
        finally
        {
            Directory.Delete(targetDirectory, recursive: true);
        }
    }

    /// <summary>
    /// Verifies that insecure HTTP ModDB or DBolical download URLs fail and enforce HTTPS.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DeliverContentAsync_WithInsecureHttpModDbUrl_FailsWithHttpsRequirementAsync()
    {
        var targetDirectory = CreateTargetDirectory();
        const string relativePath = "insecure.zip";
        var manifest = new ContentManifest
        {
            Id = new ManifestId("moddb-insecure-mod"),
            Name = "ModDB Insecure Mod",
            Version = "1.0",
            Files =
            [
                new ManifestFile
                {
                    RelativePath = relativePath,
                    DownloadUrl = "http://www.moddb.com/downloads/start/99999",
                    SourceType = ContentSourceType.RemoteDownload,
                },
            ],
        };

        var deliverer = new HttpContentDeliverer(
            Mock.Of<IDownloadService>(),
            Mock.Of<ILogger<HttpContentDeliverer>>(),
            Mock.Of<IPlaywrightService>());

        try
        {
            var result = await deliverer.DeliverContentAsync(manifest, targetDirectory);

            result.Success.Should().BeFalse();
            result.FirstError.Should().Contain("ModDB and DBolical downloads must use HTTPS.");
        }
        finally
        {
            Directory.Delete(targetDirectory, recursive: true);
        }
    }

    /// <summary>
    /// Verifies that <see cref="HttpContentDeliverer.ValidateContentAsync"/> rejects insecure HTTP ModDB or DBolical URLs.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task ValidateContentAsync_WithInsecureHttpModDbUrl_ReturnsFalseAsync()
    {
        var manifest = new ContentManifest
        {
            Id = new ManifestId("moddb-insecure-validate"),
            Name = "ModDB Insecure Validate",
            Version = "1.0",
            Files =
            [
                new ManifestFile
                {
                    RelativePath = "insecure.zip",
                    DownloadUrl = "http://www.moddb.com/downloads/start/99999",
                    SourceType = ContentSourceType.RemoteDownload,
                    IsRequired = true,
                },
            ],
        };

        var deliverer = new HttpContentDeliverer(
            Mock.Of<IDownloadService>(),
            Mock.Of<ILogger<HttpContentDeliverer>>(),
            Mock.Of<IPlaywrightService>());

        var result = await deliverer.ValidateContentAsync(manifest);

        result.Success.Should().BeTrue();
        result.Data.Should().BeFalse();
    }

    /// <summary>
    /// Verifies that <see cref="HttpContentDeliverer.ValidateContentAsync"/> accepts secure HTTPS ModDB or DBolical URLs.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task ValidateContentAsync_WithSecureHttpsModDbUrl_ReturnsTrueAsync()
    {
        var manifest = new ContentManifest
        {
            Id = new ManifestId("moddb-secure-validate"),
            Name = "ModDB Secure Validate",
            Version = "1.0",
            Files =
            [
                new ManifestFile
                {
                    RelativePath = "secure.zip",
                    DownloadUrl = "https://www.moddb.com/downloads/start/99999",
                    SourceType = ContentSourceType.RemoteDownload,
                    IsRequired = true,
                },
            ],
        };

        var deliverer = new HttpContentDeliverer(
            Mock.Of<IDownloadService>(),
            Mock.Of<ILogger<HttpContentDeliverer>>(),
            Mock.Of<IPlaywrightService>());

        var result = await deliverer.ValidateContentAsync(manifest);

        result.Success.Should().BeTrue();
        result.Data.Should().BeTrue();
    }

    /// <summary>
    /// Verifies that user cancellation remains an <see cref="OperationCanceledException"/>.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DeliverContentAsync_WhenCancelled_PropagatesCancellationAsync()
    {
        var targetDirectory = CreateTargetDirectory();
        var manifest = CreateManifest("generals-1.08-en", "1.08", "game.dat", "hash");
        var deliverer = CreateDeliverer(Mock.Of<IDownloadService>());
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                deliverer.DeliverContentAsync(
                    manifest,
                    targetDirectory,
                    cancellationToken: cancellationSource.Token));
        }
        finally
        {
            Directory.Delete(targetDirectory, recursive: true);
        }
    }

    /// <summary>
    /// Verifies that a manifest file cannot escape the delivery target directory.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DeliverContentAsync_WithEscapingPath_ReturnsFailureAsync()
    {
        var rootDirectory = CreateTargetDirectory();
        var targetDirectory = Path.Combine(rootDirectory, "target");
        Directory.CreateDirectory(targetDirectory);
        var manifest = CreateManifest("generals-1.08-en", "1.08", "../escaped.dat", "hash");
        var downloadService = new Mock<IDownloadService>();
        var deliverer = CreateDeliverer(downloadService.Object);

        try
        {
            var result = await deliverer.DeliverContentAsync(manifest, targetDirectory);

            result.Success.Should().BeFalse();
            result.FirstError.Should().Contain("resolves outside target directory");
            File.Exists(Path.Combine(rootDirectory, "escaped.dat")).Should().BeFalse();
            downloadService.Verify(
                d => d.DownloadFileAsync(
                    It.IsAny<DownloadConfiguration>(),
                    It.IsAny<IProgress<DownloadProgress>?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
        finally
        {
            Directory.Delete(rootDirectory, recursive: true);
        }
    }

    /// <summary>
    /// Verifies that <see cref="HttpContentDeliverer.DeliverContentAsync"/> passes an <see cref="IProgress{DownloadProgress}"/>
    /// to <see cref="IDownloadService"/> and forwards progress updates including speed and percentage.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DeliverContentAsync_WithProgress_ReportsDownloadSpeedAndProgressAsync()
    {
        var targetDirectory = CreateTargetDirectory();
        var manifest = CreateManifest("test-content", "1.0", "file.zip", "test-hash");
        var reportedProgress = new List<ContentAcquisitionProgress>();
        var progressReported = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var progressMock = new Mock<IProgress<ContentAcquisitionProgress>>();
        progressMock
            .Setup(p => p.Report(It.IsAny<ContentAcquisitionProgress>()))
            .Callback<ContentAcquisitionProgress>(progress =>
            {
                reportedProgress.Add(progress);
                if (progress.Phase == ContentAcquisitionPhase.Downloading &&
                    progress.CurrentOperation.Contains("/s", StringComparison.Ordinal) &&
                    progress.ProgressPercentage > 0)
                {
                    progressReported.TrySetResult(true);
                }
            });

        var downloadService = new Mock<IDownloadService>();
        downloadService
            .Setup(d => d.DownloadFileAsync(
                It.IsAny<DownloadConfiguration>(),
                It.IsAny<IProgress<DownloadProgress>?>(),
                It.IsAny<CancellationToken>()))
            .Returns((DownloadConfiguration config, IProgress<DownloadProgress>? fileProgress, CancellationToken _) =>
            {
                fileProgress?.Report(new DownloadProgress(
                    bytesReceived: 512,
                    totalBytes: 1024,
                    fileName: "file.zip",
                    url: config.Url,
                    bytesPerSecond: 5 * 1024 * 1024));
                File.WriteAllText(config.DestinationPath, "content");
                return Task.FromResult(DownloadResult.CreateSuccess(
                    config.DestinationPath,
                    new FileInfo(config.DestinationPath).Length,
                    TimeSpan.FromMilliseconds(1),
                    hashVerified: true));
            });

        var deliverer = CreateDeliverer(downloadService.Object);

        try
        {
            var result = await deliverer.DeliverContentAsync(manifest, targetDirectory, progressMock.Object);

            result.Success.Should().BeTrue();
            await Task.WhenAny(progressReported.Task, Task.Delay(5000));
            reportedProgress.Should().Contain(p =>
                p.Phase == ContentAcquisitionPhase.Downloading &&
                p.CurrentOperation.Contains("/s", StringComparison.Ordinal) &&
                p.ProgressPercentage > 0);
        }
        finally
        {
            Directory.Delete(targetDirectory, recursive: true);
        }
    }

    /// <summary>
    /// Verifies that archive downloads invoke the archive payload processor.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DeliverContentAsync_WithArchive_InvokesArchiveProcessorAsync()
    {
        var targetDirectory = CreateTargetDirectory();
        var manifest = CreateManifest("archive-mod", "1.0", "mod.zip", "test-hash");
        var downloadService = CreateSuccessfulDownloadService();
        var archiveProcessorMock = new Mock<IArchivePayloadProcessor>();
        var deliverer = new HttpContentDeliverer(
            downloadService.Object,
            Mock.Of<ILogger<HttpContentDeliverer>>(),
            archivePayloadProcessor: archiveProcessorMock.Object);

        try
        {
            var result = await deliverer.DeliverContentAsync(manifest, targetDirectory);

            result.Success.Should().BeTrue();
            archiveProcessorMock.Verify(
                a => a.ExtractArchivesSafelyAsync(
                    targetDirectory,
                    manifest.ContentType,
                    It.IsAny<IProgress<ContentAcquisitionProgress>?>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
        finally
        {
            Directory.Delete(targetDirectory, recursive: true);
        }
    }

    private static HttpContentDeliverer CreateDeliverer(IDownloadService downloadService) =>
        new(downloadService, Mock.Of<ILogger<HttpContentDeliverer>>());

    private static Mock<IDownloadService> CreateSuccessfulDownloadService()
    {
        var downloadService = new Mock<IDownloadService>();
        downloadService
            .Setup(d => d.DownloadFileAsync(
                It.IsAny<DownloadConfiguration>(),
                It.IsAny<IProgress<DownloadProgress>?>(),
                It.IsAny<CancellationToken>()))
            .Returns((DownloadConfiguration config, IProgress<DownloadProgress>? _, CancellationToken _) =>
            {
                File.WriteAllText(config.DestinationPath, "content");
                return Task.FromResult(DownloadResult.CreateSuccess(
                    config.DestinationPath,
                    new FileInfo(config.DestinationPath).Length,
                    TimeSpan.FromMilliseconds(1),
                    hashVerified: true));
            });

        return downloadService;
    }

    private static ContentManifest CreateManifest(
        string contentName,
        string version,
        string relativePath,
        string hash)
    {
        var manifestId = ManifestIdGenerator.GeneratePublisherContentId(
            PublisherTypeConstants.CsvRegistry,
            ContentType.GameInstallation,
            contentName);

        return new ContentManifest
        {
            Id = new ManifestId(manifestId),
            Name = contentName,
            Version = version,
            ContentType = ContentType.GameInstallation,
            TargetGame = GameType.Generals,
            OriginalProviderName = CsvConstants.SourceName,
            OriginalContentId = contentName,
            Files =
            [
                new ManifestFile
                {
                    RelativePath = relativePath,
                    SourceType = ContentSourceType.RemoteDownload,
                    InstallTarget = ContentInstallTarget.Workspace,
                    Size = 7,
                    Hash = hash,
                    DownloadUrl = "https://example.com/game.dat",
                    IsRequired = false,
                    IsExecutable = true,
                    Permissions = new FilePermissions { UnixPermissions = "755" },
                },
            ],
        };
    }

    private static string CreateTargetDirectory()
    {
        var targetDirectory = Path.Combine(
            Path.GetTempPath(),
            nameof(HttpContentDelivererTests),
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(targetDirectory);
        return targetDirectory;
    }
}
