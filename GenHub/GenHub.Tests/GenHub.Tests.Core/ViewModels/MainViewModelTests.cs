using System.Threading.Tasks;
using GenHub.Common.ViewModels;
using GenHub.Core.Interfaces.GameInstallations;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GenHub.Tests.Core.ViewModels;

/// <summary>
/// Contains unit tests for the <see cref="MainViewModel"/> class.
/// </summary>
public class MainViewModelTests
{
    /// <summary>
    /// Tests that <see cref="MainViewModel"/> can be instantiated successfully.
    /// </summary>
    [Fact]
    public void Constructor_CreatesValidInstance()
    {
        // Arrange
        var mockOrchestrator = new Mock<IGameInstallationDetectionOrchestrator>();
        var logger = NullLogger<MainViewModel>.Instance;

        // Act
        var vm = new MainViewModel(mockOrchestrator.Object, logger);

        // Assert
        Assert.NotNull(vm);
        Assert.IsType<MainViewModel>(vm);
    }

    /// <summary>
    /// Verifies ScanForGamesAsync can be called.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task ScanForGamesAsync_CanBeCalled()
    {
        // Arrange
        var mockOrchestrator = new Mock<IGameInstallationDetectionOrchestrator>();
        var logger = NullLogger<MainViewModel>.Instance;
        var viewModel = new MainViewModel(mockOrchestrator.Object, logger);

        // Act & Assert
        await viewModel.ScanForGamesAsync();
        Assert.True(true); // Test passes if no exception is thrown
    }
}
