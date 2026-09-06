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
