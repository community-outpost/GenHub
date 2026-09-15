using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Tools;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Features.Content.Services.ContentDeliverers;
using Microsoft.Extensions.Logging;
using Moq;
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
                    new Uri("https://example.com/game.dat"),
                    expectedDestinationPath,
                    expectedHash,
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
                    new Uri("https://www.moddb.com/downloads/start/99999"),
                    expectedDestinationPath,
                    It.IsAny<string?>(),
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
                    It.IsAny<Uri>(),
                    It.IsAny<string>(),
                    It.IsAny<string?>(),
                    It.IsAny<IProgress<DownloadProgress>?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
        finally
        {
            Directory.Delete(rootDirectory, recursive: true);
        }
    }

    private static HttpContentDeliverer CreateDeliverer(IDownloadService downloadService) =>
        new(downloadService, Mock.Of<ILogger<HttpContentDeliverer>>());

    private static Mock<IDownloadService> CreateSuccessfulDownloadService()
    {
        var downloadService = new Mock<IDownloadService>();
        downloadService
            .Setup(d => d.DownloadFileAsync(
                It.IsAny<Uri>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<IProgress<DownloadProgress>?>(),
                It.IsAny<CancellationToken>()))
            .Returns((Uri _, string destinationPath, string? _, IProgress<DownloadProgress>? _, CancellationToken _) =>
            {
                File.WriteAllText(destinationPath, "content");
                return Task.FromResult(DownloadResult.CreateSuccess(
                    destinationPath,
                    new FileInfo(destinationPath).Length,
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
