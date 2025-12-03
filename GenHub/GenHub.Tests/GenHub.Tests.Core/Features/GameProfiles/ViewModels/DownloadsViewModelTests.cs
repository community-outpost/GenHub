using GenHub.Core.Interfaces.Notifications;
using GenHub.Features.Downloads.ViewModels;
using Moq;

namespace GenHub.Tests.Core.ViewModels;

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
        var mockNotificationService = new Mock<INotificationService>();

        // Act
        var vm = new DownloadsViewModel(mockNotificationService.Object);
        await vm.InitializeAsync();
    }
}