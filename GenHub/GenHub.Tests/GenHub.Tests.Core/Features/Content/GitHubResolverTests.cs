using GenHub.Core.Constants;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GitHub;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.Content.Services.GitHub;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Content;

/// <summary>
/// Unit tests for <see cref="GitHubResolver"/>.
/// </summary>
public class GitHubResolverTests : IDisposable
{
    private readonly Mock<IGitHubApiClient> _apiClientMock;
    private readonly Mock<IContentManifestBuilder> _manifestBuilderMock;
    private readonly Mock<ILogger<GitHubResolver>> _loggerMock;
    private readonly ServiceProvider _serviceProvider;
    private readonly GitHubResolver _resolver;

    /// <summary>
    /// Initializes a new instance of the <see cref="GitHubResolverTests"/> class.
    /// </summary>
    public GitHubResolverTests()
    {
        _apiClientMock = new Mock<IGitHubApiClient>();
        _manifestBuilderMock = new Mock<IContentManifestBuilder>();
        _loggerMock = new Mock<ILogger<GitHubResolver>>();

        var services = new ServiceCollection();
        services.AddTransient<IContentManifestBuilder>(sp => _manifestBuilderMock.Object);
        _serviceProvider = services.BuildServiceProvider();

        _resolver = new GitHubResolver(_apiClientMock.Object, _serviceProvider, _loggerMock.Object);
    }

    /// <summary>
    /// Tests that ResolveAsync returns a successful manifest when given a valid discovered item.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test.</returns>
    [Fact]
    public async Task ResolveAsync_WithValidDiscoveredItem_ReturnsSuccessfulManifest()
    {
        var discoveredItem = CreateItem("v1.0");
        var release = CreateRelease("v1.0");

        _apiClientMock.Setup(c => c.GetReleaseByTagAsync(It.IsAny<string>(), It.IsAny<string>(), "v1.0", It.IsAny<CancellationToken>()))
            .ReturnsAsync(release);

        SetupBuilder(release);

        var result = await _resolver.ResolveAsync(discoveredItem);

        Assert.True(result.Success);
        Assert.Equal("v1.0", result.Data!.Version);
    }

    /// <summary>
    /// Tests that ResolveAsync calls GetLatestReleaseAsync when the tag is "latest".
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test.</returns>
    [Fact]
    public async Task ResolveAsync_WithLatestTag_CallsGetLatestRelease()
    {
        var discoveredItem = CreateItem("latest");
        var release = CreateRelease("v1.1");

        _apiClientMock.Setup(c => c.GetLatestReleaseAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(release);

        SetupBuilder(release);

        var result = await _resolver.ResolveAsync(discoveredItem);

        Assert.True(result.Success);
        _apiClientMock.Verify(c => c.GetLatestReleaseAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Tests that ResolveAsync falls back to any release when the latest release is not found.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test.</returns>
    [Fact]
    public async Task ResolveAsync_WhenLatestReleaseNotFound_FallsBackToAnyRelease()
    {
        var discoveredItem = CreateItem("latest");
        var preRelease = CreateRelease("v0.5-beta");

        _apiClientMock.Setup(c => c.GetLatestReleaseAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GitHubRelease)null!);

        _apiClientMock.Setup(c => c.GetReleasesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([preRelease]);

        SetupBuilder(preRelease);

        var result = await _resolver.ResolveAsync(discoveredItem);

        Assert.True(result.Success);
        Assert.Equal("v0.5-beta", result.Data!.Version);
        _apiClientMock.Verify(c => c.GetReleasesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Tests that ResolveAsync returns a failure result when metadata is missing.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ResolveAsync_MissingMetadata_ReturnsFailure()
    {
        var discoveredItem = new ContentSearchResult { ResolverId = "GitHubRelease" };
        var result = await _resolver.ResolveAsync(discoveredItem);
        Assert.False(result.Success);
    }

    /// <summary>
    /// Disposes of the test resources.
    /// </summary>
    public void Dispose()
    {
        _serviceProvider?.Dispose();
        GC.SuppressFinalize(this);
    }

    private static ContentSearchResult CreateItem(string tag)
    {
        var item = new ContentSearchResult { ResolverId = "GitHubRelease" };
        item.ResolverMetadata[GitHubConstants.OwnerMetadataKey] = "owner";
        item.ResolverMetadata[GitHubConstants.RepoMetadataKey] = "repo";
        item.ResolverMetadata[GitHubConstants.TagMetadataKey] = tag;
        return item;
    }

    private static GitHubRelease CreateRelease(string tag)
    {
        return new GitHubRelease
        {
            TagName = tag,
            PublishedAt = DateTimeOffset.Now,
            Assets = [new GitHubReleaseAsset { Name = "test.zip", BrowserDownloadUrl = "http://test.com" },],
        };
    }

    private void SetupBuilder(GitHubRelease release)
    {
        _manifestBuilderMock.Setup(m => m.WithBasicInfo(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>())).Returns(_manifestBuilderMock.Object);
        _manifestBuilderMock.Setup(m => m.WithContentType(It.IsAny<ContentType>(), It.IsAny<GameType>())).Returns(_manifestBuilderMock.Object);
        _manifestBuilderMock.Setup(m => m.WithPublisher(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).Returns(_manifestBuilderMock.Object);
        _manifestBuilderMock.Setup(m => m.WithMetadata(It.IsAny<string>(), It.IsAny<List<string>?>(), It.IsAny<string>(), It.IsAny<List<string>?>(), It.IsAny<string>())).Returns(_manifestBuilderMock.Object);
        _manifestBuilderMock.Setup(m => m.WithInstallationInstructions(It.IsAny<WorkspaceStrategy>())).Returns(_manifestBuilderMock.Object);
        _manifestBuilderMock.Setup(m => m.AddRemoteFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ContentSourceType>(), It.IsAny<bool>(), It.IsAny<FilePermissions?>())).ReturnsAsync(_manifestBuilderMock.Object);
        _manifestBuilderMock.Setup(m => m.Build()).Returns(new ContentManifest { Version = release.TagName });
    }
}