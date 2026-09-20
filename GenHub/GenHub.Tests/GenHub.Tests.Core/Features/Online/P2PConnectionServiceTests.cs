using GenHub.Core.Models.Online;
using GenHub.Features.Online.Services;
using Microsoft.Extensions.Logging;
using Moq;
using System.Net;
using System.Net.Sockets;

namespace GenHub.Tests.Core.Features.Online;

/// <summary>
/// Unit tests for <see cref="P2PConnectionService"/>.
/// </summary>
public sealed class P2PConnectionServiceTests : IDisposable
{
    private readonly P2PConnectionService _service;

    /// <summary>
    /// Initializes a new instance of the <see cref="P2PConnectionServiceTests"/> class.
    /// </summary>
    public P2PConnectionServiceTests()
    {
        _service = new P2PConnectionService(Mock.Of<ILogger<P2PConnectionService>>());
    }

    /// <summary>
    /// Tests that starting on an ephemeral port binds successfully.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task StartListeningAsync_WithEphemeralPort_ShouldBindAsync()
    {
        // Act
        var result = await _service.StartListeningAsync(0);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.True(result.Data.Port > 0);
        Assert.Equal(OnlineConnectionQuality.Connecting, _service.CurrentQuality);
    }

    /// <summary>
    /// Tests that an out-of-range port fails validation.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task StartListeningAsync_WithInvalidPort_ShouldFailAsync()
    {
        // Act
        var result = await _service.StartListeningAsync(70000);

        // Assert
        Assert.False(result.Success);
    }

    /// <summary>
    /// Tests that STUN candidates try IPv4 before IPv6.
    /// </summary>
    [Fact]
    public void OrderStunCandidates_WithMixedFamilies_ShouldPreferIpv4()
    {
        // Arrange
        var addresses = new[] { IPAddress.IPv6Loopback, IPAddress.Parse("203.0.113.7"), IPAddress.Loopback };

        // Act
        var ordered = P2PConnectionService.OrderStunCandidates(addresses);

        // Assert
        Assert.Equal(3, ordered.Count);
        Assert.Equal(AddressFamily.InterNetwork, ordered[0].AddressFamily);
        Assert.Equal(AddressFamily.InterNetwork, ordered[1].AddressFamily);
        Assert.Equal(AddressFamily.InterNetworkV6, ordered[2].AddressFamily);
    }

    /// <summary>
    /// Tests that connecting without a listener fails.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task ConnectToPeerAsync_WithoutListener_ShouldFailAsync()
    {
        // Act
        var result = await _service.ConnectToPeerAsync("127.0.0.1", 4321);

        // Assert
        Assert.False(result.Success);
    }

    /// <summary>
    /// Tests that connecting to loopback after listening succeeds.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task ConnectToPeerAsync_AfterListening_ShouldSucceedAsync()
    {
        // Arrange
        await _service.StartListeningAsync(0);

        // Act
        var result = await _service.ConnectToPeerAsync("127.0.0.1", 4321);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(OnlineConnectionQuality.Connecting, _service.CurrentQuality);
    }

    /// <summary>
    /// Tests that an invalid peer address fails validation.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task ConnectToPeerAsync_WithInvalidAddress_ShouldFailAsync()
    {
        // Arrange
        await _service.StartListeningAsync(0);

        // Act
        var result = await _service.ConnectToPeerAsync("not-an-ip", 4321);

        // Assert
        Assert.False(result.Success);
    }

    /// <summary>
    /// Tests that stopping resets quality to unknown.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task StopListeningAsync_AfterStart_ShouldResetQualityAsync()
    {
        // Arrange
        await _service.StartListeningAsync(0);

        // Act
        var result = await _service.StopListeningAsync();

        // Assert
        Assert.True(result.Success);
        Assert.Equal(OnlineConnectionQuality.Unknown, _service.CurrentQuality);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _service.Dispose();
    }
}
