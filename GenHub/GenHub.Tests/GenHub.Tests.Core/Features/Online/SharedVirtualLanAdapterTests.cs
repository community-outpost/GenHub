using GenHub.Core.Interfaces.Online;
using GenHub.Core.Models.Online;
using GenHub.Core.Models.Results;
using GenHub.Features.Online.Services;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Tests.Core.Features.Online;

/// <summary>
/// Unit tests for <see cref="SharedVirtualLanAdapter"/>.
/// </summary>
public class SharedVirtualLanAdapterTests : IDisposable
{
    private readonly SharedVirtualLanAdapter _adapter;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="SharedVirtualLanAdapterTests"/> class.
    /// </summary>
    public SharedVirtualLanAdapterTests()
    {
        _adapter = new SharedVirtualLanAdapter(
            Mock.Of<IOverlaySidecarHost>(),
            Mock.Of<IOverlaySidecarLocator>(),
            Mock.Of<ILogger<SharedVirtualLanAdapter>>());
    }

    /// <summary>
    /// Tests that a pending-selection config without a tunnel runner joins successfully and remains Down.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task BringUpAsync_WithPendingSelection_ShouldSucceedWithoutSidecarAsync()
    {
        // Arrange
        var host = new Mock<IOverlaySidecarHost>(MockBehavior.Strict);
        using var adapter = new SharedVirtualLanAdapter(
            host.Object,
            Mock.Of<IOverlaySidecarLocator>(),
            Mock.Of<ILogger<SharedVirtualLanAdapter>>());
        var pending = Convert.ToBase64String(Encoding.UTF8.GetBytes("""{"v":0,"overlay":"pending-selection"}"""));

        // Act
        var result = await adapter.BringUpAsync(pending, "10.42.0.7");

        // Assert
        Assert.True(result.Success);
        Assert.Equal(OnlineAdapterState.Down, adapter.State);
        host.Verify(h => h.StartAsync(It.IsAny<string>(), It.IsAny<IOverlaySidecarLocator>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Tests that a pending-selection config with a tunnel runner starts the runner and transitions to Up.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task BringUpAsync_WithPendingSelectionAndTunnelRunner_ShouldStartTunnelRunnerAndTransitionToUpAsync()
    {
        // Arrange
        var host = new Mock<IOverlaySidecarHost>(MockBehavior.Strict);
        var runner = new Mock<ITunnelRunner>();
        runner.Setup(r => r.StartAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        using var adapter = new SharedVirtualLanAdapter(
            host.Object,
            Mock.Of<IOverlaySidecarLocator>(),
            Mock.Of<ILogger<SharedVirtualLanAdapter>>(),
            runner.Object);
        var pending = Convert.ToBase64String(Encoding.UTF8.GetBytes("""{"v":0,"overlay":"pending-selection"}"""));

        // Act
        var result = await adapter.BringUpAsync(pending, "10.42.0.7");

        // Assert
        Assert.True(result.Success);
        Assert.Equal(OnlineAdapterState.Up, adapter.State);
        Assert.Equal("10.42.0.7", adapter.OverlayIp);
        host.Verify(h => h.StartAsync(It.IsAny<string>(), It.IsAny<IOverlaySidecarLocator>(), It.IsAny<CancellationToken>()), Times.Never);
        runner.Verify(r => r.StartAsync(pending, "10.42.0.7", It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Tests that a sidecar start success transitions adapter to Up with correct overlay IP.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task BringUpAsync_WithSidecarSuccess_ShouldTransitionToUpAsync()
    {
        // Arrange
        var host = new Mock<IOverlaySidecarHost>();
        host.Setup(h => h.StartAsync(It.IsAny<string>(), It.IsAny<IOverlaySidecarLocator>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<SidecarInfo>.CreateSuccess(new SidecarInfo(1234, "/path/to/config")));

        using var adapter = new SharedVirtualLanAdapter(
            host.Object,
            Mock.Of<IOverlaySidecarLocator>(),
            Mock.Of<ILogger<SharedVirtualLanAdapter>>());
        var config = Convert.ToBase64String(Encoding.UTF8.GetBytes("""{"v":1,"overlay":"genhub-tun"}"""));

        // Act
        var result = await adapter.BringUpAsync(config, "10.42.0.2");

        // Assert
        Assert.True(result.Success);
        Assert.Equal(OnlineAdapterState.Up, adapter.State);
        Assert.Equal("10.42.0.2", adapter.OverlayIp);
    }

    /// <summary>
    /// Tests that when sidecar is not found, adapter falls back to in-process tunnel runner and transitions to Up.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task BringUpAsync_WithSidecarFailureAndTunnelRunnerSuccess_ShouldFallbackToRunnerAndTransitionToUpAsync()
    {
        // Arrange
        var host = new Mock<IOverlaySidecarHost>();
        host.Setup(h => h.StartAsync(It.IsAny<string>(), It.IsAny<IOverlaySidecarLocator>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<SidecarInfo>.CreateFailure("Sidecar binary not installed."));

        var runner = new Mock<ITunnelRunner>();
        runner.Setup(r => r.StartAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        using var adapter = new SharedVirtualLanAdapter(
            host.Object,
            Mock.Of<IOverlaySidecarLocator>(),
            Mock.Of<ILogger<SharedVirtualLanAdapter>>(),
            runner.Object);
        var config = Convert.ToBase64String(Encoding.UTF8.GetBytes("""{"v":1,"overlay":"genhub-tun"}"""));

        // Act
        var result = await adapter.BringUpAsync(config, "10.42.0.5");

        // Assert
        Assert.True(result.Success);
        Assert.Equal(OnlineAdapterState.Up, adapter.State);
        Assert.Equal("10.42.0.5", adapter.OverlayIp);
        runner.Verify(r => r.StartAsync(config, "10.42.0.5", It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Tests that teardown cleans up running components and sets state to Down.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task TearDownAsync_WhenUp_ShouldStopHostAndRunnerAndTransitionToDownAsync()
    {
        // Arrange
        var host = new Mock<IOverlaySidecarHost>();
        host.Setup(h => h.StartAsync(It.IsAny<string>(), It.IsAny<IOverlaySidecarLocator>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<SidecarInfo>.CreateSuccess(new SidecarInfo(1234, "/path/to/config")));
        host.Setup(h => h.StopAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        var runner = new Mock<ITunnelRunner>();
        runner.SetupGet(r => r.IsRunning).Returns(true);
        runner.Setup(r => r.StopAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        using var adapter = new SharedVirtualLanAdapter(
            host.Object,
            Mock.Of<IOverlaySidecarLocator>(),
            Mock.Of<ILogger<SharedVirtualLanAdapter>>(),
            runner.Object);
        var config = Convert.ToBase64String(Encoding.UTF8.GetBytes("""{"v":1,"overlay":"genhub-tun"}"""));

        await adapter.BringUpAsync(config, "10.42.0.2");
        Assert.Equal(OnlineAdapterState.Up, adapter.State);

        // Act
        var tearDownResult = await adapter.TearDownAsync();

        // Assert
        Assert.True(tearDownResult.Success);
        Assert.Equal(OnlineAdapterState.Down, adapter.State);
        Assert.Null(adapter.OverlayIp);
        host.Verify(h => h.StopAsync(It.IsAny<CancellationToken>()), Times.Once);
        runner.Verify(r => r.StopAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _adapter.Dispose();
    }
}
