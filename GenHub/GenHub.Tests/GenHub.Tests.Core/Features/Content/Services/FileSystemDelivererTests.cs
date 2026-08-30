using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Tools;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Features.Content.Services.ContentDeliverers;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Content.Services;

/// <summary>
/// Unit tests for <see cref="FileSystemDeliverer"/>.
/// </summary>
public sealed class FileSystemDelivererTests
{
    private readonly Mock<IConfigurationProviderService> _configProviderMock = new();

    /// <summary>
    /// Verifies that CanDeliver returns false when the manifest has no files.
    /// </summary>
    [Fact]
    public void CanDeliver_EmptyFiles_ReturnsFalse()
    {
        var deliverer = CreateDeliverer();
        var manifest = new ContentManifest
        {
            Id = ManifestId.Create("1.0.test.gameclient.zerohour"),
            Name = "Test",
            ContentType = ContentType.GameClient,
            Files = [],
        };

        var canDeliver = deliverer.CanDeliver(manifest);

        Assert.False(canDeliver);
    }

    /// <summary>
    /// Verifies that CanDeliver returns true when all files are ContentAddressable.
    /// </summary>
    [Fact]
    public void CanDeliver_ContentAddressableFiles_ReturnsTrue()
    {
        var deliverer = CreateDeliverer();
        var manifest = new ContentManifest
        {
            Id = ManifestId.Create("1.0.test.gameclient.zerohour"),
            Name = "Test",
            ContentType = ContentType.GameClient,
            Files =
            [
                new ManifestFile
                {
                    RelativePath = "test.dll",
                    SourceType = ContentSourceType.ContentAddressable,
                },
            ],
        };

        var canDeliver = deliverer.CanDeliver(manifest);

        Assert.True(canDeliver);
    }

    /// <summary>
    /// Verifies that DeliverContentAsync supports string versions without throwing format errors.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task DeliverContentAsync_StringVersion_SucceedsAsync()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "sample content");
            _configProviderMock.Setup(c => c.GetWorkspacePath()).Returns(Path.GetDirectoryName(tempFile)!);

            var deliverer = CreateDeliverer();
            var manifest = new ContentManifest
            {
                Id = ManifestId.Create("1.0.thesuperhackers.gameclient.zerohour"),
                Name = "TheSuperHackers Zero Hour Game Code",
                ContentType = ContentType.GameClient,
                TargetGame = GameType.ZeroHour,
                Version = "2026.07.31",
                Files =
                [
                    new ManifestFile
                    {
                        RelativePath = Path.GetFileName(tempFile),
                        SourceType = ContentSourceType.ContentAddressable,
                        SourcePath = tempFile,
                    },
                ],
            };

            var result = await deliverer.DeliverContentAsync(manifest, Path.GetDirectoryName(tempFile)!);

            Assert.True(result.Success, result.FirstError);
            Assert.NotNull(result.Data);
            Assert.Equal("2026.07.31", result.Data.Version);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    private FileSystemDeliverer CreateDeliverer(IContentManifestBuilder? manifestBuilder = null)
    {
        var builderMock = new Mock<IContentManifestBuilder>();
        builderMock.Setup(b => b.WithBasicInfo(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>())).Returns(builderMock.Object);
        builderMock.Setup(b => b.WithContentType(It.IsAny<ContentType>(), It.IsAny<GameType>())).Returns(builderMock.Object);
        builderMock.Setup(b => b.WithPublisher(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).Returns(builderMock.Object);
        builderMock.Setup(b => b.WithMetadata(It.IsAny<string>(), It.IsAny<List<string>?>(), It.IsAny<string>(), It.IsAny<List<string>?>(), It.IsAny<string>())).Returns(builderMock.Object);
        builderMock.Setup(b => b.AddContentAddressableFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<bool>(), It.IsAny<FilePermissions?>())).ReturnsAsync(builderMock.Object);
        builderMock.Setup(b => b.AddRequiredDirectories(It.IsAny<string[]>())).Returns(builderMock.Object);
        builderMock.Setup(b => b.Build()).Returns(new ContentManifest
        {
            Id = ManifestId.Create("1.0.thesuperhackers.gameclient.zerohour"),
            Name = "TheSuperHackers Zero Hour Game Code",
            Version = "2026.07.31",
            ContentType = ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
        });

        return new FileSystemDeliverer(
            NullLogger<FileSystemDeliverer>.Instance,
            _configProviderMock.Object,
            () => manifestBuilder ?? builderMock.Object);
    }
}
