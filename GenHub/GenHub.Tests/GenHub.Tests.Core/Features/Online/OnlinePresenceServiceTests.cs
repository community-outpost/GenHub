using GenHub.Features.Online.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace GenHub.Tests.Core.Features.Online;

/// <summary>
/// Unit tests for <see cref="OnlinePresenceService"/>.
/// </summary>
public sealed class OnlinePresenceServiceTests : IDisposable
{
    private readonly OnlinePresenceService _service;

    /// <summary>
    /// Initializes a new instance of the <see cref="OnlinePresenceServiceTests"/> class.
    /// </summary>
    public OnlinePresenceServiceTests()
    {
        _service = new OnlinePresenceService(Mock.Of<ILogger<OnlinePresenceService>>());
    }

    /// <summary>
    /// Tests that an https edge maps to a wss presence URI.
    /// </summary>
    [Fact]
    public void BuildPresenceUri_WithHttps_ShouldUseWss()
    {
        // Act
        var uri = OnlinePresenceService.BuildPresenceUri("https://edge.example.test", "net-1", "grant");

        // Assert
        Assert.Equal("wss", uri.Scheme);
        Assert.Equal("edge.example.test", uri.Host);
        Assert.Equal("/v1/networks/net-1/presence", uri.AbsolutePath);
        Assert.Contains("ticket=grant", uri.Query);
    }

    /// <summary>
    /// Tests that an http edge maps to a ws presence URI.
    /// </summary>
    [Fact]
    public void BuildPresenceUri_WithHttp_ShouldUseWs()
    {
        // Act
        var uri = OnlinePresenceService.BuildPresenceUri("http://localhost:8787", "net-1", "grant");

        // Assert
        Assert.Equal("ws", uri.Scheme);
        Assert.Equal(8787, uri.Port);
    }

    /// <summary>
    /// Tests that a roster message parses into members.
    /// </summary>
    [Fact]
    public void ParseRosterMessage_WithRoster_ShouldReturnMembers()
    {
        // Arrange
        const string message = """
            {"type":"roster","members":[
              {"displayName":"Host","overlayIp":"10.42.0.1","quality":1,"isHost":true},
              {"displayName":"Guest","overlayIp":"10.42.0.2","quality":2,"isHost":false}
            ]}
            """;

        // Act
        var members = OnlinePresenceService.ParseRosterMessage(message);

        // Assert
        Assert.NotNull(members);
        Assert.Equal(2, members.Count);
        Assert.Equal("10.42.0.2", members[1].OverlayIp);
    }

    /// <summary>
    /// Tests that a non-roster message returns null.
    /// </summary>
    [Fact]
    public void ParseRosterMessage_WithEvent_ShouldReturnNull()
    {
        // Act
        var members = OnlinePresenceService.ParseRosterMessage("""{"type":"event","event":"report"}""");

        // Assert
        Assert.Null(members);
    }

    /// <summary>
    /// Tests that malformed JSON returns null.
    /// </summary>
    [Fact]
    public void ParseRosterMessage_WithInvalidJson_ShouldReturnNull()
    {
        // Act
        var members = OnlinePresenceService.ParseRosterMessage("not json");

        // Assert
        Assert.Null(members);
    }

    /// <summary>
    /// Tests that connecting without a grant fails validation.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task ConnectAsync_WithBlankGrant_ShouldFailAsync()
    {
        // Act
        var result = await _service.ConnectAsync("net-1", "  ");

        // Assert
        Assert.False(result.Success);
    }

    /// <summary>
    /// Tests that disconnecting while idle succeeds.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task DisconnectAsync_WhenIdle_ShouldSucceedAsync()
    {
        // Act
        var result = await _service.DisconnectAsync();

        // Assert
        Assert.True(result.Success);
        Assert.False(_service.IsConnected);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _service.Dispose();
    }
}
