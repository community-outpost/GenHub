using System.Linq;
using FluentAssertions;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Features.Content.Services.GitHub;
using GenHub.Features.Content.Services.Publishers;
using Microsoft.Extensions.Logging;
using Moq;

using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Content.Services.GitHub;

/// <summary>
/// Unit tests for <see cref="GitHubContentDeliverer"/>.
/// </summary>
public class GitHubContentDelivererTests
{
    private readonly Mock<IDownloadService> _downloadService = new();
    private readonly Mock<IContentManifestPool> _manifestPool = new();
    private readonly Mock<PublisherManifestFactoryResolver> _factoryResolver;
    private readonly Mock<ILogger<GitHubContentDeliverer>> _logger = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="GitHubContentDelivererTests"/> class.
    /// </summary>
    public GitHubContentDelivererTests()
    {
        // PublisherManifestFactoryResolver expects IEnumerable<IPublisherManifestFactory> + ILogger.
        // Since we override ResolveFactory behavior in tests, we can pass an empty factory list.
        _factoryResolver = new Mock<PublisherManifestFactoryResolver>(
            Enumerable.Empty<IPublisherManifestFactory>(),
            new Mock<ILogger<PublisherManifestFactoryResolver>>().Object);
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
    /// Tests that DeliverContentAsync extracts ZIP files for various content types.
    /// </summary>
    /// <param name="contentType">The content type to test.</param>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Theory]
    [InlineData(ContentType.Mod)]
    [InlineData(ContentType.GameClient)]
    [InlineData(ContentType.Addon)]
    [InlineData(ContentType.ModdingTool)]
    [InlineData(ContentType.Executable)]
    public async Task DeliverContentAsync_ShouldExtractZip_ForMatchingContentTypes(ContentType contentType)
    {
        // Arrange
        var deliverer = new GitHubContentDeliverer(_downloadService.Object, _manifestPool.Object, _factoryResolver.Object, _logger.Object);
        var manifest = new ContentManifest
        {
            Id = ManifestId.Create("1.100.github." + GetContentTypeSuffix(contentType) + ".testcontent"),
            ContentType = contentType,
            Publisher = new PublisherInfo { PublisherType = "test-publisher" },
            Files = [new ManifestFile { DownloadUrl = "https://github.com/user/repo/release.zip", RelativePath = "release.zip" }],
        };

        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            // Mock download service to return success and create a dummy zip file
            _downloadService.Setup(s => s.DownloadFileAsync(It.IsAny<Uri>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IProgress<DownloadProgress>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(DownloadResult.CreateSuccess("test.zip", 100, TimeSpan.FromSeconds(1), true))
                .Callback<Uri, string, string, IProgress<DownloadProgress>, CancellationToken>((uri, path, hash, prog, token) =>
                {
                    // Create a dummy zip file so Path.GetExtension(f).Equals(".zip") works
                    System.IO.File.WriteAllBytes(path, [0x50, 0x4B, 0x05, 0x06, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00]);
                });

            // Mock a factory to avoid resolution failure
            var mockFactory = new Mock<IPublisherManifestFactory>();
            mockFactory.Setup(f => f.CreateManifestsFromExtractedContentAsync(It.IsAny<ContentManifest>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([manifest]);

            _factoryResolver.Setup(r => r.ResolveFactory(It.IsAny<ContentManifest>()))
                .Returns(mockFactory.Object);

            _manifestPool.Setup(p => p.AddManifestAsync(It.IsAny<ContentManifest>(), It.IsAny<string>(), It.IsAny<IProgress<GenHub.Core.Models.Content.ContentStorageProgress>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

            // Act
            var result = await deliverer.DeliverContentAsync(manifest, tempDir);

            // Assert
            result.Success.Should().BeTrue();
            result.Data.Should().NotBeNull();
            result.Data!.Id.Should().Be(manifest.Id);

            // If it is a ZIP, GitHubContentDeliverer extracts and resolves a factory
            _factoryResolver.Verify(r => r.ResolveFactory(It.IsAny<ContentManifest>()), Times.AtLeastOnce());
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    private static string GetContentTypeSuffix(ContentType contentType)
    {
        return contentType switch
        {
            ContentType.Mod => "mod",
            ContentType.GameClient => "gameclient",
            ContentType.Addon => "addon",
            ContentType.Map => "map",
            ContentType.Skin => "skin",
            ContentType.LanguagePack => "langpack",
            ContentType.Video => "video",
            ContentType.ModdingTool => "tool",
            ContentType.Executable => "exe",
            _ => throw new ArgumentOutOfRangeException(nameof(contentType), contentType, null),
        };
    }
}
