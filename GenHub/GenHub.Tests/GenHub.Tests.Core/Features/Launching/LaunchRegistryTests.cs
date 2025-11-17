using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Launching;
using GenHub.Features.Launching;
using Microsoft.Extensions.Logging;
using Moq;

namespace GenHub.Tests.Core.Features.Launching;

/// <summary>
/// Unit tests for <see cref="LaunchRegistry"/>.
/// </summary>
public class LaunchRegistryTests
{
    private readonly LaunchRegistry _registry;

    /// <summary>
    /// Initializes a new instance of the <see cref="LaunchRegistryTests"/> class.
    /// </summary>
    public LaunchRegistryTests()
    {
        var loggerMock = new Mock<ILogger<LaunchRegistry>>();
        _registry = new LaunchRegistry(loggerMock.Object);
    }

    /// <summary>
    /// Tests that RegisterLaunchAsync adds launch info to the registry.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task RegisterLaunchAsync_ShouldAddLaunchInfo()
    {
        // Arrange
        var launchInfo = new GameLaunchInfo
        {
            LaunchId = Guid.NewGuid().ToString(),
            ProfileId = "profile1",
            WorkspaceId = "workspace1",
            ProcessInfo = new GameProcessInfo { ProcessId = 123 },
            LaunchedAt = DateTime.UtcNow,
        };

        // Act
        await _registry.RegisterLaunchAsync(launchInfo);
        var result = await _registry.GetLaunchInfoAsync(launchInfo.LaunchId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(launchInfo.LaunchId, result.LaunchId);
    }

    /// <summary>
    /// Tests that GetLaunchInfoAsync returns null for non-existent launch ID.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task GetLaunchInfoAsync_WithNonExistentId_ShouldReturnNull()
    {
        // Act
        var result = await _registry.GetLaunchInfoAsync("non-existent-id");

        // Assert
        Assert.Null(result);
    }

    /// <summary>
    /// Tests that UnregisterLaunchAsync removes launch info from the registry.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task UnregisterLaunchAsync_ShouldRemoveLaunchInfo()
    {
        // Arrange
        var launchInfo = new GameLaunchInfo
        {
            LaunchId = Guid.NewGuid().ToString(),
            ProfileId = "profile1",
            WorkspaceId = "workspace1",
            ProcessInfo = new GameProcessInfo { ProcessId = 123 },
            LaunchedAt = DateTime.UtcNow,
        };
        await _registry.RegisterLaunchAsync(launchInfo);

        // Act
        await _registry.UnregisterLaunchAsync(launchInfo.LaunchId);
        var result = await _registry.GetLaunchInfoAsync(launchInfo.LaunchId);

        // Assert
        Assert.Null(result);
    }
}