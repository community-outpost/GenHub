using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GenHub.Core.Interfaces.AppUpdate;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Core.Models.GitHub;
using GenHub.Features.AppUpdate.Services;
using GenHub.Features.GitHub.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Octokit;
using Xunit;

namespace GenHub.Tests.Core.Features.AppUpdate.Services;

/// <summary>
/// Integration tests for App Update service components.
/// </summary>
public class AppUpdateServiceIntegrationTests : IDisposable
{
    private readonly Mock<ILogger<AppUpdateService>> _mockAppUpdateLogger;
    private readonly Mock<ILogger<AppVersionService>> _mockVersionLogger;
    private readonly Mock<ILogger<OctokitGitHubApiClient>> _mockGitHubLogger;
    private readonly Mock<ILogger<UpdateInstaller>> _mockInstallerLogger;
    private readonly Mock<IGitHubClient> _mockGitHubClient;
    private readonly HttpClient _httpClient;

    public AppUpdateServiceIntegrationTests()
    {
        _mockAppUpdateLogger = new Mock<ILogger<AppUpdateService>>();
        _mockVersionLogger = new Mock<ILogger<AppVersionService>>();
        _mockGitHubLogger = new Mock<ILogger<OctokitGitHubApiClient>>();
        _mockInstallerLogger = new Mock<ILogger<UpdateInstaller>>();
        _mockGitHubClient = new Mock<IGitHubClient>();
        var mockHttpHandler = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        mockHttpHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = System.Net.HttpStatusCode.OK,
                Content = new StringContent("test content")
            });
        _httpClient = new HttpClient(mockHttpHandler.Object);    }

    [Fact]
    public async Task FullUpdateFlow_WithValidRelease_ShouldCompleteSuccessfully()
    {
        // Arrange
        var versionService = new AppVersionService(_mockVersionLogger.Object);
        var versionComparator = new SemVerComparator();
        var gitHubService = new OctokitGitHubApiClient(_mockGitHubClient.Object, _mockGitHubLogger.Object);
        var installer = new UpdateInstaller(_httpClient, _mockInstallerLogger.Object);
        var updateService = new AppUpdateService(
            versionService,
            versionComparator,
            gitHubService,
            installer,
            _mockAppUpdateLogger.Object);

        // Mock GitHub release
        var mockRelease = new Release(
            url: "https://api.github.com/repos/test/repo/releases/1",
            htmlUrl: "https://github.com/test/repo/releases/tag/v2.0.0",
            assetsUrl: "https://api.github.com/repos/test/repo/releases/1/assets",
            uploadUrl: "https://uploads.github.com/repos/test/repo/releases/1/assets",
            tarballUrl: "https://api.github.com/repos/test/repo/tarball/v2.0.0",
            zipballUrl: "https://api.github.com/repos/test/repo/zipball/v2.0.0",
            id: 1,
            nodeId: "MDc6UmVsZWFzZTE=",
            tagName: "v2.0.0",
            targetCommitish: "main",
            name: "Version 2.0.0",
            body: "New features and bug fixes",
            draft: false,
            prerelease: false,
            createdAt: DateTimeOffset.UtcNow.AddDays(-1),
            publishedAt: DateTimeOffset.UtcNow.AddDays(-1),
            author: new Author(),
            assets: new[]
            {
                new ReleaseAsset(
                    url: "https://api.github.com/repos/test/repo/releases/assets/1",
                    browserDownloadUrl: "https://github.com/test/repo/releases/download/v2.0.0/app.zip",
                    id: 1,
                    nodeId: "MDEyOlJlbGVhc2VBc3NldDE=",
                    name: "app-windows.zip",
                    label: null,
                    state: "uploaded",
                    contentType: "application/zip",
                    size: 1024,
                    downloadCount: 100,
                    createdAt: DateTimeOffset.UtcNow.AddDays(-1),
                    updatedAt: DateTimeOffset.UtcNow.AddDays(-1),
                    uploader: new Author())
            });

        _mockGitHubClient.Setup(x => x.Repository.Release.GetLatest("test", "repo"))
            .ReturnsAsync(mockRelease);

        // Act
        var result = await updateService.CheckForUpdatesAsync("test", "repo");

        // Assert
        result.Should().NotBeNull();
        result.IsUpdateAvailable.Should().BeTrue();
        result.LatestVersion.Should().Be("v2.0.0");
        result.Assets.Should().HaveCount(1);
    }

    [Fact]
    public void ServiceComposition_ShouldWorkCorrectly()
    {
        // Arrange & Act
        var versionService = new AppVersionService(_mockVersionLogger.Object);
        var versionComparator = new SemVerComparator();
        var gitHubService = new OctokitGitHubApiClient(_mockGitHubClient.Object, _mockGitHubLogger.Object);
        var installer = new UpdateInstaller(_httpClient, _mockInstallerLogger.Object);
        var updateService = new AppUpdateService(
            versionService,
            versionComparator,
            gitHubService,
            installer,
            _mockAppUpdateLogger.Object);

        // Assert
        versionService.Should().NotBeNull();
        versionComparator.Should().NotBeNull();
        gitHubService.Should().NotBeNull();
        installer.Should().NotBeNull();
        updateService.Should().NotBeNull();
        
        // Verify version service works
        var currentVersion = versionService.GetCurrentVersion();
        currentVersion.Should().NotBeNullOrEmpty();
        
        // Verify version comparator works
        var isNewer = versionComparator.IsNewerVersion("1.0.0", "2.0.0");
        isNewer.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateService_WithNetworkError_ShouldHandleGracefully()
    {
        // Arrange
        var versionService = new AppVersionService(_mockVersionLogger.Object);
        var versionComparator = new SemVerComparator();
        var gitHubService = new OctokitGitHubApiClient(_mockGitHubClient.Object, _mockGitHubLogger.Object);
        var installer = new UpdateInstaller(_httpClient, _mockInstallerLogger.Object);
        var updateService = new AppUpdateService(
            versionService,
            versionComparator,
            gitHubService,
            installer,
            _mockAppUpdateLogger.Object);

        _mockGitHubClient.Setup(x => x.Repository.Release.GetLatest("test", "repo"))
            .ThrowsAsync(new HttpRequestException("Network error"));

        // Act
        var result = await updateService.CheckForUpdatesAsync("test", "repo");

        // Assert
        result.Should().NotBeNull();
        result.IsUpdateAvailable.Should().BeFalse();
        result.HasErrors.Should().BeTrue();
        result.ErrorMessages.Should().Contain(msg => msg.Contains("Network error"));
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}
