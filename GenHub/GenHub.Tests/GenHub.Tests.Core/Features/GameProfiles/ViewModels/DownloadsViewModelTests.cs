using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Features.Content.Services.ContentDiscoverers;
using GenHub.Features.Downloads.ViewModels;
using Microsoft.Extensions.Logging;
using Moq;

namespace GenHub.Tests.Core.Features.GameProfiles.ViewModels;

/// <summary>
/// Tests for DownloadsViewModel.
/// </summary>
public class DownloadsViewModelTests
{
    /// <summary>
    /// Ensures InitializeAsync completes successfully.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task InitializeAsync_CompletesSuccessfully()
    {
        // Arrange
        var mockServiceProvider = new Mock<IServiceProvider>();
        var mockLogger = new Mock<ILogger<DownloadsViewModel>>();
        var mockNotificationService = new Mock<INotificationService>();

        // Create a real instance of the discoverer with mocked dependencies to avoid Moq proxy issues
        var discoverer = new GitHubTopicsDiscoverer(
            new Mock<IGitHubApiClient>().Object,
            new Mock<ILogger<GitHubTopicsDiscoverer>>().Object);

        var mockConfigProvider = new Mock<IConfigurationProviderService>();
        mockConfigProvider.Setup(x => x.GetApplicationDataPath()).Returns(Path.GetTempPath());
        mockConfigProvider.Setup(x => x.GetWorkspacePath()).Returns(Path.Combine(Path.GetTempPath(), "GenHubWorkspaces"));

        var vm = new DownloadsViewModel(
            mockServiceProvider.Object,
            mockLogger.Object,
            mockNotificationService.Object,
            discoverer,
            mockConfigProvider.Object);

        // Act
        await vm.InitializeAsync();

        // Assert
        Assert.NotNull(vm);
    }
}