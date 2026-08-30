using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Features.Content.Services.Publishers;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Content.Publishers;

/// <summary>
/// Unit tests for <see cref="GitHubManifestFactory"/>.
/// </summary>
public sealed class GitHubManifestFactoryTests : IDisposable
{
    private readonly string _stagingDirectory = Path.Combine(Path.GetTempPath(), "GenHubTests", Guid.NewGuid().ToString("N"));

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Directory.Exists(_stagingDirectory))
        {
            Directory.Delete(_stagingDirectory, recursive: true);
        }
    }

    /// <summary>
    /// Verifies that archive payload delivered to staging is extracted and processed into manifest files.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task CreateManifestsFromExtractedContentAsync_ZipArchiveDelivered_ExtractsAndCreatesManifestAsync()
    {
        // Arrange
        Directory.CreateDirectory(_stagingDirectory);
        var archivePath = Path.Combine(_stagingDirectory, "LemonControlBar1080p.zip");
        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            await using var writer = new StreamWriter(archive.CreateEntry("340_ControlBarPro1080ZH.big").Open());
            await writer.WriteAsync("dummy big payload");
        }

        var hashProviderMock = new Mock<IFileHashProvider>();
        hashProviderMock.Setup(h => h.ComputeFileHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("dummyhash123");

        var payloadProcessor = new GenHub.Features.Content.Services.Common.ArchivePayloadProcessor(
            NullLogger<GenHub.Features.Content.Services.Common.ArchivePayloadProcessor>.Instance);

        var factory = new GitHubManifestFactory(
            NullLogger<GitHubManifestFactory>.Instance,
            hashProviderMock.Object,
            payloadProcessor);

        var originalManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.0.github.addon.controlbarprolemoneditionzh"),
            Name = "Control Bar Pro Lemon Edition ZH (1080p)",
            Version = "1.0",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            Publisher = new PublisherInfo
            {
                PublisherType = "github",
                Name = "L3-M",
                Website = "https://github.com/L3-M/GeneralsControlBar",
            },
        };

        // Act
        var manifests = await factory.CreateManifestsFromExtractedContentAsync(originalManifest, _stagingDirectory);

        // Assert
        Assert.Single(manifests);
        var resultManifest = manifests[0];
        Assert.False(File.Exists(archivePath), "The zip archive should have been unpacked and deleted.");
        Assert.Single(resultManifest.Files);
        Assert.Equal("340_ControlBarPro1080ZH.big", resultManifest.Files[0].RelativePath);
    }

    /// <summary>
    /// Verifies that Control Bar payload with nested folders is repacked into SAGE BIG archives by GitHubManifestFactory.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task CreateManifestsFromExtractedContentAsync_ControlBarNestedStructure_RepacksIntoBigArchivesAsync()
    {
        // Arrange
        Directory.CreateDirectory(_stagingDirectory);
        var archivePath = Path.Combine(_stagingDirectory, "cbpr.zip");
        {
            using var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create);
            {
                await using var writer1 = new StreamWriter(archive.CreateEntry("ZH/1080p/BIG/Window/ControlBarPro.wnd").Open());
                await writer1.WriteAsync("wnd data");
            }

            {
                await using var writer2 = new StreamWriter(archive.CreateEntry("ZH/1080p/BIG/Art/test.tga").Open());
                await writer2.WriteAsync("art data");
            }
        }

        var hashProviderMock = new Mock<IFileHashProvider>();
        hashProviderMock.Setup(h => h.ComputeFileHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("dummyhash123");

        var converter = new GenHub.Features.Content.Services.CommunityOutpost.CompressedImageToTgaConverter(
            NullLogger<GenHub.Features.Content.Services.CommunityOutpost.CompressedImageToTgaConverter>.Instance);
        var processor = new GenHub.Features.Content.Services.Common.ControlBarPackageProcessor(
            converter,
            NullLogger<GenHub.Features.Content.Services.Common.ControlBarPackageProcessor>.Instance);

        var payloadProcessor = new GenHub.Features.Content.Services.Common.ArchivePayloadProcessor(
            NullLogger<GenHub.Features.Content.Services.Common.ArchivePayloadProcessor>.Instance);

        var factory = new GitHubManifestFactory(
            NullLogger<GitHubManifestFactory>.Instance,
            hashProviderMock.Object,
            payloadProcessor,
            processor);

        var originalManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.103.github.addon.lemoncontrolbar1080p"),
            Name = "Control Bar Pro Lemon Edition ZH (1080p)",
            Version = "1.3",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            Publisher = new PublisherInfo
            {
                PublisherType = "github",
                Name = "L3-M",
                Website = "https://github.com/L3-M/GeneralsControlBar",
            },
        };

        // Act
        var manifests = await factory.CreateManifestsFromExtractedContentAsync(originalManifest, _stagingDirectory);

        // Assert
        Assert.Single(manifests);
        var resultManifest = manifests[0];
        Assert.Contains(resultManifest.Files, f => f.RelativePath == "340_ControlBarProArt1080ZH.big");
        Assert.Contains(resultManifest.Files, f => f.RelativePath == "340_ControlBarProData1080ZH.big");
        Assert.Contains(resultManifest.Files, f => f.RelativePath == "340_ControlBarProZH.big");
    }
}
