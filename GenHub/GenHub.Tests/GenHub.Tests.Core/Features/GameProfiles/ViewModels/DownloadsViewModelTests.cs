using GenHub.Core.Interfaces.GitHub;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Features.Content.Services.ContentDiscoverers;
using GenHub.Features.Downloads.ViewModels;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;
using System.Threading.Tasks;
using Xunit;

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
        var serviceProviderMock = new Mock<IServiceProvider>();
        var loggerMock = new Mock<ILogger<DownloadsViewModel>>();
        var mockNotificationService = new Mock<INotificationService>();
        var mockGitHubDiscoverer = new Mock<GitHubTopicsDiscoverer>(
            It.IsAny<IGitHubApiClient>(),
            It.IsAny<ILogger<GitHubTopicsDiscoverer>>(),
            It.IsAny<IMemoryCache>());

        // Act
        var vm = new DownloadsViewModel(serviceProviderMock.Object, loggerMock.Object, mockNotificationService.Object, mockGitHubDiscoverer.Object);

        // Assert
        await vm.InitializeAsync();
    }
}
