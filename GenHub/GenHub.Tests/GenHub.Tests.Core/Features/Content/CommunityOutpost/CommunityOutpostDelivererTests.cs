using System;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Features.Content.Services.CommunityOutpost;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Content.CommunityOutpost;

/// <summary>
/// Unit tests for <see cref="CommunityOutpostDeliverer"/>.
/// </summary>
public sealed class CommunityOutpostDelivererTests
{
    /// <summary>
    /// Verifies that ValidateContentAsync succeeds when a manifest contains a valid archive file.
    /// </summary>
    /// <returns>A task that completes when the operation finishes.</returns>
    [Fact]
    public async Task ValidateContentAsync_ValidDatArchive_ReturnsSuccessAsync()
    {
        // Arrange
        var downloadService = new Mock<IDownloadService>();
        var converter = new CompressedImageToTgaConverter(NullLogger<CompressedImageToTgaConverter>.Instance);
        var deliverer = new CommunityOutpostDeliverer(
            downloadService.Object,
            converter,
            NullLogger<CommunityOutpostDeliverer>.Instance);

        var manifest = new ContentManifest
        {
            Id = ManifestId.Create("1.0.communityoutpost.addon.hlen"),
            Name = "Hotkeys Indicators",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            Publisher = new PublisherInfo { PublisherType = CommunityOutpostConstants.PublisherType },
            Files =
            [
                new ManifestFile
                {
                    RelativePath = "hlen.dat",
                    DownloadUrl = "https://legi.cc/gp2/f/hlen.dat",
                },
            ],
        };

        // Act
        var result = await deliverer.ValidateContentAsync(manifest, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.True(result.Data);
    }

    /// <summary>
    /// Verifies that DeliverContentAsync falls back to the /gp2/f/ endpoint when the primary /patch/ URL fails.
    /// </summary>
    /// <returns>A task that completes when the operation finishes.</returns>
    [Fact]
    public async Task DeliverContentAsync_PatchUrlFailure_FallsBackToGp2FilesEndpointAsync()
    {
        // Arrange
        var tempDirectory = Path.Combine(Path.GetTempPath(), "GenHubTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        try
        {
            var downloadService = new Mock<IDownloadService>();
            var converter = new CompressedImageToTgaConverter(NullLogger<CompressedImageToTgaConverter>.Instance);
            var deliverer = new CommunityOutpostDeliverer(
                downloadService.Object,
                converter,
                NullLogger<CommunityOutpostDeliverer>.Instance);

            var manifest = new ContentManifest
            {
                Id = ManifestId.Create("1.0.communityoutpost.addon.gent"),
                Name = "GenTool",
                ContentType = ContentType.Addon,
                TargetGame = GameType.ZeroHour,
                Publisher = new PublisherInfo { PublisherType = CommunityOutpostConstants.PublisherType },
                Metadata = new ContentMetadata { Tags = ["contentCode:gent"] },
                Files =
                [
                    new ManifestFile
                    {
                        RelativePath = "gent.zip",
                        DownloadUrl = "https://legi.cc/patch/gent.zip",
                    },
                ],
            };

            // Create a small valid zip file for the fallback download
            var validZipBytes = CreateDummyZipArchive();

            downloadService
                .Setup(d => d.DownloadFileAsync(
                    new Uri("https://legi.cc/patch/gent.zip"),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<IProgress<DownloadProgress>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(DownloadResult.CreateFailure("404 Not Found"));

            downloadService
                .Setup(d => d.DownloadFileAsync(
                     new Uri("https://legi.cc/gp2/f/gent.dat"),
                     It.IsAny<string>(),
                     It.IsAny<string>(),
                     It.IsAny<IProgress<DownloadProgress>>(),
                     It.IsAny<CancellationToken>()))
                .Callback<Uri, string, string?, IProgress<DownloadProgress>?, CancellationToken>((_, dest, _, _, _) => File.WriteAllBytes(dest, validZipBytes))
                .ReturnsAsync(DownloadResult.CreateSuccess("content.zip", validZipBytes.Length, TimeSpan.FromMilliseconds(100)));

            // Act
            var result = await deliverer.DeliverContentAsync(manifest, tempDirectory, null, CancellationToken.None);

            // Assert
            Assert.True(result.Success, result.FirstError);
            downloadService.Verify(
                d => d.DownloadFileAsync(
                    new Uri("https://legi.cc/gp2/f/gent.dat"),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<IProgress<DownloadProgress>>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                try
                {
                    Directory.Delete(tempDirectory, recursive: true);
                }
                catch
                {
                }
            }
        }
    }

    /// <summary>
    /// Verifies that when the primary download URL returns an HTML error page with HTTP 200,
    /// DeliverContentAsync rejects the HTML and successfully falls back to a valid archive URL.
    /// </summary>
    /// <returns>A task that completes when the operation finishes.</returns>
    [Fact]
    public async Task DeliverContentAsync_HtmlResponseOnPrimaryUrl_RejectsHtmlAndFallsBackToArchiveAsync()
    {
        // Arrange
        var tempDirectory = Path.Combine(Path.GetTempPath(), "GenHubTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        try
        {
            var downloadService = new Mock<IDownloadService>();
            var converter = new CompressedImageToTgaConverter(NullLogger<CompressedImageToTgaConverter>.Instance);
            var deliverer = new CommunityOutpostDeliverer(
                downloadService.Object,
                converter,
                NullLogger<CommunityOutpostDeliverer>.Instance);

            var manifest = new ContentManifest
            {
                Id = ManifestId.Create("1.20260802.communityoutpost.gameclient.communitypatch"),
                Name = "Community Patch",
                ContentType = ContentType.GameClient,
                TargetGame = GameType.ZeroHour,
                Publisher = new PublisherInfo { PublisherType = CommunityOutpostConstants.PublisherType },
                Metadata = new ContentMetadata { Tags = ["contentCode:community-patch"] },
                Files =
                [
                    new ManifestFile
                    {
                        RelativePath = "community-patch.zip",
                        DownloadUrl = "https://legi.cc/gp2/f/community-patch.zip",
                    },
                ],
            };

            var validZipBytes = CreateDummyZipArchive();
            var htmlErrorBytes = System.Text.Encoding.UTF8.GetBytes("<!DOCTYPE html><html><body>404 Not Found</body></html>");

            // Primary URL writes HTML error body
            downloadService
                .Setup(d => d.DownloadFileAsync(
                    new Uri("https://legi.cc/gp2/f/community-patch.zip"),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<IProgress<DownloadProgress>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<Uri, string, string?, IProgress<DownloadProgress>?, CancellationToken>((_, dest, _, _, _) => File.WriteAllBytes(dest, htmlErrorBytes))
                .ReturnsAsync(DownloadResult.CreateSuccess("content.zip", htmlErrorBytes.Length, TimeSpan.FromMilliseconds(50)));

            // Fallback GitHub URL writes valid zip archive
            downloadService
                .Setup(d => d.DownloadFileAsync(
                    It.Is<Uri>(u => u.Host.Contains("github.com")),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<IProgress<DownloadProgress>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<Uri, string, string?, IProgress<DownloadProgress>?, CancellationToken>((_, dest, _, _, _) => File.WriteAllBytes(dest, validZipBytes))
                .ReturnsAsync(DownloadResult.CreateSuccess("content.zip", validZipBytes.Length, TimeSpan.FromMilliseconds(100)));

            // Act
            var result = await deliverer.DeliverContentAsync(manifest, tempDirectory, null, CancellationToken.None);

            // Assert
            Assert.True(result.Success, result.FirstError);
            downloadService.Verify(
                d => d.DownloadFileAsync(
                    It.Is<Uri>(u => u.Host.Contains("github.com")),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<IProgress<DownloadProgress>>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                try
                {
                    Directory.Delete(tempDirectory, recursive: true);
                }
                catch
                {
                }
            }
        }
    }

    /// <summary>
    /// Verifies that when delivering content resolved from a generic catalog without explicit manifest dependencies,
    /// DeliverContentAsync discovers and merges registry-defined AutoInstall dependencies (such as hlen for hleg).
    /// </summary>
    /// <returns>A task that completes when the operation finishes.</returns>
    [Fact]
    public async Task DeliverContentAsync_GenericCatalogHotkeysWithoutExplicitDependencies_ProcessesAndMergesIndicatorsDependencyAsync()
    {
        // Arrange
        var tempDirectory = Path.Combine(Path.GetTempPath(), "GenHubTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        try
        {
            var downloadService = new Mock<IDownloadService>();
            var converter = new CompressedImageToTgaConverter(NullLogger<CompressedImageToTgaConverter>.Instance);
            var deliverer = new CommunityOutpostDeliverer(
                downloadService.Object,
                converter,
                NullLogger<CommunityOutpostDeliverer>.Instance);

            var manifest = new ContentManifest
            {
                Id = ManifestId.Create("1.20260701.communityoutpost.addon.hleg"),
                Name = "Legionnaire's Hotkeys",
                ContentType = ContentType.Addon,
                TargetGame = GameType.ZeroHour,
                Publisher = new PublisherInfo { PublisherType = CommunityOutpostConstants.PublisherType },
                Metadata = new ContentMetadata { Tags = ["contentCode:hleg"] },
                Dependencies = [],
                Files =
                [
                    new ManifestFile
                    {
                        RelativePath = "hleg.dat",
                        DownloadUrl = "https://legi.cc/gp2/f/hleg.dat",
                    },
                ],
            };

            var validZipBytes = CreateDummyZipArchive();

            downloadService
                .Setup(d => d.DownloadFileAsync(
                    It.Is<Uri>(u => u.ToString().Contains("hleg")),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<IProgress<DownloadProgress>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<Uri, string, string?, IProgress<DownloadProgress>?, CancellationToken>((_, dest, _, _, _) => File.WriteAllBytes(dest, validZipBytes))
                .ReturnsAsync(DownloadResult.CreateSuccess("hleg.dat", validZipBytes.Length, TimeSpan.FromMilliseconds(50)));

            downloadService
                .Setup(d => d.DownloadFileAsync(
                    It.Is<Uri>(u => u.ToString().Contains("hlen")),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<IProgress<DownloadProgress>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<Uri, string, string?, IProgress<DownloadProgress>?, CancellationToken>((_, dest, _, _, _) => File.WriteAllBytes(dest, validZipBytes))
                .ReturnsAsync(DownloadResult.CreateSuccess("hlen.dat", validZipBytes.Length, TimeSpan.FromMilliseconds(50)));

            downloadService
                .Setup(d => d.DownloadFileAsync(
                    It.Is<Uri>(u => u.ToString().Contains("gent")),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<IProgress<DownloadProgress>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<Uri, string, string?, IProgress<DownloadProgress>?, CancellationToken>((_, dest, _, _, _) => File.WriteAllBytes(dest, validZipBytes))
                .ReturnsAsync(DownloadResult.CreateSuccess("gent.dat", validZipBytes.Length, TimeSpan.FromMilliseconds(50)));

            // Act
            var result = await deliverer.DeliverContentAsync(manifest, tempDirectory, null, CancellationToken.None);

            // Assert
            Assert.True(result.Success, result.FirstError);
            downloadService.Verify(
                d => d.DownloadFileAsync(
                    It.Is<Uri>(u => u.ToString().Contains("hlen.dat")),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<IProgress<DownloadProgress>>(),
                    It.IsAny<CancellationToken>()),
                Times.AtLeastOnce);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                try
                {
                    Directory.Delete(tempDirectory, recursive: true);
                }
                catch
                {
                }
            }
        }
    }

    private static byte[] CreateDummyZipArchive()
    {
        using var memoryStream = new MemoryStream();
        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, true))
        {
            var entry = archive.CreateEntry("dummy.txt");
            using var entryStream = entry.Open();
            using var writer = new StreamWriter(entryStream);
            writer.Write("test payload");
        }

        return memoryStream.ToArray();
    }
}
