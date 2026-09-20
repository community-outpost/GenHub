using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Online;
using GenHub.Features.Online.Services;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text;

namespace GenHub.Tests.Core.Features.Online;

/// <summary>
/// Unit tests for <see cref="OverlaySidecarHost"/> and <see cref="OverlayConfigInspector"/>.
/// </summary>
public sealed class OverlaySidecarHostTests : IDisposable
{
    private readonly OverlaySidecarHost _host;

    /// <summary>
    /// Initializes a new instance of the <see cref="OverlaySidecarHostTests"/> class.
    /// </summary>
    public OverlaySidecarHostTests()
    {
        _host = new OverlaySidecarHost(Mock.Of<ILogger<OverlaySidecarHost>>());
    }

    /// <summary>
    /// Tests that an empty configuration fails validation.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task StartAsync_WithEmptyConfig_ShouldFailAsync()
    {
        // Act
        var result = await _host.StartAsync("  ", Mock.Of<IOverlaySidecarLocator>());

        // Assert
        Assert.False(result.Success);
        Assert.False(_host.IsRunning);
    }

    /// <summary>
    /// Tests that a missing binary fails with a not-installed error.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task StartAsync_WithMissingBinary_ShouldFailAsync()
    {
        // Arrange
        var locator = new Mock<IOverlaySidecarLocator>();
        locator.Setup(l => l.LocateBinary()).Returns((string?)null);

        // Act
        var result = await _host.StartAsync("{}", locator.Object);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("not installed", result.Errors[0]);
        Assert.False(_host.IsRunning);
    }

    /// <summary>
    /// Tests that a binary exiting during startup fails the start.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task StartAsync_WithEarlyExit_ShouldFailAsync()
    {
        // Arrange
        var locator = new Mock<IOverlaySidecarLocator>();
        locator.Setup(l => l.LocateBinary()).Returns("dotnet");
        locator.Setup(l => l.BuildArguments(It.IsAny<string>())).Returns("--info");

        // Act
        var result = await _host.StartAsync("{}", locator.Object);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("exited during startup", result.Errors[0]);
        Assert.False(_host.IsRunning);
    }

    /// <summary>
    /// Tests that stopping while idle succeeds.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task StopAsync_WhenIdle_ShouldSucceedAsync()
    {
        // Act
        var result = await _host.StopAsync();

        // Assert
        Assert.True(result.Success);
        Assert.False(_host.IsRunning);
    }

    /// <summary>
    /// Tests that the inspector reads the pending-selection overlay name.
    /// </summary>
    [Fact]
    public void Inspector_WithPendingConfig_ShouldReturnName()
    {
        // Arrange
        var config = Convert.ToBase64String(Encoding.UTF8.GetBytes("""{"v":0,"overlay":"pending-selection"}"""));

        // Act
        var name = OverlayConfigInspector.TryGetOverlayName(config);

        // Assert
        Assert.Equal("pending-selection", name);
    }

    /// <summary>
    /// Tests that the inspector returns null for unreadable payloads.
    /// </summary>
    /// <param name="config">The adapter configuration.</param>
    [Theory]
    [InlineData("")]
    [InlineData("not-base64!!!")]
    [InlineData("e30=")]
    public void Inspector_WithUnreadableConfig_ShouldReturnNull(string config)
    {
        // Act
        var name = OverlayConfigInspector.TryGetOverlayName(config);

        // Assert
        Assert.Null(name);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _host.Dispose();
    }
}
