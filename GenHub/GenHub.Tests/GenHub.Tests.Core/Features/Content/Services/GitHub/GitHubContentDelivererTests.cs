using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Features.Content.Services.GitHub;
using GenHub.Features.Content.Services.Publishers;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

using ContentType = GenHub.Core.Models.Enums.ContentType;
using FileMode = System.IO.FileMode;
using ZipArchiveMode = System.IO.Compression.ZipArchiveMode;

namespace GenHub.Tests.Core.Features.Content.Services.GitHub;

/// <summary>
/// Unit tests for <see cref="GitHubContentDeliverer"/>.
/// </summary>
public class GitHubContentDelivererTests : IAsyncLifetime
{
    private readonly Mock<IDownloadService> _downloadService = new();
    private readonly Mock<IContentManifestPool> _manifestPool = new();
    private readonly Mock<IPublisherManifestFactoryResolver> _factoryResolver = new();
    private readonly Mock<ILogger<GitHubContentDeliverer>> _logger = new();
    private string _tempDir = string.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="GitHubContentDelivererTests"/> class.
    /// </summary>
    public GitHubContentDelivererTests()
    {
    }

    /// <inheritdoc />
    public Task InitializeAsync()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "GitHubDelivererTest_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DisposeAsync()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Tests that CanDeliver returns true for GitHub URLs.
    /// </summary>
    [Fact]
    public void CanDeliver_ShouldReturnTrue_ForGitHubUrls()
    {
        var deliverer = new GitHubContentDeliverer(_downloadService.Object, _manifestPool.Object, _factoryResolver.Object, _logger.Object);
        var manifest = new ContentManifest
        {
            Files = [new ManifestFile { DownloadUrl = "https://github.com/user/repo/release.zip" }],
        };

        deliverer.CanDeliver(manifest).Should().BeTrue();
    }

    /// <summary>
    /// Tests that CanDeliver returns false for non-GitHub URLs.
    /// </summary>
    [Fact]
    public void CanDeliver_ShouldReturnFalse_ForNonGitHubUrls()
    {
        var deliverer = new GitHubContentDeliverer(_downloadService.Object, _manifestPool.Object, _factoryResolver.Object, _logger.Object);
        var manifest = new ContentManifest
        {
            Files = [new ManifestFile { DownloadUrl = "https://example.com/release.zip" }],
        };

        deliverer.CanDeliver(manifest).Should().BeFalse();
    }

    /// <summary>
    /// Tests that DeliverContentAsync extracts ZIP files for matching content types.
    /// </summary>
    /// <param name="contentType">The type of content being delivered.</param>
    /// <param name="zipFilesExist">Indicates whether ZIP files exist in the manifest.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Theory]
    [InlineData(ContentType.Mod, true)]
    [InlineData(ContentType.GameClient, true)]
    [InlineData(ContentType.Addon, true)]
    [InlineData(ContentType.ModdingTool, true)]
    [InlineData(ContentType.Executable, true)]
    [InlineData(ContentType.MapPack, false)]
    public async Task DeliverContentAsync_ShouldExtractZip_IfZipFilesExist(ContentType contentType, bool zipFilesExist)
    {
        // Arrange
        var deliverer = new GitHubContentDeliverer(_downloadService.Object, _manifestPool.Object, _factoryResolver.Object, _logger.Object);

        var files = new List<ManifestFile>();
        if (zipFilesExist)
        {
            files.Add(new ManifestFile { RelativePath = "test.zip", DownloadUrl = "https://github.com/user/repo/test.zip" });
        }
        else
        {
            files.Add(new ManifestFile { RelativePath = "test.txt", DownloadUrl = "https://github.com/user/repo/test.txt" });
        }

        var id = ManifestId.Create("1.0.test.mod.content");
        var manifest = new ContentManifest
        {
            Id = id,
            ContentType = contentType,
            Files = files,
        };

        _downloadService.Setup(x => x.DownloadFileAsync(It.IsAny<Uri>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<IProgress<DownloadProgress>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Uri uri, string path, string? hash, IProgress<DownloadProgress>? progress, CancellationToken ct) =>
            {
                if (path.EndsWith(".zip"))
                {
                    // Create a valid empty ZIP file if needed, but the code just opens it.
                    // We'll create a dummy one to avoid ZipException.
                    using var fileStream = new FileStream(path, FileMode.Create);
                    using var archive = new ZipArchive(fileStream, ZipArchiveMode.Create);
                }
                else
                {
                    File.WriteAllText(path, "text content");
                }

                return DownloadResult.CreateSuccess(path, 0, TimeSpan.Zero);
            });

        // If it extracts, it will call HandleExtractedContent -> factoryResolver.ResolveFactory
        if (zipFilesExist)
        {
            var mockFactory = new Mock<IPublisherManifestFactory>();
            mockFactory.Setup(x => x.CreateManifestsFromExtractedContentAsync(It.IsAny<ContentManifest>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([new ContentManifest { Id = ManifestId.Create("1.0.test.mod.content") }]);
            mockFactory.Setup(x => x.GetManifestDirectory(It.IsAny<ContentManifest>(), It.IsAny<string>()))
                 .Returns(Path.Combine(_tempDir, "extracted"));

            _factoryResolver.Setup(x => x.ResolveFactory(It.IsAny<ContentManifest>()))
                .Returns(mockFactory.Object);

            _manifestPool.Setup(x => x.AddManifestAsync(It.IsAny<ContentManifest>(), It.IsAny<string>(), It.IsAny<IProgress<ContentStorageProgress>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));
        }

        // Act
        var result = await deliverer.DeliverContentAsync(manifest, _tempDir);

        // Assert
        result.Success.Should().BeTrue();
        if (zipFilesExist)
        {
            // Verify that factory resolver was called if zip files existed
            _factoryResolver.Verify(x => x.ResolveFactory(It.IsAny<ContentManifest>()), Times.AtLeastOnce);

            // The zip file should have been deleted after extraction
            File.Exists(Path.Combine(_tempDir, "test.zip")).Should().BeFalse();
        }
        else
        {
            // Should NOT have called factory resolver if no zip files were present
            _factoryResolver.Verify(x => x.ResolveFactory(It.IsAny<ContentManifest>()), Times.Never);
        }
    }
}
