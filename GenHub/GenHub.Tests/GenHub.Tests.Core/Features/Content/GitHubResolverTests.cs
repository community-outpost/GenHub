using GenHub.Core.Interfaces.GitHub;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GitHub;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Features.Content.Services.ContentResolvers;
using Microsoft.Extensions.Logging;
using Moq;

using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Content;

/// <summary>
/// Unit tests for <see cref="GitHubResolver"/>.
/// </summary>
public class GitHubResolverTests
{
    private readonly Mock<IGitHubApiClient> _apiClientMock;
    private readonly Mock<IContentManifestBuilder> _manifestBuilderMock;
    private readonly Mock<ILogger<GitHubResolver>> _loggerMock;
    private readonly GitHubResolver _resolver;

    /// <summary>
    /// Initializes a new instance of the <see cref="GitHubResolverTests"/> class.
    /// </summary>
    public GitHubResolverTests()
    {
        _apiClientMock = new Mock<IGitHubApiClient>();
        _manifestBuilderMock = new Mock<IContentManifestBuilder>();
        _loggerMock = new Mock<ILogger<GitHubResolver>>();
        _resolver = new GitHubResolver(_apiClientMock.Object, _manifestBuilderMock.Object, _loggerMock.Object);
    }

    /// <summary>
    /// Verifies that <see cref="GitHubResolver.ResolveAsync"/> returns a successful manifest when given valid discovered item.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task ResolveAsync_WithValidDiscoveredItem_ReturnsSuccessfulManifest()
    {
        // Arrange
        var discoveredItem = new ContentSearchResult
        {
            Id = "github.test.mod.v1",
            ResolverId = "GitHubRelease",
        };
        discoveredItem.ResolverMetadata["owner"] = "test-owner";
        discoveredItem.ResolverMetadata["repo"] = "test-repo";
        discoveredItem.ResolverMetadata["tag"] = "v1.0";

        var releaseAsset = new GitHubReleaseAsset
        {
            Name = "mod.zip",
            Size = 1024,
            BrowserDownloadUrl = "http://example.com/mod.zip",
        };
        var gitHubRelease = new GitHubRelease
        {
            Name = "Test Mod Release",
            TagName = "v1.0",
            Author = "Test Author",
            Body = "Release notes.",
            PublishedAt = new DateTimeOffset(2023, 1, 1, 0, 0, 0, TimeSpan.Zero),
            Assets = new List<GitHubReleaseAsset> { releaseAsset },
        };

        _apiClientMock.Setup(c => c.GetReleaseByTagAsync("test-owner", "test-repo", "v1.0", It.IsAny<CancellationToken>()))
            .ReturnsAsync(gitHubRelease);

        // Setup manifest builder chaining and Build()
        var manifestBuilder = _manifestBuilderMock;
        manifestBuilder.Setup(m => m.WithBasicInfo(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
            .Returns(manifestBuilder.Object);
        manifestBuilder.Setup(m => m.WithContentType(It.IsAny<ContentType>(), It.IsAny<GameType>()))
            .Returns(manifestBuilder.Object);
        manifestBuilder.Setup(m => m.WithPublisher(It.IsAny<string>(), string.Empty, string.Empty, string.Empty, string.Empty))
            .Returns(manifestBuilder.Object);
        manifestBuilder.Setup(m => m.WithMetadata(It.IsAny<string>(), It.IsAny<List<string>?>(), It.IsAny<string>(), It.IsAny<List<string>?>(), It.IsAny<string>()))
            .Returns(manifestBuilder.Object);
        manifestBuilder.Setup(m => m.WithInstallationInstructions(It.IsAny<WorkspaceStrategy>()))
            .Returns(manifestBuilder.Object);
        manifestBuilder.Setup(m => m.AddRemoteFileAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<ContentSourceType>(),
                It.IsAny<bool>(),
                It.IsAny<FilePermissions?>()))
            .ReturnsAsync(manifestBuilder.Object);

        // Build returns a real manifest
        manifestBuilder.Setup(m => m.Build()).Returns(new ContentManifest
        {
            Id = "1.0.genhub.mod.githubtestmod",
            Name = "Test Mod Release",
            Version = "v1.0",
            Publisher = new PublisherInfo { Name = "Test Author" },
            Metadata = new ContentMetadata { Description = "Release notes." },
            Files = new List<ManifestFile>
            {
                new ManifestFile
                {
                    RelativePath = "mod.zip",
                    Size = 1024,
                    DownloadUrl = "http://example.com/mod.zip",
                },
            },
        });

        // Act
        var result = await _resolver.ResolveAsync(discoveredItem);

        // Assert
        if (!result.Success)
        {
            var invocationMethods = _manifestBuilderMock.Invocations.Select(i => i.Method.Name).ToList();
            Assert.Fail($"Resolver failure: {result.FirstError}. Builder invocations: {string.Join(",", invocationMethods)}");
        }

        ContentManifest manifest = result.Data!;
        Assert.NotNull(manifest);
        Assert.Equal("1.0.genhub.mod.githubtestmod", manifest.Id);
        Assert.Equal("Test Mod Release", manifest.Name);
        Assert.Equal("v1.0", manifest.Version);
        Assert.Equal("Test Author", manifest.Publisher.Name);
        Assert.Equal("Release notes.", manifest.Metadata.Description);

        var manifestFile = Assert.Single(manifest.Files);
        Assert.Equal("mod.zip", manifestFile.RelativePath);
        Assert.Equal(1024, manifestFile.Size);
        Assert.Equal("http://example.com/mod.zip", manifestFile.DownloadUrl);
    }

    /// <summary>
    /// Verifies that <see cref="GitHubResolver.ResolveAsync"/> returns failure when metadata is missing.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task ResolveAsync_MissingMetadata_ReturnsFailure()
    {
        // Arrange
        var discoveredItem = new ContentSearchResult { ResolverId = "GitHubRelease" }; // Missing metadata

        // Act
        var result = await _resolver.ResolveAsync(discoveredItem);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Missing required metadata", result.FirstError);
    }
}
