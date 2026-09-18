using FluentAssertions;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Features.Content.Services.GenLauncher;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Content.GenLauncher;

/// <summary>
/// Unit tests for <see cref="GenLauncherDeliverer"/>.
/// </summary>
public sealed class GenLauncherDelivererTests
{
    private readonly Mock<IDownloadService> _downloadServiceMock = new();
    private readonly Mock<IContentManifestPool> _manifestPoolMock = new();

    /// <summary>
    /// Tests that CanDeliver returns true for GenLauncher manifests.
    /// </summary>
    [Fact]
    public void CanDeliver_WithGenLauncherPublisher_ReturnsTrue()
    {
        var deliverer = CreateDeliverer();
        var manifest = new ContentManifest
        {
            Id = ManifestId.Create("1.1.genlauncher.mod.test"),
            Name = "Test Mod",
            Version = "1.0",
            ContentType = ContentType.Mod,
            Publisher = new PublisherInfo { PublisherType = PublisherTypeConstants.GenLauncher },
        };

        var canDeliver = deliverer.CanDeliver(manifest);
        canDeliver.Should().BeTrue();
    }

    /// <summary>
    /// Tests that DeliverContentAsync fails when given an invalid download URL.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task DeliverContentAsync_WithInvalidDownloadUrl_ReturnsFailure()
    {
        var deliverer = CreateDeliverer();
        var targetDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        try
        {
            var manifest = new ContentManifest
            {
                Id = ManifestId.Create("1.1.genlauncher.mod.test"),
                Name = "Test Mod",
                Version = "1.0",
                ContentType = ContentType.Mod,
                Publisher = new PublisherInfo { PublisherType = PublisherTypeConstants.GenLauncher },
                Files =
                [
                    new ManifestFile
                    {
                        RelativePath = "test.zip",
                        DownloadUrl = "not-a-valid-uri",
                        SourceType = ContentSourceType.RemoteDownload,
                    },
                ],
            };

            var result = await deliverer.DeliverContentAsync(manifest, targetDir, null, CancellationToken.None);

            result.Success.Should().BeFalse();
            result.FirstError.Should().Contain("Invalid download URL");
        }
        finally
        {
            if (Directory.Exists(targetDir))
            {
                Directory.Delete(targetDir, recursive: true);
            }
        }
    }

    /// <summary>
    /// Tests that DeliverContentAsync blocks SSRF unsafe URLs (e.g. file:, ftp:, localhost).
    /// </summary>
    /// <param name="unsafeUrl">The SSRF unsafe download URL to test.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://attacker.com/payload.zip")]
    [InlineData("http://127.0.0.1/sensitive.zip")]
    [InlineData("http://localhost:8080/data.zip")]
    public async Task DeliverContentAsync_WithSsrfUnsafeUrl_RejectsWithoutDownloading(string unsafeUrl)
    {
        var deliverer = CreateDeliverer();
        var targetDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        try
        {
            var manifest = new ContentManifest
            {
                Id = ManifestId.Create("1.1.genlauncher.mod.ssrf"),
                Name = "SSRF Mod",
                Version = "1.0",
                ContentType = ContentType.Mod,
                Publisher = new PublisherInfo { PublisherType = PublisherTypeConstants.GenLauncher },
                Files =
                [
                    new ManifestFile
                    {
                        RelativePath = "test.zip",
                        DownloadUrl = unsafeUrl,
                        SourceType = ContentSourceType.RemoteDownload,
                    },
                ],
            };

            var result = await deliverer.DeliverContentAsync(manifest, targetDir, null, CancellationToken.None);

            result.Success.Should().BeFalse();
            result.FirstError.Should().Contain("Invalid download URL");
            _downloadServiceMock.Verify(
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
            if (Directory.Exists(targetDir))
            {
                Directory.Delete(targetDir, recursive: true);
            }
        }
    }

    /// <summary>
    /// Tests that DeliverContentAsync fails when download service fails.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task DeliverContentAsync_WhenDownloadFails_ReturnsFailure()
    {
        _downloadServiceMock.Setup(d => d.DownloadFileAsync(
            It.IsAny<Uri>(),
            It.IsAny<string>(),
            It.IsAny<string?>(),
            It.IsAny<IProgress<DownloadProgress>?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(DownloadResult.CreateFailure("Network timeout"));

        var deliverer = CreateDeliverer();
        var targetDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        try
        {
            var manifest = new ContentManifest
            {
                Id = ManifestId.Create("1.1.genlauncher.mod.test"),
                Name = "Test Mod",
                Version = "1.0",
                ContentType = ContentType.Mod,
                Publisher = new PublisherInfo { PublisherType = PublisherTypeConstants.GenLauncher },
                Files =
                [
                    new ManifestFile
                    {
                        RelativePath = "test.zip",
                        DownloadUrl = "https://example.com/test.zip",
                        SourceType = ContentSourceType.RemoteDownload,
                    },
                ],
            };

            var result = await deliverer.DeliverContentAsync(manifest, targetDir, null, CancellationToken.None);

            result.Success.Should().BeFalse();
            result.FirstError.Should().Contain("Network timeout");
        }
        finally
        {
            if (Directory.Exists(targetDir))
            {
                Directory.Delete(targetDir, recursive: true);
            }
        }
    }

    private GenLauncherDeliverer CreateDeliverer()
    {
        var factory = new GenLauncherManifestFactory(
            Mock.Of<IArchivePayloadProcessor>(),
            NullLogger<GenLauncherManifestFactory>.Instance);

        return new GenLauncherDeliverer(
            _downloadServiceMock.Object,
            _manifestPoolMock.Object,
            factory,
            NullLogger<GenLauncherDeliverer>.Instance);
    }
}
