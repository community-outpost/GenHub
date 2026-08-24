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
    /// Verifies that font sizes the profile leaves unset keep the values already in settings.json,
    /// which is where the values a user configured inside the client itself live.
    /// </summary>
    [Fact]
    public void ApplyToGeneralsOnlineSettings_UnsetFontSizes_PreservesExistingValues()
    {
        // Arrange - seed with values no GenHub default would produce
        var profile = new GameProfile();
        var settings = new GeneralsOnlineSettings
        {
            SystemTimeFontSize = 99,
            NetworkLatencyFontSize = 98,
            RenderFpsFontSize = 97,
            ResolutionFontAdjustment = 96,
        };

        // Act
        GameSettingsMapper.ApplyToGeneralsOnlineSettings(profile, settings);

        // Assert
        Assert.Equal(99, settings.SystemTimeFontSize);
        Assert.Equal(98, settings.NetworkLatencyFontSize);
        Assert.Equal(97, settings.RenderFpsFontSize);
        Assert.Equal(96, settings.ResolutionFontAdjustment);
    }

    /// <summary>
    /// Verifies that GeneralsOnline options the profile leaves unset keep the values already in
    /// settings.json rather than being reset to GenHub's defaults.
    /// </summary>
    [Fact]
    public void ApplyToGeneralsOnlineSettings_UnsetGeneralsOnlineOptions_PreservesExistingValues()
    {
        // Arrange - the profile declares one option; everything else is the client's own
        var profile = new GameProfile { GoShowFps = true };
        var settings = new GeneralsOnlineSettings
        {
            ShowPing = false,
            RememberUsername = false,
            ChatFontSize = 24,
        };
        settings.Camera.MinHeight = 42.0f;
        settings.Render.FpsLimit = 60;
        settings.Social.NotificationFriendComesOnlineMenus = false;

        // Act
        GameSettingsMapper.ApplyToGeneralsOnlineSettings(profile, settings);

        // Assert
        Assert.True(settings.ShowFps);
        Assert.False(settings.ShowPing);
        Assert.False(settings.RememberUsername);
        Assert.Equal(24, settings.ChatFontSize);
        Assert.Equal(42.0f, settings.Camera.MinHeight);
        Assert.Equal(60, settings.Render.FpsLimit);
        Assert.False(settings.Social.NotificationFriendComesOnlineMenus);
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
    /// Verifies that a fresh settings.json keeps money transaction audio audible, so that the
    /// model default and the settings screen agree on what an unconfigured profile writes.
    /// </summary>
    [Fact]
    public void ApplyToGeneralsOnlineSettings_UnsetMoneyTransactionVolume_StaysAudible()
    {
        // Arrange
        var profile = new GameProfile();
        var settings = new GeneralsOnlineSettings();

        // Act
        GameSettingsMapper.ApplyToGeneralsOnlineSettings(profile, settings);

        // Assert
        Assert.Equal(GameSettingsTheSuperHackersConstants.DefaultMoneyTransactionVolume, settings.MoneyTransactionVolume);
        Assert.NotEqual(0, settings.MoneyTransactionVolume);
    }

    /// <summary>
    /// Verifies that cursor capture, edge scroll and observer toggles the profile leaves unset
    /// keep the values already in settings.json.
    /// </summary>
    [Fact]
    public void ApplyToGeneralsOnlineSettings_UnsetToggles_PreservesExistingValues()
    {
        // Arrange - seed each toggle inverted relative to its GenHub default
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
        Assert.False(settings.PlayerObserverEnabled);
        Assert.False(settings.CursorCaptureEnabledInFullscreenGame);
        Assert.False(settings.CursorCaptureEnabledInFullscreenMenu);
        Assert.False(settings.CursorCaptureEnabledInWindowedGame);
        Assert.True(settings.CursorCaptureEnabledInWindowedMenu);
        Assert.False(settings.ScreenEdgeScrollEnabledInFullscreenApp);
        Assert.True(settings.ScreenEdgeScrollEnabledInWindowedApp);
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

    /// <summary>
    /// Verifies that TshGameWindowTransitionSpeedMultiplier is correctly mapped to TheSuperHackers section.
    /// </summary>
    [Fact]
    public void ApplyToOptions_TshGameWindowTransitionSpeedMultiplier_MapsToTheSuperHackersSection()
    {
        // Arrange
        var profile = new GameProfile
        {
            TshGameWindowTransitionSpeedMultiplier = 2.5f,
        };
        var options = new IniOptions();

        // Act
        GameSettingsMapper.ApplyToOptions(profile, options);

        // Assert
        Assert.True(options.AdditionalSections.TryGetValue("TheSuperHackers", out var tsh));
        Assert.True(tsh.TryGetValue("GameWindowTransitionSpeedMultiplier", out var speed));
        Assert.Equal("2.5", speed);
    }

    /// <summary>
    /// Verifies that GameWindowTransitionSpeedMultiplier is loaded from hierarchical options.
    /// </summary>
    [Fact]
    public void ApplyFromOptions_HierarchicalSection_MapsGameWindowTransitionSpeedMultiplier()
    {
        // Arrange
        var options = new IniOptions();
        options.AdditionalSections["TheSuperHackers"] = new Dictionary<string, string>
        {
            ["GameWindowTransitionSpeedMultiplier"] = "3.75",
        };
        var profile = new GameProfile();

        // Act
        GameSettingsMapper.ApplyFromOptions(options, profile);

        // Assert
        Assert.Equal(3.75f, profile.TshGameWindowTransitionSpeedMultiplier);
    }

    /// <summary>
    /// Verifies that GameWindowTransitionSpeedMultiplier is loaded from flat root video properties.
    /// </summary>
    [Fact]
    public void ApplyFromOptions_FlatProperties_MapsGameWindowTransitionSpeedMultiplier()
    {
        // Arrange
        var options = new IniOptions();
        options.Video.AdditionalProperties["GameWindowTransitionSpeedMultiplier"] = "5.0";
        var profile = new GameProfile();

        // Act
        GameSettingsMapper.ApplyFromOptions(options, profile);

        // Assert
        Assert.Equal(5.0f, profile.TshGameWindowTransitionSpeedMultiplier);
    }

    /// <summary>
    /// Verifies that GameWindowTransitionSpeedMultiplier maps to and from GeneralsOnlineSettings.
    /// </summary>
    [Fact]
    public void ApplyToAndFromGeneralsOnlineSettings_GameWindowTransitionSpeedMultiplier_RoundTrips()
    {
        // Arrange
        var profile = new GameProfile
        {
            TshGameWindowTransitionSpeedMultiplier = 10.0f,
        };
        var settings = new GeneralsOnlineSettings();

        // Act
        GameSettingsMapper.ApplyToGeneralsOnlineSettings(profile, settings);

        // Assert
        Assert.Equal(10.0f, settings.GameWindowTransitionSpeedMultiplier);

        // Act back
        var targetProfile = new GameProfile();
        GameSettingsMapper.ApplyFromGeneralsOnlineSettings(settings, targetProfile);

        // Assert back
        Assert.Equal(10.0f, targetProfile.TshGameWindowTransitionSpeedMultiplier);
    }

    /// <summary>
    /// Verifies that PopulateGameProfile and UpdateFromRequest preserve GameWindowTransitionSpeedMultiplier.
    /// </summary>
    [Fact]
    public void PopulateAndUpdate_PreservesGameWindowTransitionSpeedMultiplier()
    {
        // Arrange
        var createRequest = new CreateProfileRequest
        {
            Name = "TestProfile",
            TshGameWindowTransitionSpeedMultiplier = 4.2f,
        };
        var profile = new GameProfile();

        // Act
        GameSettingsMapper.PopulateGameProfile(profile, createRequest);

        // Assert
        Assert.Equal(4.2f, profile.TshGameWindowTransitionSpeedMultiplier);

        // Update
        var updateRequest = new UpdateProfileRequest
        {
            TshGameWindowTransitionSpeedMultiplier = 8.4f,
        };
        GameSettingsMapper.UpdateFromRequest(profile, updateRequest);
        Assert.Equal(8.4f, profile.TshGameWindowTransitionSpeedMultiplier);
    }

    /// <summary>
    /// Verifies that out-of-range values are clamped to Min/Max and NaN/Infinity values are ignored.
    /// </summary>
    /// <param name="input">The raw string input value from Options.ini.</param>
    /// <param name="expected">The expected clamped float multiplier value.</param>
    [Theory]
    [InlineData("0.2", 1.0f)]
    [InlineData("5000.0", 10.0f)]
    [InlineData("-10.0", 1.0f)]
    public void ApplyFromOptions_ClampsOutOfRangeTransitionSpeedMultiplier(string input, float expected)
    {
        // Arrange
        var options = new IniOptions();
        options.AdditionalSections["TheSuperHackers"] = new Dictionary<string, string>
        {
            ["GameWindowTransitionSpeedMultiplier"] = input,
        };
        var profile = new GameProfile();

        // Act
        GameSettingsMapper.ApplyFromOptions(options, profile);

        // Assert
        Assert.Equal(expected, profile.TshGameWindowTransitionSpeedMultiplier);
    }

    /// <summary>
    /// Verifies that non-finite values (NaN, Infinity) are ignored and do not corrupt profile settings.
    /// </summary>
    /// <param name="input">The raw non-finite or invalid string input value.</param>
    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    [InlineData("invalid_float")]
    public void ApplyFromOptions_IgnoresNonFiniteTransitionSpeedMultiplier(string input)
    {
        // Arrange
        var options = new IniOptions();
        options.AdditionalSections["TheSuperHackers"] = new Dictionary<string, string>
        {
            ["GameWindowTransitionSpeedMultiplier"] = input,
        };
        var profile = new GameProfile();

        // Act
        GameSettingsMapper.ApplyFromOptions(options, profile);

        // Assert
        Assert.Null(profile.TshGameWindowTransitionSpeedMultiplier);
    }

    /// <summary>
    /// Verifies that NormalizeTransitionSpeedMultiplier clamps out-of-range values and rejects non-finite values.
    /// </summary>
    [Fact]
    public void NormalizeTransitionSpeedMultiplier_ShouldClampAndFilterCorrectly()
    {
        Assert.Null(GameSettingsMapper.NormalizeTransitionSpeedMultiplier(null));
        Assert.Null(GameSettingsMapper.NormalizeTransitionSpeedMultiplier(float.NaN));
        Assert.Null(GameSettingsMapper.NormalizeTransitionSpeedMultiplier(float.PositiveInfinity));
        Assert.Null(GameSettingsMapper.NormalizeTransitionSpeedMultiplier(float.NegativeInfinity));
        Assert.Equal(1.0f, GameSettingsMapper.NormalizeTransitionSpeedMultiplier(0.5f));
        Assert.Equal(10.0f, GameSettingsMapper.NormalizeTransitionSpeedMultiplier(50.0f));
        Assert.Equal(1.05f, GameSettingsMapper.NormalizeTransitionSpeedMultiplier(1.05f));
    }

    /// <summary>
    /// Verifies that ApplyToOptions clamps out-of-range transition speed multiplier before writing to dictionary.
    /// </summary>
    [Fact]
    public void ApplyToOptions_ShouldClampTransitionSpeedMultiplier()
    {
        var profile = new GameProfile
        {
            TshGameWindowTransitionSpeedMultiplier = 99.0f,
        };
        var options = new IniOptions();

        GameSettingsMapper.ApplyToOptions(profile, options);

        Assert.True(options.AdditionalSections.TryGetValue("TheSuperHackers", out var tshDict));
        Assert.Equal("10", tshDict["GameWindowTransitionSpeedMultiplier"]);
    }
}