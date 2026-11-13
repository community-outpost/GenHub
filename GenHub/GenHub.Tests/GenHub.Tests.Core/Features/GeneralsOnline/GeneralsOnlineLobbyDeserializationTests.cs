using GenHub.Core.Models.GeneralsOnline;
using System.Text.Json;

namespace GenHub.Tests.Core.Features.GeneralsOnline;

/// <summary>
/// Verifies the Generals Online backend JSON contract mapping.
/// </summary>
public class GeneralsOnlineLobbyDeserializationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Tests that the lobby list payload maps backend names to lobby properties.
    /// </summary>
    [Fact]
    public void LobbiesResult_ShouldMapBackendPropertyNames()
    {
        // Arrange
        const string json = """
            {
              "lobbies": [
                {
                  "LobbyID": 9842,
                  "Owner": 1052,
                  "Name": "[EU] Pro 1v1 Tourney",
                  "State": 0,
                  "MapName": "Tournament Desert",
                  "MapPath": "Maps\\Tournament Desert",
                  "IsMapOfficial": true,
                  "IsPassworded": false,
                  "AllowObservers": true,
                  "ExeCRC": 2948194012,
                  "IniCRC": 1048576021,
                  "MaximumCameraHeight": 310,
                  "Members": [
                    { "UserID": 1052, "DisplayName": "GeneralAlex", "SlotIndex": 0, "SlotState": 5, "Side": 1, "Color": 2, "Region": "Europe" },
                    { "UserID": -1, "DisplayName": "Open", "SlotIndex": 1, "SlotState": 0, "Side": 0, "Color": 0, "Region": "Unknown" }
                  ]
                }
              ],
              "latencies": [42, 68, 120]
            }
            """;

        // Act
        var result = JsonSerializer.Deserialize<GeneralsOnlineLobbiesResult>(json, JsonOptions);

        // Assert
        Assert.NotNull(result);
        var lobby = Assert.Single(result.Lobbies);
        Assert.Equal(9842, lobby.LobbyId);
        Assert.Equal(1052, lobby.Owner);
        Assert.Equal("[EU] Pro 1v1 Tourney", lobby.Name);
        Assert.Equal(GeneralsOnlineLobbyState.GameSetup, lobby.State);
        Assert.Equal("Tournament Desert", lobby.MapName);
        Assert.True(lobby.IsMapOfficial);
        Assert.False(lobby.IsPassworded);
        Assert.Equal(2_948_194_012U, lobby.ExeCrc);
        Assert.Equal(1_048_576_021U, lobby.IniCrc);
        Assert.Equal("GeneralAlex", lobby.HostName);
        Assert.Equal(1, lobby.PlayerCount);
        Assert.Equal(2, lobby.MaxPlayers);
        Assert.True(lobby.IsWaiting);
        Assert.False(lobby.IsInProgress);
        Assert.Equal([42, 68, 120], result.Latencies);
    }

    /// <summary>
    /// Tests that a lobby payload with null members deserializes safely into an empty list.
    /// </summary>
    [Fact]
    public void Lobby_WhenMembersIsNull_ShouldNormalizeToEmptyCollection()
    {
        // Arrange
        const string json = """{ "LobbyID": 100, "Members": null }""";

        // Act
        var lobby = JsonSerializer.Deserialize<GeneralsOnlineLobby>(json, JsonOptions);

        // Assert
        Assert.NotNull(lobby);
        Assert.NotNull(lobby.Members);
        Assert.Empty(lobby.Members);
        Assert.Equal(0, lobby.PlayerCount);
        Assert.Equal(0, lobby.OccupiedCount);
        Assert.Equal(0, lobby.OpenSlotCount);
        Assert.Equal(0, lobby.MaxPlayers);
        Assert.False(lobby.IsFull);
    }

    /// <summary>
    /// Tests that the login payload maps backend names to login properties.
    /// </summary>
    [Fact]
    public void LoginResult_ShouldMapBackendPropertyNames()
    {
        // Arrange
        const string json = """
            {
              "result": 1,
              "session_token": "session-jwt",
              "refresh_token": "refresh-token",
              "user_id": 12345,
              "display_name": "PlayerName",
              "ban_reason": "",
              "ws_uri": "wss://api.playgenerals.online/ws"
            }
            """;

        // Act
        var result = JsonSerializer.Deserialize<LoginResult>(json, JsonOptions);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(PendingLoginState.LoginSuccess, result.Result);
        Assert.True(result.IsSuccess);
        Assert.False(result.IsPending);
        Assert.False(result.IsFailed);
        Assert.Equal("session-jwt", result.SessionToken);
        Assert.Equal("refresh-token", result.RefreshToken);
        Assert.Equal(12345, result.UserId);
        Assert.Equal("PlayerName", result.DisplayName);
        Assert.Equal("wss://api.playgenerals.online/ws", result.WebSocketUri);
    }

    /// <summary>
    /// Tests that a waiting login payload reports the pending state.
    /// </summary>
    [Fact]
    public void LoginResult_WhenWaiting_ShouldReportPending()
    {
        // Arrange
        const string json = """{ "result": 0, "session_token": "", "refresh_token": "", "user_id": -1, "display_name": "", "ban_reason": "", "ws_uri": "" }""";

        // Act
        var result = JsonSerializer.Deserialize<LoginResult>(json, JsonOptions);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.IsPending);
        Assert.False(result.IsSuccess);
    }

    /// <summary>
    /// Tests that two players on distinct teams render as 1v1.
    /// </summary>
    [Fact]
    public void TeamsText_WhenTwoDistinctTeams_ShouldReturn1v1()
    {
        var lobby = new GeneralsOnlineLobby
        {
            Members =
            [
                new GeneralsOnlineLobbyMember { SlotState = GeneralsOnlineSlotState.SlotPlayer, Team = 1 },
                new GeneralsOnlineLobbyMember { SlotState = GeneralsOnlineSlotState.SlotPlayer, Team = 2 },
            ],
        };

        Assert.Equal("1v1", lobby.TeamsText);
    }

    /// <summary>
    /// Tests that four players split across two teams render as 2v2.
    /// </summary>
    [Fact]
    public void TeamsText_WhenTwoVsTwo_ShouldReturn2v2()
    {
        var lobby = new GeneralsOnlineLobby
        {
            Members =
            [
                new GeneralsOnlineLobbyMember { SlotState = GeneralsOnlineSlotState.SlotPlayer, Team = 1 },
                new GeneralsOnlineLobbyMember { SlotState = GeneralsOnlineSlotState.SlotPlayer, Team = 1 },
                new GeneralsOnlineLobbyMember { SlotState = GeneralsOnlineSlotState.SlotPlayer, Team = 2 },
                new GeneralsOnlineLobbyMember { SlotState = GeneralsOnlineSlotState.SlotPlayer, Team = 2 },
            ],
        };

        Assert.Equal("2v2", lobby.TeamsText);
    }

    /// <summary>
    /// Tests that free-for-all configurations return FFA.
    /// </summary>
    [Fact]
    public void TeamsText_WhenFreeForAll_ShouldReturnFFA()
    {
        var lobby = new GeneralsOnlineLobby
        {
            Members =
            [
                new GeneralsOnlineLobbyMember { SlotState = GeneralsOnlineSlotState.SlotPlayer, Team = -1 },
                new GeneralsOnlineLobbyMember { SlotState = GeneralsOnlineSlotState.SlotPlayer, Team = -1 },
            ],
        };

        Assert.Equal("FFA", lobby.TeamsText);
    }
}
