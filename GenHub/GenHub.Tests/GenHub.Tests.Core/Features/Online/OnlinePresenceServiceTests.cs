using GenHub.Features.Online.Services;
using Microsoft.Extensions.Logging;
using Moq;
using System.Net.Http;

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
        _service = new OnlinePresenceService(
            Mock.Of<IHttpClientFactory>(),
            Mock.Of<ILogger<OnlinePresenceService>>());
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
              {"displayName":"Host","overlayIp":"10.42.0.1","quality":1,"isHost":true,"isLaunched":true},
              {"displayName":"Guest","overlayIp":"10.42.0.2","quality":2,"isHost":false,"isLaunched":false}
            ]}
            """;

        // Act
        var members = OnlinePresenceService.ParseRosterMessage(message);

        // Assert
        Assert.NotNull(members);
        Assert.Equal(2, members.Count);
        Assert.Equal("10.42.0.2", members[1].OverlayIp);
        Assert.True(members[0].IsLaunched);
        Assert.False(members[1].IsLaunched);
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
    /// Tests that a profile-changed event parses into the expected profile.
    /// </summary>
    [Fact]
    public void ParseProfileChangedMessage_WithEvent_ShouldReturnExpected()
    {
        // Arrange
        const string message = """
            {"type":"event","event":"profile-changed","data":{
              "expectedProfileId":"p1",
              "expectedProfileFingerprint":"opf1|zh|mod-a",
              "expectedProfileName":"Zero Hour Plus",
              "expectedGameClientId":"zh",
              "expectedContentIds":["mod-a"]
            }}
            """;

        // Act
        var expected = OnlinePresenceService.ParseProfileChangedMessage(message);

        // Assert
        Assert.NotNull(expected);
        Assert.Equal("opf1|zh|mod-a", expected.ExpectedProfileFingerprint);
        Assert.Equal("Zero Hour Plus", expected.ExpectedProfileName);
        Assert.Equal(["mod-a"], expected.ExpectedContentIds);
    }

    /// <summary>
    /// Tests that other events and rosters return null.
    /// </summary>
    /// <param name="message">The non-profile-changed message.</param>
    [Theory]
    [InlineData("""{"type":"event","event":"report"}""")]
    [InlineData("""{"type":"roster","members":[]}""")]
    [InlineData("not json")]
    public void ParseProfileChangedMessage_WithoutProfileChanged_ShouldReturnNull(string message)
    {
        // Act
        var expected = OnlinePresenceService.ParseProfileChangedMessage(message);

        // Assert
        Assert.Null(expected);
    }

    /// <summary>
    /// Tests that a fresh grant needs no refresh on first connect.
    /// </summary>
    [Fact]
    public void GrantNeedsRefresh_WithFreshGrant_ShouldReturnFalse()
    {
        // Arrange
        var now = new DateTime(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc);

        // Act
        var needsRefresh = OnlinePresenceService.GrantNeedsRefresh(now.AddMinutes(10), now, 0);

        // Assert
        Assert.False(needsRefresh);
    }

    /// <summary>
    /// Tests that a grant inside the lead window refreshes before connecting.
    /// </summary>
    [Fact]
    public void GrantNeedsRefresh_WithExpiringGrant_ShouldReturnTrue()
    {
        // Arrange
        var now = new DateTime(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc);

        // Act
        var needsRefresh = OnlinePresenceService.GrantNeedsRefresh(now.AddSeconds(60), now, 0);

        // Assert
        Assert.True(needsRefresh);
    }

    /// <summary>
    /// Tests that an unknown expiry refreshes on reconnects but not first connect.
    /// </summary>
    /// <param name="attempt">The reconnect attempt number.</param>
    /// <param name="expected">The expected refresh decision.</param>
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public void GrantNeedsRefresh_WithUnknownExpiry_ShouldFollowAttempt(int attempt, bool expected)
    {
        // Arrange
        var now = new DateTime(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc);

        // Act
        var needsRefresh = OnlinePresenceService.GrantNeedsRefresh(default, now, attempt);

        // Assert
        Assert.Equal(expected, needsRefresh);
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

    /// <summary>
    /// Tests that concurrent calls to UpdateAdvertisedProfile do not throw SemaphoreFullException.
    /// </summary>
    [Fact]
    public void UpdateAdvertisedProfile_Concurrently_ShouldNotThrow()
    {
        // Act & Assert
        var exception = Record.Exception(() =>
        {
            Parallel.For(0, 50, _ =>
            {
                _service.UpdateAdvertisedProfile("fp", "profile", "user", false);
            });
        });

        Assert.Null(exception);
    }

    public void Dispose()
    {
        _service.Dispose();
    }
}
