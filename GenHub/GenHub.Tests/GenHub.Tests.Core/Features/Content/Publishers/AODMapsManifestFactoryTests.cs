using System;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Features.Content.Services.Publishers;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Content.Publishers;

/// <summary>
/// Regression tests for AODMaps manifest post-processing.
/// </summary>
public sealed class AODMapsManifestFactoryTests : IDisposable
{
    private readonly string _stagingDirectory = Path.Combine(Path.GetTempPath(), "GenHubTests", Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Verifies that AODMaps archive payloads are installed into the user Maps directory.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task CreateManifestsFromExtractedContentAsync_MapArchive_RoutesPayloadToUserMapsDirectoryAsync()
    {
        // Arrange
        Directory.CreateDirectory(_stagingDirectory);
        var archivePath = Path.Combine(_stagingDirectory, "last-stand.zip");
        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            await using var writer = new StreamWriter(archive.CreateEntry("LastStand.map").Open());
            await writer.WriteAsync("map payload");
        }

        var hashProvider = new Mock<IFileHashProvider>();
        hashProvider.Setup(provider => provider.ComputeFileHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("content-hash");
        var factory = new AODMapsManifestFactory(
            () => new Mock<IContentManifestBuilder>().Object,
            new Mock<IManifestIdService>().Object,
            new Mock<IProviderDefinitionLoader>().Object,
            hashProvider.Object,
            new Mock<ILogger<AODMapsManifestFactory>>().Object);
        var original = new ContentManifest
        {
            Id = "1.0.aodmaps.map.laststand",
            Name = "Last Stand",
            ContentType = ContentType.Map,
            TargetGame = GameType.ZeroHour,
        };

        // Act
        var manifest = Assert.Single(await factory.CreateManifestsFromExtractedContentAsync(original, _stagingDirectory));

        // Assert
        var file = Assert.Single(manifest.Files);
        Assert.Equal(ContentInstallTarget.UserMapsDirectory, file.InstallTarget);
    }

    /// <summary>
    /// Verifies that an archive entry attempting path traversal throws an InvalidDataException.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task CreateManifestsFromExtractedContentAsync_PathTraversalArchive_ThrowsInvalidDataExceptionAsync()
    {
        // Arrange
        Directory.CreateDirectory(_stagingDirectory);
        var zipPath = Path.Combine(_stagingDirectory, "traversal.zip");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("../escaped.map");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("malicious content");
        }

        var factory = new AODMapsManifestFactory(
            () => new Mock<IContentManifestBuilder>().Object,
            new Mock<IManifestIdService>().Object,
            new Mock<IProviderDefinitionLoader>().Object,
            new Mock<IFileHashProvider>().Object,
            new Mock<ILogger<AODMapsManifestFactory>>().Object);
        var original = new ContentManifest
        {
            Id = "1.0.aodmaps.map.traversal",
            Name = "Traversal Test",
            ContentType = ContentType.Map,
            TargetGame = GameType.ZeroHour,
        };

        // Act & Assert
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            factory.CreateManifestsFromExtractedContentAsync(original, _stagingDirectory));
    }

    /// <summary>
    /// Verifies that an empty archive containing no files throws an InvalidDataException.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task CreateManifestsFromExtractedContentAsync_EmptyArchive_ThrowsInvalidDataExceptionAsync()
    {
        // Arrange
        Directory.CreateDirectory(_stagingDirectory);
        var zipPath = Path.Combine(_stagingDirectory, "empty.zip");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            // Empty archive - no entries
        }

        var factory = new AODMapsManifestFactory(
            () => new Mock<IContentManifestBuilder>().Object,
            new Mock<IManifestIdService>().Object,
            new Mock<IProviderDefinitionLoader>().Object,
            new Mock<IFileHashProvider>().Object,
            new Mock<ILogger<AODMapsManifestFactory>>().Object);
        var original = new ContentManifest
        {
            Id = "1.0.aodmaps.map.empty",
            Name = "Empty Map",
            ContentType = ContentType.Map,
            TargetGame = GameType.ZeroHour,
        };

        // Act & Assert
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            factory.CreateManifestsFromExtractedContentAsync(original, _stagingDirectory));
    }

    /// <summary>
    /// Verifies that when the extracted directory does not exist, the original manifest is returned.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task CreateManifestsFromExtractedContentAsync_DirectoryDoesNotExist_ReturnsOriginalManifestAsync()
    {
        // Arrange
        var nonExistentDirectory = Path.Combine(_stagingDirectory, "does-not-exist");
        var factory = new AODMapsManifestFactory(
            () => new Mock<IContentManifestBuilder>().Object,
            new Mock<IManifestIdService>().Object,
            new Mock<IProviderDefinitionLoader>().Object,
            new Mock<IFileHashProvider>().Object,
            new Mock<ILogger<AODMapsManifestFactory>>().Object);
        var original = new ContentManifest
        {
            Id = "1.0.aodmaps.map.test",
            Name = "Test Map",
        };

        // Act
        var result = await factory.CreateManifestsFromExtractedContentAsync(original, nonExistentDirectory);

        // Assert
        var manifest = Assert.Single(result);
        Assert.Same(original, manifest);
    }

    /// <summary>
    /// Verifies that when no ZIP files exist in the extracted directory, the original manifest is returned.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task CreateManifestsFromExtractedContentAsync_NoZipFiles_ReturnsOriginalManifestAsync()
    {
        // Arrange
        Directory.CreateDirectory(_stagingDirectory);
        var factory = new AODMapsManifestFactory(
            () => new Mock<IContentManifestBuilder>().Object,
            new Mock<IManifestIdService>().Object,
            new Mock<IProviderDefinitionLoader>().Object,
            new Mock<IFileHashProvider>().Object,
            new Mock<ILogger<AODMapsManifestFactory>>().Object);
        var original = new ContentManifest
        {
            Id = "1.0.aodmaps.map.test",
            Name = "Test Map",
        };

        // Act
        var result = await factory.CreateManifestsFromExtractedContentAsync(original, _stagingDirectory);

        // Assert
        var manifest = Assert.Single(result);
        Assert.Same(original, manifest);
    }

    /// <summary>
    /// Deletes the test staging directory.
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(_stagingDirectory))
        {
            Directory.Delete(_stagingDirectory, recursive: true);
        }
    }
}
