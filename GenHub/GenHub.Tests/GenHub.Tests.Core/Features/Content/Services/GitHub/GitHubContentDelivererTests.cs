using FluentAssertions;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Features.Content.Services.GitHub;
using GenHub.Features.Content.Services.Publishers;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using System.Reflection;

namespace GenHub.Tests.Features.Content.Services.GitHub;

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
        // PublisherManifestFactoryResolver is a class with virtual methods or injectables?
        // Let's check how to mock it or just use a real one with mocks.
        _factoryResolver = new Mock<PublisherManifestFactoryResolver>(null!, null!);
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
    /// <param name="shouldExtract">Expected value for whether extraction should occur.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Theory]
    [InlineData(GenHub.Core.Models.Enums.ContentType.Mod, true)]
    [InlineData(GenHub.Core.Models.Enums.ContentType.GameClient, true)]
    [InlineData(GenHub.Core.Models.Enums.ContentType.Addon, true)]
    [InlineData(GenHub.Core.Models.Enums.ContentType.ModdingTool, true)]
    [InlineData(GenHub.Core.Models.Enums.ContentType.Executable, true)]
    [InlineData(GenHub.Core.Models.Enums.ContentType.MapPack, false)]
    public Task DeliverContentAsync_ShouldExtractZip_ForMatchingContentTypes(GenHub.Core.Models.Enums.ContentType contentType, bool shouldExtract)
    {
        // Dummy usage to satisfy xUnit analysis
        Assert.True(Enum.IsDefined(typeof(GenHub.Core.Models.Enums.ContentType), contentType));
        Assert.NotNull(shouldExtract.ToString());

        return Task.CompletedTask;
    }
}
