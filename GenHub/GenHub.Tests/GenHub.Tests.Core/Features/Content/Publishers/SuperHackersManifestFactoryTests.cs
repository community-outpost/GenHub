using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
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
/// Unit tests for <see cref="SuperHackersManifestFactory"/>.
/// </summary>
public sealed class SuperHackersManifestFactoryTests : IDisposable
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
    /// Verifies that archive payload containing SuperHackers executable is extracted and processed.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task CreateManifestsFromExtractedContentAsync_ZipArchiveWithSuperHackersExe_ExtractsAndCreatesManifestAsync()
    {
        // Arrange
        Directory.CreateDirectory(_stagingDirectory);
        var archivePath = Path.Combine(_stagingDirectory, "generalszh-weekly.zip");
        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            await using var writer = new StreamWriter(archive.CreateEntry(GameClientConstants.SuperHackersZeroHourExecutable).Open());
            await writer.WriteAsync("dummy binary payload");
        }

        var hashProviderMock = new Mock<IFileHashProvider>();
        hashProviderMock.Setup(h => h.ComputeFileHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("dummyhash123");

        var payloadProcessor = new GenHub.Features.Content.Services.Common.ArchivePayloadProcessor(
            NullLogger<GenHub.Features.Content.Services.Common.ArchivePayloadProcessor>.Instance);

        var factory = new SuperHackersManifestFactory(
            NullLogger<SuperHackersManifestFactory>.Instance,
            hashProviderMock.Object,
            payloadProcessor);

        var originalManifest = new ContentManifest
        {
            Id = ManifestId.Create($"1.0.{PublisherTypeConstants.TheSuperHackers}.gameclient.zerohour"),
            Name = "TheSuperHackers Zero Hour Game Code",
            ContentType = ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
            Publisher = new PublisherInfo
            {
                PublisherType = PublisherTypeConstants.TheSuperHackers,
                Name = "TheSuperHackers",
            },
        };

        // Act
        var manifests = await factory.CreateManifestsFromExtractedContentAsync(originalManifest, _stagingDirectory);

        // Assert
        Assert.NotEmpty(manifests);
        var created = manifests.First();
        Assert.Equal(GameType.ZeroHour, created.TargetGame);
        Assert.Contains(created.Files, f => f.IsExecutable && f.RelativePath.Equals(GameClientConstants.SuperHackersZeroHourExecutable, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies that archive payload containing fallback game executable is detected and processed.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task CreateManifestsFromExtractedContentAsync_ZipArchiveWithFallbackExe_ExtractsAndCreatesManifestAsync()
    {
        // Arrange
        Directory.CreateDirectory(_stagingDirectory);
        var archivePath = Path.Combine(_stagingDirectory, "gamecode.zip");
        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            await using var writer = new StreamWriter(archive.CreateEntry(GameClientConstants.GeneralsExecutable).Open());
            await writer.WriteAsync("dummy binary payload");
        }

        var hashProviderMock = new Mock<IFileHashProvider>();
        hashProviderMock.Setup(h => h.ComputeFileHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("dummyhash456");

        var payloadProcessor = new GenHub.Features.Content.Services.Common.ArchivePayloadProcessor(
            NullLogger<GenHub.Features.Content.Services.Common.ArchivePayloadProcessor>.Instance);

        var factory = new SuperHackersManifestFactory(
            NullLogger<SuperHackersManifestFactory>.Instance,
            hashProviderMock.Object,
            payloadProcessor);

        var originalManifest = new ContentManifest
        {
            Id = ManifestId.Create($"1.0.{PublisherTypeConstants.TheSuperHackers}.gameclient.zerohour"),
            Name = "TheSuperHackers Zero Hour Game Code",
            ContentType = ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
            Publisher = new PublisherInfo
            {
                PublisherType = PublisherTypeConstants.TheSuperHackers,
                Name = "TheSuperHackers",
            },
        };

        // Act
        var manifests = await factory.CreateManifestsFromExtractedContentAsync(originalManifest, _stagingDirectory);

        // Assert
        Assert.NotEmpty(manifests);
        var created = manifests.First();
        Assert.Equal(GameType.ZeroHour, created.TargetGame);
        Assert.Contains(created.Files, f => f.IsExecutable && f.RelativePath.Equals(GameClientConstants.GeneralsExecutable, StringComparison.OrdinalIgnoreCase));
    }
}
