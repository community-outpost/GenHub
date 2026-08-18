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

    /// <summary>
    /// Verifies that settings.json keys this model does not declare survive a load-modify-save
    /// round trip, because saving replaces the GeneralsOnline client's file wholesale.
    /// </summary>
    [Fact]
    public void RoundTrip_Should_PreserveUnknownKeys()
    {
        // Arrange
        var json = @"
{
 ""show_ping"": true,
 ""auth_token"": ""secret"",
 ""unmodelled_toggle"": false,
 ""camera"": {
  ""min_height"": 100.0,
  ""unmodelled_zoom_step"": 7
 }
}";

        // Act
        var settings = JsonSerializer.Deserialize<GeneralsOnlineSettings>(json, _options);
        Assert.NotNull(settings);
        settings.ShowPing = false;
        var rewritten = JsonSerializer.Serialize(settings, _options);
        var reloaded = JsonSerializer.Deserialize<GeneralsOnlineSettings>(rewritten, _options);

        // Assert
        Assert.NotNull(reloaded);
        Assert.False(reloaded.ShowPing);
        Assert.Equal(100.0f, reloaded.Camera.MinHeight);
        Assert.Equal("secret", reloaded.AdditionalSettings["auth_token"].GetString());
        Assert.False(reloaded.AdditionalSettings["unmodelled_toggle"].GetBoolean());
        Assert.Equal(7, reloaded.Camera.AdditionalSettings["unmodelled_zoom_step"].GetInt32());
    }

    /// <summary>
    /// Verifies that a section spelled as an explicit null, which is valid JSON and overwrites the
    /// property initializer, is restored so that merging into the loaded settings cannot throw.
    /// </summary>
    [Fact]
    public void EnsureNestedSectionsInitialized_Should_ReplaceSectionsDeserializedAsNull()
    {
        // Arrange
        var json = @"{ ""camera"": null, ""chat"": null, ""debug"": null, ""render"": null, ""social"": null }";
        var settings = JsonSerializer.Deserialize<GeneralsOnlineSettings>(json, _options);
        Assert.NotNull(settings);
        Assert.Null(settings.Camera);

        // Act
        settings.EnsureNestedSectionsInitialized();

        // Assert
        Assert.NotNull(settings.Camera);
        Assert.NotNull(settings.Chat);
        Assert.NotNull(settings.Debug);
        Assert.NotNull(settings.Render);
        Assert.NotNull(settings.Social);
    }
}
