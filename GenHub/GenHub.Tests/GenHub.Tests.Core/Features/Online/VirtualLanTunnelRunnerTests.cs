using GenHub.Core.Services.Online;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Text;
using System.Threading.Tasks;

namespace GenHub.Tests.Core.Features.Online;

/// <summary>
/// Unit tests for <see cref="VirtualLanTunnelRunner"/>.
/// </summary>
public class VirtualLanTunnelRunnerTests : IDisposable
{
    private readonly VirtualLanTunnelRunner _runner;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="VirtualLanTunnelRunnerTests"/> class.
    /// </summary>
    public VirtualLanTunnelRunnerTests()
    {
        _runner = new VirtualLanTunnelRunner(Mock.Of<ILogger<VirtualLanTunnelRunner>>());
    }

    /// <summary>
    /// Tests that starting with a valid configuration marks the runner as running.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task StartAsync_WithValidConfig_ShouldStartAndSetIsRunningAsync()
    {
        // Arrange
        var configJson = """
        {
            "v": 1,
            "overlay": "genhub-tun",
            "subnet": "10.42.0.0/20",
            "overlayIp": "10.42.0.2",
            "networkId": "2c36769e-814b-4818-adbc-b42cedd00729",
            "relay": {
                "host": "127.0.0.1",
                "port": 58088
            }
        }
        """;
        var base64Config = Convert.ToBase64String(Encoding.UTF8.GetBytes(configJson));

        // Act
        var result = await _runner.StartAsync(base64Config, "10.42.0.2");

        // Assert
        Assert.True(result.Success);
        Assert.True(_runner.IsRunning);

        // Teardown
        var stopResult = await _runner.StopAsync();
        Assert.True(stopResult.Success);
        Assert.False(_runner.IsRunning);
    }

    /// <summary>
    /// Tests that starting with a DNS hostname relay resolves asynchronously and starts.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task StartAsync_WithHostnameRelay_ShouldResolveEndpointAndStartAsync()
    {
        // Arrange
        var configJson = """
        {
            "v": 1,
            "overlay": "genhub-tun",
            "networkId": "2c36769e-814b-4818-adbc-b42cedd00729",
            "overlayIp": "10.42.0.2",
            "relay": {
                "host": "localhost",
                "port": 58089
            }
        }
        """;
        var base64Config = Convert.ToBase64String(Encoding.UTF8.GetBytes(configJson));

        // Act
        var result = await _runner.StartAsync(base64Config, "10.42.0.2");

        // Assert
        Assert.True(result.Success);
        Assert.True(_runner.IsRunning);

        var stopResult = await _runner.StopAsync();
        Assert.True(stopResult.Success);
        Assert.False(_runner.IsRunning);
    }

    /// <summary>
    /// Tests that starting an already running runner returns success idempotently.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task StartAsync_WhenAlreadyRunning_ShouldBeIdempotentAsync()
    {
        // Arrange
        var configJson = """
        {
            "v": 1,
            "networkId": "2c36769e-814b-4818-adbc-b42cedd00729",
            "overlayIp": "10.42.0.2",
            "relay": { "host": "127.0.0.1", "port": 58090 }
        }
        """;
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(configJson));

        await _runner.StartAsync(base64, "10.42.0.2");
        Assert.True(_runner.IsRunning);

        // Act
        var secondStart = await _runner.StartAsync(base64, "10.42.0.2");

        // Assert
        Assert.True(secondStart.Success);
        Assert.True(_runner.IsRunning);

        await _runner.StopAsync();
    }

    /// <summary>
    /// Tests that starting with invalid config fails gracefully.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task StartAsync_WithInvalidConfig_ShouldReturnFailureAsync()
    {
        // Act
        var result = await _runner.StartAsync("invalid-base64-and-not-json-{{{", "10.42.0.2");

        // Assert
        Assert.False(result.Success);
        Assert.False(_runner.IsRunning);
    }

    /// <summary>
    /// Tests that calling StartAsync on a disposed runner throws ObjectDisposedException.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task StartAsync_WhenDisposed_ThrowsObjectDisposedExceptionAsync()
    {
        // Arrange
        _runner.Dispose();

        // Act & Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(() => _runner.StartAsync("{}", "10.42.0.2"));
    }

    /// <summary>
    /// Tests that calling Dispose multiple times is safe and idempotent.
    /// </summary>
    [Fact]
    public void Dispose_WhenCalledMultipleTimes_IsIdempotent()
    {
        // Act & Assert
        _runner.Dispose();
        _runner.Dispose();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _runner.Dispose();
    }
}
