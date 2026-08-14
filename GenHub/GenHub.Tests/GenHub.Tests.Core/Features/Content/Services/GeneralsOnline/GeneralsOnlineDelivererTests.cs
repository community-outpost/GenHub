using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Results;
using GenHub.Features.Content.Services.GeneralsOnline;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Content.Services.GeneralsOnline;

/// <summary>
/// Unit tests for <see cref="GeneralsOnlineDeliverer"/>.
/// </summary>
public class GeneralsOnlineDelivererTests : IDisposable
{
    private readonly Mock<IDownloadService> _downloadServiceMock;
    private readonly Mock<IContentManifestPool> _manifestPoolMock;
    private readonly Mock<IProviderDefinitionLoader> _providerLoaderMock;
    private readonly GeneralsOnlineManifestFactory _manifestFactory;
    private readonly GeneralsOnlineDeliverer _deliverer;
    private readonly string _tempDir;

    /// <summary>
    /// Initializes a new instance of the <see cref="GeneralsOnlineDelivererTests"/> class.
    /// </summary>
    public GeneralsOnlineDelivererTests()
    {
        _downloadServiceMock = new Mock<IDownloadService>();
        _manifestPoolMock = new Mock<IContentManifestPool>();
        _providerLoaderMock = new Mock<IProviderDefinitionLoader>();

        _providerLoaderMock
            .Setup(l => l.GetProvider(PublisherTypeConstants.GeneralsOnline))
            .Returns(new ProviderDefinition
            {
                ProviderId = PublisherTypeConstants.GeneralsOnline,
                PublisherType = PublisherTypeConstants.GeneralsOnline,
                Endpoints = new ProviderEndpoints
                {
                    WebsiteUrl = "https://example.com/go",
                },
            });

        _manifestFactory = new GeneralsOnlineManifestFactory(
            NullLogger<GeneralsOnlineManifestFactory>.Instance,
            _providerLoaderMock.Object);

        _deliverer = new GeneralsOnlineDeliverer(
            _downloadServiceMock.Object,
            _manifestPoolMock.Object,
            _manifestFactory,
            NullLogger<GeneralsOnlineDeliverer>.Instance);

        _tempDir = Path.Combine(Path.GetTempPath(), "GenHub_GODelivererTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    /// <summary>
    /// Cleans up test artifacts.
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Verifies CanDeliver returns true for GeneralsOnline manifests with zip downloads.
    /// </summary>
    [Fact]
    public void CanDeliver_ValidGeneralsOnlineManifest_ReturnsTrue()
    {
        var manifest = new ContentManifest
        {
            Publisher = new PublisherInfo
            {
                Name = GeneralsOnlineConstants.PublisherName,
                PublisherType = PublisherTypeConstants.GeneralsOnline,
            },
            Files =
            [
                new ManifestFile
                {
                    DownloadUrl = "https://example.com/GeneralsOnline_101525_QFE5.zip",
                    SourceType = ContentSourceType.RemoteDownload,
                },
            ],
        };

        Assert.True(_deliverer.CanDeliver(manifest));
    }

    /// <summary>
    /// Verifies CanDeliver returns false for other publishers.
    /// </summary>
    [Fact]
    public void CanDeliver_OtherPublisher_ReturnsFalse()
    {
        var manifest = new ContentManifest
        {
            Publisher = new PublisherInfo { PublisherType = "other-publisher" },
            Files =
            [
                new ManifestFile
                {
                    DownloadUrl = "https://example.com/other.zip",
                    SourceType = ContentSourceType.RemoteDownload,
                },
            ],
        };

        Assert.False(_deliverer.CanDeliver(manifest));
    }

    /// <summary>
    /// Verifies DeliverContentAsync fails when any manifest registration in pool fails.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task DeliverContentAsync_WhenManifestRegistrationFails_ReturnsFailure()
    {
        // Arrange
        var zipPath = Path.Combine(_tempDir, "test.zip");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("generalsonlinezh_60.exe");
            using (var writer = new StreamWriter(entry.Open()))
            {
                writer.Write("fake content");
            }

            var gameDataEntry = archive.CreateEntry("GeneralsOnlineGameData/splash.bmp");
            using (var writer = new StreamWriter(gameDataEntry.Open()))
            {
                writer.Write("fake splash");
            }
        }

        _downloadServiceMock
            .Setup(d => d.DownloadFileAsync(
                It.IsAny<Uri>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<IProgress<DownloadProgress>?>(),
                It.IsAny<CancellationToken>()))
            .Callback<Uri, string, string?, IProgress<DownloadProgress>?, CancellationToken>((url, path, hash, prog, token) =>
            {
                File.Copy(zipPath, path, true);
            })
            .ReturnsAsync(DownloadResult.CreateSuccess(zipPath, 100, TimeSpan.FromSeconds(1)));

        var manifest = new ContentManifest
        {
            Id = ManifestId.Create("1.1015255.generalsonline.gameclient.60hz"),
            Name = GameClientConstants.GeneralsOnline60HzDisplayName,
            Version = "101525_QFE5",
            ContentType = ContentType.GameClient,
            Publisher = new PublisherInfo { PublisherType = PublisherTypeConstants.GeneralsOnline },
            Files =
            [
                new ManifestFile
                {
                    DownloadUrl = "https://example.com/GeneralsOnline_101525_QFE5.zip",
                    SourceType = ContentSourceType.RemoteDownload,
                },
            ],
        };

        // First manifest registration succeeds, second fails
        var callCount = 0;
        _manifestPoolMock
            .Setup(p => p.AddManifestAsync(
                It.IsAny<ContentManifest>(),
                It.IsAny<string>(),
                It.IsAny<IProgress<ContentStorageProgress>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                return callCount == 1
                    ? OperationResult<bool>.CreateSuccess(true)
                    : OperationResult<bool>.CreateFailure("Simulated pool registration failure");
            });

        // Act
        var targetDir = Path.Combine(_tempDir, "delivery");
        Directory.CreateDirectory(targetDir);
        var result = await _deliverer.DeliverContentAsync(manifest, targetDir, null, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Simulated pool registration failure", result.FirstError);
    }
}
