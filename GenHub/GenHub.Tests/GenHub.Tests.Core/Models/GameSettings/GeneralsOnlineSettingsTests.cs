using System.Text.Json;
using GenHub.Core.Models.GameSettings;
using Xunit;

namespace GenHub.Tests.Core.Models.GameSettings;

/// <summary>
/// Tests for the <see cref="GeneralsOnlineSettings"/> class.
/// </summary>
public class GeneralsOnlineSettingsTests
{
    private static readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    /// <summary>
    /// Verifies that deserialization correctly handles the nested structure and snake_case naming.
    /// </summary>
    [Fact]
    public void Deserialization_Should_HandleNestedStructure()
    {
        // Arrange
        var json = @"
{
 ""camera"": {
  ""max_height_only_when_lobby_host"": 310.0,
  ""min_height"": 100.0,
  ""move_speed_ratio"": 1.0
 },
 ""chat"": {
  ""duration_seconds_until_fade_out"": 30
 },
 ""debug"": {
  ""verbose_logging"": false
 },
 ""render"": {
  ""fps_limit"": 60,
  ""limit_framerate"": true,
  ""stats_overlay"": true
 },
 ""social"": {
  ""notification_friend_comes_online_gameplay"": true,
  ""notification_friend_comes_online_menus"": true,
  ""notification_friend_goes_offline_gameplay"": true,
  ""notification_friend_goes_offline_menus"": true,
  ""notification_player_accepts_request_gameplay"": true,
  ""notification_player_accepts_request_menus"": true,
  ""notification_player_sends_request_gameplay"": true,
  ""notification_player_sends_request_menus"": true
 }
}";

        // Act
        var settings = JsonSerializer.Deserialize<GeneralsOnlineSettings>(json, _options);

        // Assert
        Assert.NotNull(settings);
        Assert.Equal(310.0f, settings.Camera.MaxHeightOnlyWhenLobbyHost);
        Assert.Equal(100.0f, settings.Camera.MinHeight);
        Assert.Equal(1.0f, settings.Camera.MoveSpeedRatio);
        Assert.Equal(30, settings.Chat.DurationSecondsUntilFadeOut);
        Assert.False(settings.Debug.VerboseLogging);
        Assert.Equal(60, settings.Render.FpsLimit);
        Assert.True(settings.Render.LimitFramerate);
        Assert.True(settings.Render.StatsOverlay);
        Assert.True(settings.Social.NotificationFriendComesOnlineGameplay);
    }

    /// <summary>
    /// Verifies that serialization produces the expected nested snake_case JSON structure.
    /// </summary>
    [Fact]
    public void Serialization_Should_ProduceNestedSnakeCase()
    {
        // Arrange
        var settings = new GeneralsOnlineSettings();
        settings.Camera.MinHeight = 123.4f;
        settings.Render.FpsLimit = 144;
        settings.Debug.VerboseLogging = true;

        // Act
        var json = JsonSerializer.Serialize(settings, _options);

        // Assert
        Assert.Contains("\"camera\": {", json);
        Assert.Contains("\"min_height\": 123.4", json);
        Assert.Contains("\"fps_limit\": 144", json);
        Assert.Contains("\"verbose_logging\": true", json);
    }
}
