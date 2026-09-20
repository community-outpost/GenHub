using GenHub.Core.Interfaces.Online;
using GenHub.Core.Models.Online;
using GenHub.Features.Online.Services;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text;

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
    /// Tests that a pending-selection config joins successfully without starting the sidecar.
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
