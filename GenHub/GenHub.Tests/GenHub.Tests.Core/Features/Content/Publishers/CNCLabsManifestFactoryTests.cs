using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Features.Content.Services.Publishers;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Content.Publishers;

/// <summary>
/// Regression tests for CNC Labs manifest post-processing.
/// </summary>
public sealed class CNCLabsManifestFactoryTests : IDisposable
{
    private readonly string _stagingDirectory = Path.Combine(Path.GetTempPath(), "GenHubTests", Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Verifies that map archives produce user-Maps payload entries instead of workspace files.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task CreateManifestsFromExtractedContentAsync_MapArchive_RoutesPayloadToUserMapsDirectoryAsync()
    {
        // Arrange
        Directory.CreateDirectory(_stagingDirectory);
        var archivePath = Path.Combine(_stagingDirectory, "south-lebanon.zip");
        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            await using var writer = new StreamWriter(archive.CreateEntry("SouthLebanon.map").Open());
            await writer.WriteAsync("map payload");
        }

        var hashProvider = new Mock<IFileHashProvider>();
        hashProvider.Setup(provider => provider.ComputeFileHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("content-hash");
        var factory = new CNCLabsManifestFactory(
            () => new Mock<IContentManifestBuilder>().Object,
            new Mock<IProviderDefinitionLoader>().Object,
            hashProvider.Object,
            new Mock<ILogger<CNCLabsManifestFactory>>().Object);
        var original = new ContentManifest
        {
            Id = "1.0.cnclabs.map.southlebanon",
            Name = "South Lebanon",
            ContentType = ContentType.Map,
            TargetGame = GameType.ZeroHour,
        };

        // Act
        var result = await factory.CreateManifestsFromExtractedContentAsync(original, _stagingDirectory);
        Assert.True(result.Success);
        var manifest = Assert.Single(result.Data!);

        // Assert
        var file = Assert.Single(manifest.Files);
        Assert.Equal(ContentInstallTarget.UserMapsDirectory, file.InstallTarget);
    }

    /// <summary>
    /// Verifies that non-map archives (such as mods) route payloads to the Workspace directory.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task CreateManifestsFromExtractedContentAsync_ModArchive_RoutesPayloadToWorkspaceAsync()
    {
        // Arrange
        Directory.CreateDirectory(_stagingDirectory);
        var archivePath = Path.Combine(_stagingDirectory, "test-mod.zip");
        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            await using var writer = new StreamWriter(archive.CreateEntry("modfile.big").Open());
            await writer.WriteAsync("big payload");
        }

        var hashProvider = new Mock<IFileHashProvider>();
        hashProvider.Setup(provider => provider.ComputeFileHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("content-hash");
        var factory = new CNCLabsManifestFactory(
            () => new Mock<IContentManifestBuilder>().Object,
            new Mock<IProviderDefinitionLoader>().Object,
            hashProvider.Object,
            new Mock<ILogger<CNCLabsManifestFactory>>().Object);
        var original = new ContentManifest
        {
            Id = "1.0.cnclabs.mod.testmod",
            Name = "Test Mod",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
        };

        // Act
        var result = await factory.CreateManifestsFromExtractedContentAsync(original, _stagingDirectory);
        Assert.True(result.Success);
        var manifest = Assert.Single(result.Data!);

        // Assert
        var file = Assert.Single(manifest.Files);
        Assert.Equal(ContentInstallTarget.Workspace, file.InstallTarget);
    }

    /// <summary>
    /// Verifies that CanHandle returns false safely when publisher info is null.
    /// </summary>
    [Fact]
    public void CanHandle_ShouldReturnFalse_WhenPublisherIsNull()
    {
        var factory = new CNCLabsManifestFactory(
            () => new Mock<IContentManifestBuilder>().Object,
            new Mock<IProviderDefinitionLoader>().Object,
            new Mock<IFileHashProvider>().Object,
            new Mock<ILogger<CNCLabsManifestFactory>>().Object);

        var manifest = new ContentManifest
        {
            Id = "1.0.cnclabs.map.test",
            Name = "Test",
            Publisher = null!,
        };

        Assert.False(factory.CanHandle(manifest));
    }

    /// <summary>
    /// Verifies that CanHandle returns true when publisher type matches case-insensitively.
    /// </summary>
    [Fact]
    public void CanHandle_ShouldReturnTrue_WhenPublisherMatchesCaseInsensitive()
    {
        var factory = new CNCLabsManifestFactory(
            () => new Mock<IContentManifestBuilder>().Object,
            new Mock<IProviderDefinitionLoader>().Object,
            new Mock<IFileHashProvider>().Object,
            new Mock<ILogger<CNCLabsManifestFactory>>().Object);

        var manifest = new ContentManifest
        {
            Id = "1.0.cnclabs.map.test",
            Name = "Test",
            Publisher = new PublisherInfo { PublisherType = "CNCLABS" },
        };

        Assert.True(factory.CanHandle(manifest));
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
