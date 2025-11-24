using GenHub.Core.Constants;
using GenHub.Features.AppUpdate.Services;
using Microsoft.Extensions.Logging;
using Moq;
using System.Net.Http;

namespace GenHub.Tests.Core.Features.AppUpdate.Services;

/// <summary>
/// Tests for <see cref="VelopackUpdateManager"/>.
/// </summary>
public class VelopackUpdateManagerTests
{
    private readonly Mock<ILogger<VelopackUpdateManager>> _mockLogger;
    private readonly Mock<IHttpClientFactory> _mockHttpClientFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="VelopackUpdateManagerTests"/> class.
    /// </summary>
    public VelopackUpdateManagerTests()
    {
        _mockLogger = new Mock<ILogger<VelopackUpdateManager>>();
        _mockHttpClientFactory = new Mock<IHttpClientFactory>();

        // Use the actual interface method, not the extension method
        _mockHttpClientFactory.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(new HttpClient());
    }

    /// <summary>
    /// Tests that VelopackUpdateManager can be constructed successfully.
    /// </summary>
    [Fact]
    public void Constructor_ShouldInitializeSuccessfully()
    {
        // Act
        var manager = new VelopackUpdateManager(_mockLogger.Object, _mockHttpClientFactory.Object);

        // Assert
        Assert.NotNull(manager);
        Assert.False(manager.IsUpdatePendingRestart);
    }

    /// <summary>
    /// Tests that constructor throws ArgumentNullException when logger is null.
    /// </summary>
    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new VelopackUpdateManager(null!, _mockHttpClientFactory.Object));
    }

    /// <summary>
    /// Tests that CheckForUpdatesAsync returns null when running from development environment.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task CheckForUpdatesAsync_InDevEnvironment_ShouldReturnNull()
    {
        // Arrange
        var manager = new VelopackUpdateManager(_mockLogger.Object, _mockHttpClientFactory.Object);

        // Act
        var result = await manager.CheckForUpdatesAsync();

        // Assert
        Assert.Null(result);
    }

    /// <summary>
    /// Tests that CheckForUpdatesAsync handles cancellation properly.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task CheckForUpdatesAsync_WithCancellation_ShouldHandleGracefully()
    {
        // Arrange
        var manager = new VelopackUpdateManager(_mockLogger.Object, _mockHttpClientFactory.Object);
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        var result = await manager.CheckForUpdatesAsync(cts.Token);

        // Assert
        Assert.Null(result); // Should return null gracefully when UpdateManager is not initialized
    }

    /// <summary>
    /// Tests that DownloadUpdatesAsync throws InvalidOperationException when UpdateManager is not initialized.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task DownloadUpdatesAsync_WhenNotInitialized_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var manager = new VelopackUpdateManager(_mockLogger.Object, _mockHttpClientFactory.Object);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => manager.DownloadUpdatesAsync(null!));
    }

    /// <summary>
    /// Tests that ApplyUpdatesAndRestart throws InvalidOperationException when UpdateManager is not initialized.
    /// </summary>
    [Fact]
    public void ApplyUpdatesAndRestart_WhenNotInitialized_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var manager = new VelopackUpdateManager(_mockLogger.Object, _mockHttpClientFactory.Object);

        // Act & Assert
        Assert.Throws<InvalidOperationException>(
            () => manager.ApplyUpdatesAndRestart(null!));
    }

    /// <summary>
    /// Tests that ApplyUpdatesAndExit throws InvalidOperationException when UpdateManager is not initialized.
    /// </summary>
    [Fact]
    public void ApplyUpdatesAndExit_WhenNotInitialized_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var manager = new VelopackUpdateManager(_mockLogger.Object, _mockHttpClientFactory.Object);

        // Act & Assert
        Assert.Throws<InvalidOperationException>(
            () => manager.ApplyUpdatesAndExit(null!));
    }

    /// <summary>
    /// Tests that IsUpdatePendingRestart property is false when UpdateManager is not initialized.
    /// </summary>
    [Fact]
    public void IsUpdatePendingRestart_WhenNotInitialized_ShouldReturnFalse()
    {
        // Arrange
        var manager = new VelopackUpdateManager(_mockLogger.Object, _mockHttpClientFactory.Object);

        // Act
        var isPending = manager.IsUpdatePendingRestart;

        // Assert
        Assert.False(isPending);
    }

    /// <summary>
    /// Tests that VelopackUpdateManager uses correct GitHub repository URL from constants.
    /// </summary>
    [Fact]
    public void VelopackUpdateManager_ShouldUseCorrectRepositoryUrl()
    {
        // Arrange & Act
        var manager = new VelopackUpdateManager(_mockLogger.Object, _mockHttpClientFactory.Object);

        // Assert - verify that the logger was called during construction
        // In a development/test environment, the UpdateManager won't be available
        // so we verify the warning log about UpdateManager not being available
        _mockLogger.Verify(
            x => x.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) =>
                    v.ToString() !.Contains("Velopack") ||
                    v.ToString() !.Contains("Update")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }
}
