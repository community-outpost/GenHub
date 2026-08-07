using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.GameSettings;

namespace GenHub.Tests.Core.Helpers;

/// <summary>
/// Tests for the <see cref="GameSettingsMapper"/> class.
/// </summary>
public class GameSettingsMapperTests
{
    /// <summary>
    /// Verifies that all texture quality levels map to the correct engine values.
    /// </summary>
    /// <param name="quality">The texture quality level.</param>
    /// <param name="expectedReduction">The expected texture reduction value in Options.ini.</param>
    [Theory]
    [InlineData(TextureQuality.Low, GameSettingsConstants.TextureQuality.TextureReductionLow)]
    [InlineData(TextureQuality.Medium, GameSettingsConstants.TextureQuality.TextureReductionMedium)]
    [InlineData(TextureQuality.High, GameSettingsConstants.TextureQuality.TextureReductionHigh)]
    [InlineData(TextureQuality.VeryHigh, GameSettingsConstants.TextureQuality.TextureReductionHigh)]
    public void ApplyToOptions_AllTextureQualities_SetsCorrectReduction(TextureQuality quality, int expectedReduction)
    {
        // Arrange
        var profile = new GameProfile
        {
            VideoTextureQuality = quality,
        };
        var options = new IniOptions();

        // Act
        GameSettingsMapper.ApplyToOptions(profile, options);

        // Assert
        Assert.Equal(expectedReduction, options.Video.TextureReduction);
    }

    /// <summary>
    /// Verifies that mapping from engine values correctly results in the expected texture quality.
    /// </summary>
    /// <param name="reduction">The texture reduction value from Options.ini.</param>
    /// <param name="expectedQuality">The expected texture quality level.</param>
    [Theory]
    [InlineData(GameSettingsConstants.TextureQuality.TextureReductionLow, TextureQuality.Low)]
    [InlineData(GameSettingsConstants.TextureQuality.TextureReductionMedium, TextureQuality.Medium)]
    [InlineData(GameSettingsConstants.TextureQuality.TextureReductionHigh, TextureQuality.High)]
    public void ApplyFromOptions_AllReductions_MapsToCorrectQuality(int reduction, TextureQuality expectedQuality)
    {
        // Arrange
        var options = new IniOptions();
        options.Video.TextureReduction = reduction;
        var profile = new GameProfile();

        // Act
        GameSettingsMapper.ApplyFromOptions(options, profile);

        // Assert
        Assert.Equal(expectedQuality, profile.VideoTextureQuality);
    }

    /// <summary>
    /// Verifies that a profile with no TheSuperHackers font sizes set falls back to the declared defaults.
    /// </summary>
    [Fact]
    public void ApplyToGeneralsOnlineSettings_UnsetFontSizes_UsesDeclaredDefaults()
    {
        // Arrange - seed with values the mapper must overwrite, so a missing assignment fails
        var profile = new GameProfile();
        var settings = new GeneralsOnlineSettings
        {
            SystemTimeFontSize = 99,
            NetworkLatencyFontSize = 99,
            RenderFpsFontSize = 99,
            ResolutionFontAdjustment = 99,
        };

        // Act
        GameSettingsMapper.ApplyToGeneralsOnlineSettings(profile, settings);

        // Assert
        Assert.Equal(GameSettingsTheSuperHackersConstants.DefaultSystemTimeFontSize, settings.SystemTimeFontSize);
        Assert.Equal(GameSettingsTheSuperHackersConstants.DefaultNetworkLatencyFontSize, settings.NetworkLatencyFontSize);
        Assert.Equal(GameSettingsTheSuperHackersConstants.DefaultRenderFpsFontSize, settings.RenderFpsFontSize);
        Assert.Equal(GameSettingsTheSuperHackersConstants.DefaultResolutionFontAdjustment, settings.ResolutionFontAdjustment);
    }

    /// <summary>
    /// Verifies that the fallback defaults match the values declared on the settings model itself.
    /// </summary>
    [Fact]
    public void ApplyToGeneralsOnlineSettings_UnsetFontSizes_MatchesModelDefaults()
    {
        // Arrange - seed with values the mapper must overwrite, so a missing assignment fails
        var profile = new GameProfile();
        var expected = new GeneralsOnlineSettings();
        var settings = new GeneralsOnlineSettings
        {
            SystemTimeFontSize = 99,
            NetworkLatencyFontSize = 99,
            RenderFpsFontSize = 99,
            ResolutionFontAdjustment = 99,
        };

        // Act
        GameSettingsMapper.ApplyToGeneralsOnlineSettings(profile, settings);

        // Assert
        Assert.Equal(expected.SystemTimeFontSize, settings.SystemTimeFontSize);
        Assert.Equal(expected.NetworkLatencyFontSize, settings.NetworkLatencyFontSize);
        Assert.Equal(expected.RenderFpsFontSize, settings.RenderFpsFontSize);
        Assert.Equal(expected.ResolutionFontAdjustment, settings.ResolutionFontAdjustment);
    }

    /// <summary>
    /// Verifies that explicit TheSuperHackers font sizes on the profile are written through unchanged.
    /// </summary>
    [Fact]
    public void ApplyToGeneralsOnlineSettings_ExplicitFontSizes_ArePreserved()
    {
        // Arrange
        var profile = new GameProfile
        {
            TshSystemTimeFontSize = 20,
            TshNetworkLatencyFontSize = 21,
            TshRenderFpsFontSize = 22,
            TshResolutionFontAdjustment = 23,
        };
        var settings = new GeneralsOnlineSettings();

        // Act
        GameSettingsMapper.ApplyToGeneralsOnlineSettings(profile, settings);

        // Assert
        Assert.Equal(20, settings.SystemTimeFontSize);
        Assert.Equal(21, settings.NetworkLatencyFontSize);
        Assert.Equal(22, settings.RenderFpsFontSize);
        Assert.Equal(23, settings.ResolutionFontAdjustment);
    }

    /// <summary>
    /// Verifies that a profile with no cursor capture, edge scroll or observer toggles set
    /// falls back to the declared defaults.
    /// </summary>
    [Fact]
    public void ApplyToGeneralsOnlineSettings_UnsetToggles_UsesDeclaredDefaults()
    {
        // Arrange - seed each toggle inverted, so a missing assignment fails
        var profile = new GameProfile();
        var settings = new GeneralsOnlineSettings
        {
            PlayerObserverEnabled = false,
            CursorCaptureEnabledInFullscreenGame = false,
            CursorCaptureEnabledInFullscreenMenu = false,
            CursorCaptureEnabledInWindowedGame = false,
            CursorCaptureEnabledInWindowedMenu = true,
            ScreenEdgeScrollEnabledInFullscreenApp = false,
            ScreenEdgeScrollEnabledInWindowedApp = true,
        };

        // Act
        GameSettingsMapper.ApplyToGeneralsOnlineSettings(profile, settings);

        // Assert
        Assert.Equal(GameSettingsTheSuperHackersConstants.DefaultPlayerObserverEnabled, settings.PlayerObserverEnabled);
        Assert.Equal(GameSettingsTheSuperHackersConstants.DefaultCursorCaptureEnabledInFullscreenGame, settings.CursorCaptureEnabledInFullscreenGame);
        Assert.Equal(GameSettingsTheSuperHackersConstants.DefaultCursorCaptureEnabledInFullscreenMenu, settings.CursorCaptureEnabledInFullscreenMenu);
        Assert.Equal(GameSettingsTheSuperHackersConstants.DefaultCursorCaptureEnabledInWindowedGame, settings.CursorCaptureEnabledInWindowedGame);
        Assert.Equal(GameSettingsTheSuperHackersConstants.DefaultCursorCaptureEnabledInWindowedMenu, settings.CursorCaptureEnabledInWindowedMenu);
        Assert.Equal(GameSettingsTheSuperHackersConstants.DefaultScreenEdgeScrollEnabledInFullscreenApp, settings.ScreenEdgeScrollEnabledInFullscreenApp);
        Assert.Equal(GameSettingsTheSuperHackersConstants.DefaultScreenEdgeScrollEnabledInWindowedApp, settings.ScreenEdgeScrollEnabledInWindowedApp);
    }

    /// <summary>
    /// Verifies that the toggle fallbacks match the values declared on the settings model itself.
    /// </summary>
    [Fact]
    public void ApplyToGeneralsOnlineSettings_UnsetToggles_MatchesModelDefaults()
    {
        // Arrange - seed each toggle inverted, so a missing assignment fails
        var profile = new GameProfile();
        var expected = new GeneralsOnlineSettings();
        var settings = new GeneralsOnlineSettings
        {
            PlayerObserverEnabled = false,
            CursorCaptureEnabledInFullscreenGame = false,
            CursorCaptureEnabledInFullscreenMenu = false,
            CursorCaptureEnabledInWindowedGame = false,
            CursorCaptureEnabledInWindowedMenu = true,
            ScreenEdgeScrollEnabledInFullscreenApp = false,
            ScreenEdgeScrollEnabledInWindowedApp = true,
        };

        // Act
        GameSettingsMapper.ApplyToGeneralsOnlineSettings(profile, settings);

        // Assert
        Assert.Equal(expected.PlayerObserverEnabled, settings.PlayerObserverEnabled);
        Assert.Equal(expected.CursorCaptureEnabledInFullscreenGame, settings.CursorCaptureEnabledInFullscreenGame);
        Assert.Equal(expected.CursorCaptureEnabledInFullscreenMenu, settings.CursorCaptureEnabledInFullscreenMenu);
        Assert.Equal(expected.CursorCaptureEnabledInWindowedGame, settings.CursorCaptureEnabledInWindowedGame);
        Assert.Equal(expected.CursorCaptureEnabledInWindowedMenu, settings.CursorCaptureEnabledInWindowedMenu);
        Assert.Equal(expected.ScreenEdgeScrollEnabledInFullscreenApp, settings.ScreenEdgeScrollEnabledInFullscreenApp);
        Assert.Equal(expected.ScreenEdgeScrollEnabledInWindowedApp, settings.ScreenEdgeScrollEnabledInWindowedApp);
    }

    /// <summary>
    /// Verifies that explicit toggle values on the profile are written through unchanged.
    /// </summary>
    [Fact]
    public void ApplyToGeneralsOnlineSettings_ExplicitToggles_ArePreserved()
    {
        // Arrange - every value is the opposite of its default
        var profile = new GameProfile
        {
            TshPlayerObserverEnabled = false,
            TshCursorCaptureEnabledInFullscreenGame = false,
            TshCursorCaptureEnabledInFullscreenMenu = false,
            TshCursorCaptureEnabledInWindowedGame = false,
            TshCursorCaptureEnabledInWindowedMenu = true,
            TshScreenEdgeScrollEnabledInFullscreenApp = false,
            TshScreenEdgeScrollEnabledInWindowedApp = true,
        };
        var settings = new GeneralsOnlineSettings();

        // Act
        GameSettingsMapper.ApplyToGeneralsOnlineSettings(profile, settings);

        // Assert
        Assert.False(settings.PlayerObserverEnabled);
        Assert.False(settings.CursorCaptureEnabledInFullscreenGame);
        Assert.False(settings.CursorCaptureEnabledInFullscreenMenu);
        Assert.False(settings.CursorCaptureEnabledInWindowedGame);
        Assert.True(settings.CursorCaptureEnabledInWindowedMenu);
        Assert.False(settings.ScreenEdgeScrollEnabledInFullscreenApp);
        Assert.True(settings.ScreenEdgeScrollEnabledInWindowedApp);
    }
}