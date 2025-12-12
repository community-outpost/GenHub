using System;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Features.Downloads.ViewModels;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

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
        var serviceProviderMock = new Mock<IServiceProvider>();
        var mockNotificationService = new Mock<INotificationService>();
        var loggerMock = new Mock<ILogger<DownloadsViewModel>>();

        // Act
        var vm = new DownloadsViewModel(
            serviceProviderMock.Object,
            mockNotificationService.Object,
            loggerMock.Object);
        await vm.InitializeAsync();
    }
}
