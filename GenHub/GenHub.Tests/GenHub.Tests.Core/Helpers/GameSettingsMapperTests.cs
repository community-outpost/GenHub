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
    /// Note: In the retail engine, both High and VeryHigh map to TextureReduction 0 (no reduction).
    /// VeryHigh exists in GenHub's enum to support modern renderers/mods, but Options.ini uses 0 for both.
    /// </summary>
    /// <param name="quality">The texture quality level.</param>
    /// <param name="expectedReduction">The expected texture reduction value in Options.ini.</param>
    [Theory]
    [InlineData(TextureQuality.Low, GameSettingsConstants.TextureQuality.TextureReductionLow)]
    [InlineData(TextureQuality.Medium, GameSettingsConstants.TextureQuality.TextureReductionMedium)]
    [InlineData(TextureQuality.High, GameSettingsConstants.TextureQuality.TextureReductionHigh)]
    [InlineData(TextureQuality.VeryHigh, GameSettingsConstants.TextureQuality.TextureReductionVeryHigh)]
    public void ApplyToOptions_AllTextureQualities_SetsCorrectReduction(TextureQuality quality, int expectedReduction)
    {
        // Arrange
        var profile = new GameProfile
        {
            VideoTextureQuality = quality,
        };
        var options = new IniOptions
        {
            Video = { TextureReduction = 99 },
        };

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
    /// Verifies that font sizes the profile leaves unset keep the values already in TheSuperHackers section.
    /// </summary>
    [Fact]
    public void ApplyToOptions_TheSuperHackersUnsetFontSizes_PreservesExistingValues()
    {
        // Arrange - seed with values no GenHub default would produce
        var profile = new GameProfile();
        var options = new IniOptions();
        options.AdditionalSections["TheSuperHackers"] = new Dictionary<string, string>
        {
            ["SystemTimeFontSize"] = "99",
            ["NetworkLatencyFontSize"] = "98",
            ["RenderFpsFontSize"] = "97",
            ["ResolutionFontAdjustment"] = "96",
        };

        // Act
        GameSettingsMapper.ApplyToOptions(profile, options);

        // Assert
        var tsh = options.AdditionalSections["TheSuperHackers"];
        Assert.Equal("99", tsh["SystemTimeFontSize"]);
        Assert.Equal("98", tsh["NetworkLatencyFontSize"]);
        Assert.Equal("97", tsh["RenderFpsFontSize"]);
        Assert.Equal("96", tsh["ResolutionFontAdjustment"]);
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
    /// Verifies that explicit TheSuperHackers font sizes on the profile are written to Options.ini unchanged.
    /// </summary>
    [Fact]
    public void ApplyToOptions_ExplicitFontSizes_ArePreserved()
    {
        // Arrange
        var profile = new GameProfile
        {
            TshSystemTimeFontSize = 20,
            TshNetworkLatencyFontSize = 21,
            TshRenderFpsFontSize = 22,
            TshResolutionFontAdjustment = 23,
        };
        var options = new IniOptions();

        // Act
        GameSettingsMapper.ApplyToOptions(profile, options);

        // Assert
        var tsh = options.AdditionalSections["TheSuperHackers"];
        Assert.Equal("20", tsh["SystemTimeFontSize"]);
        Assert.Equal("21", tsh["NetworkLatencyFontSize"]);
        Assert.Equal("22", tsh["RenderFpsFontSize"]);
        Assert.Equal("23", tsh["ResolutionFontAdjustment"]);
    }

    /// <summary>
    /// Verifies that an unconfigured profile preserves existing MoneyTransactionVolume in Options.ini.
    /// </summary>
    [Fact]
    public void ApplyToOptions_UnsetMoneyTransactionVolume_PreservesExistingValue()
    {
        // Arrange
        var profile = new GameProfile();
        var options = new IniOptions();
        options.AdditionalSections["TheSuperHackers"] = new Dictionary<string, string>
        {
            ["MoneyTransactionVolume"] = "75",
        };

        // Act
        GameSettingsMapper.ApplyToOptions(profile, options);

        // Assert
        Assert.Equal("75", options.AdditionalSections["TheSuperHackers"]["MoneyTransactionVolume"]);
    }

    /// <summary>
    /// Verifies that cursor capture, edge scroll and observer toggles the profile leaves unset
    /// keep the values already in TheSuperHackers section.
    /// </summary>
    [Fact]
    public void ApplyToOptions_UnsetToggles_PreservesExistingValues()
    {
        // Arrange - seed each toggle inverted relative to its GenHub default
        var profile = new GameProfile();
        var options = new IniOptions();
        options.AdditionalSections["TheSuperHackers"] = new Dictionary<string, string>
        {
            ["PlayerObserverEnabled"] = "no",
            ["CursorCaptureEnabledInFullscreenGame"] = "no",
            ["CursorCaptureEnabledInFullscreenMenu"] = "no",
            ["CursorCaptureEnabledInWindowedGame"] = "no",
            ["CursorCaptureEnabledInWindowedMenu"] = "yes",
            ["ScreenEdgeScrollEnabledInFullscreenApp"] = "no",
            ["ScreenEdgeScrollEnabledInWindowedApp"] = "yes",
        };

        // Act
        GameSettingsMapper.ApplyToOptions(profile, options);

        // Assert
        var tsh = options.AdditionalSections["TheSuperHackers"];
        Assert.Equal("no", tsh["PlayerObserverEnabled"]);
        Assert.Equal("no", tsh["CursorCaptureEnabledInFullscreenGame"]);
        Assert.Equal("no", tsh["CursorCaptureEnabledInFullscreenMenu"]);
        Assert.Equal("no", tsh["CursorCaptureEnabledInWindowedGame"]);
        Assert.Equal("yes", tsh["CursorCaptureEnabledInWindowedMenu"]);
        Assert.Equal("no", tsh["ScreenEdgeScrollEnabledInFullscreenApp"]);
        Assert.Equal("yes", tsh["ScreenEdgeScrollEnabledInWindowedApp"]);
    }

    /// <summary>
    /// Verifies that explicit toggle values on the profile are written to Options.ini unchanged.
    /// </summary>
    [Fact]
    public void ApplyToOptions_ExplicitToggles_ArePreserved()
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
        var options = new IniOptions();

        // Act
        GameSettingsMapper.ApplyToOptions(profile, options);

        // Assert
        var tsh = options.AdditionalSections["TheSuperHackers"];
        Assert.Equal("no", tsh["PlayerObserverEnabled"]);
        Assert.Equal("no", tsh["CursorCaptureEnabledInFullscreenGame"]);
        Assert.Equal("no", tsh["CursorCaptureEnabledInFullscreenMenu"]);
        Assert.Equal("no", tsh["CursorCaptureEnabledInWindowedGame"]);
        Assert.Equal("yes", tsh["CursorCaptureEnabledInWindowedMenu"]);
        Assert.Equal("no", tsh["ScreenEdgeScrollEnabledInFullscreenApp"]);
        Assert.Equal("yes", tsh["ScreenEdgeScrollEnabledInWindowedApp"]);
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
        options.Video.AdditionalProperties["GameWindowTransitionSpeedMultiplier"] = "3.0";
        var profile = new GameProfile();

        // Act
        GameSettingsMapper.ApplyFromOptions(options, profile);

        // Assert
        Assert.Equal(3.0f, profile.TshGameWindowTransitionSpeedMultiplier);
    }

    /// <summary>
    /// Verifies that ApplyToOptions and ApplyFromOptions preserve GameWindowTransitionSpeedMultiplier.
    /// </summary>
    [Fact]
    public void ApplyToAndFromOptions_GameWindowTransitionSpeedMultiplier_RoundTrips()
    {
        // Arrange
        var profile = new GameProfile
        {
            TshGameWindowTransitionSpeedMultiplier = 4.0f,
        };
        var options = new IniOptions();

        // Act
        GameSettingsMapper.ApplyToOptions(profile, options);

        // Assert
        var tsh = options.AdditionalSections["TheSuperHackers"];
        Assert.Equal("4", tsh["GameWindowTransitionSpeedMultiplier"]);

        // Act back
        var targetProfile = new GameProfile();
        GameSettingsMapper.ApplyFromOptions(options, targetProfile);

        // Assert back
        Assert.Equal(4.0f, targetProfile.TshGameWindowTransitionSpeedMultiplier);
    }

    /// <summary>
    /// Verifies that ApplyFromOptions parses TheSuperHackers section case-insensitively.
    /// </summary>
    [Fact]
    public void ApplyFromOptions_CaseInsensitiveTheSuperHackersSection_LoadsProperties()
    {
        // Arrange
        var options = new IniOptions();
        options.AdditionalSections["thesuperhackers"] = new Dictionary<string, string>
        {
            ["gamewindowtransitionspeedmultiplier"] = "1.5",
            ["archivereplays"] = "yes",
            ["systemtimefontsize"] = "18",
        };
        var profile = new GameProfile();

        // Act
        GameSettingsMapper.ApplyFromOptions(options, profile);

        // Assert
        Assert.Equal(1.5f, profile.TshGameWindowTransitionSpeedMultiplier);
        Assert.True(profile.TshArchiveReplays);
        Assert.Equal(18, profile.TshSystemTimeFontSize);
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
            TshGameWindowTransitionSpeedMultiplier = 2.2f,
        };
        var profile = new GameProfile();

        // Act
        GameSettingsMapper.PopulateGameProfile(profile, createRequest);

        // Assert
        Assert.Equal(2.2f, profile.TshGameWindowTransitionSpeedMultiplier);

        // Update
        var updateRequest = new UpdateProfileRequest
        {
            TshGameWindowTransitionSpeedMultiplier = 3.4f,
        };
        GameSettingsMapper.UpdateFromRequest(profile, updateRequest);
        Assert.Equal(3.4f, profile.TshGameWindowTransitionSpeedMultiplier);
    }

    /// <summary>
    /// Verifies that out-of-range values are clamped to Min/Max and NaN/Infinity values are ignored.
    /// </summary>
    /// <param name="input">The raw string input value from Options.ini.</param>
    /// <param name="expected">The expected clamped float multiplier value.</param>
    [Theory]
    [InlineData("0.2", 1.0f)]
    [InlineData("5000.0", 4.0f)]
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
        Assert.Equal(4.0f, GameSettingsMapper.NormalizeTransitionSpeedMultiplier(50.0f));
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
        Assert.Equal("4", tshDict["GameWindowTransitionSpeedMultiplier"]);
    }

    /// <summary>
    /// Verifies that PopulateRequest from UpdateProfileRequest to CreateProfileRequest copies all properties including UseSteamLaunch and VideoSkipEALogo.
    /// </summary>
    [Fact]
    public void PopulateRequest_CreateProfileRequest_CopiesAllSettingsIncludingUseSteamLaunchAndVideoSkipEALogo()
    {
        // Arrange
        var source = new UpdateProfileRequest
        {
            UseSteamLaunch = true,
            VideoSkipEALogo = true,
            GameSpyIPAddress = "192.168.1.1",
            VideoResolutionWidth = 1920,
            VideoResolutionHeight = 1080,
            TshGameWindowTransitionSpeedMultiplier = 2.5f,
        };
        var target = new CreateProfileRequest
        {
            Name = "Test",
        };

        // Act
        GameSettingsMapper.PopulateRequest(target, source);

        // Assert
        Assert.True(target.UseSteamLaunch);
        Assert.True(target.VideoSkipEALogo);
        Assert.Equal("192.168.1.1", target.GameSpyIPAddress);
        Assert.Equal(1920, target.VideoResolutionWidth);
        Assert.Equal(1080, target.VideoResolutionHeight);
        Assert.Equal(2.5f, target.TshGameWindowTransitionSpeedMultiplier);
    }

    /// <summary>
    /// Verifies that PopulateRequest from UpdateProfileRequest to UpdateProfileRequest copies all properties including UseSteamLaunch and VideoSkipEALogo.
    /// </summary>
    [Fact]
    public void PopulateRequest_UpdateProfileRequest_CopiesAllSettingsIncludingUseSteamLaunchAndVideoSkipEALogo()
    {
        // Arrange
        var source = new UpdateProfileRequest
        {
            UseSteamLaunch = true,
            VideoSkipEALogo = true,
            GameSpyIPAddress = "192.168.1.1",
            VideoResolutionWidth = 1920,
            VideoResolutionHeight = 1080,
            TshGameWindowTransitionSpeedMultiplier = 2.5f,
        };
        var target = new UpdateProfileRequest();

        // Act
        GameSettingsMapper.PopulateRequest(target, source);

        // Assert
        Assert.True(target.UseSteamLaunch);
        Assert.True(target.VideoSkipEALogo);
        Assert.Equal("192.168.1.1", target.GameSpyIPAddress);
        Assert.Equal(1920, target.VideoResolutionWidth);
        Assert.Equal(1080, target.VideoResolutionHeight);
        Assert.Equal(2.5f, target.TshGameWindowTransitionSpeedMultiplier);
    }

    /// <summary>
    /// Verifies that GameSettingsMapper uses InvariantCulture when formatting numeric values.
    /// </summary>
    [Fact]
    public void ApplyToOptions_NumericFormatting_UsesInvariantCulture()
    {
        var currentCulture = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            // Use German culture where comma is the decimal separator
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");

            var profile = new GameProfile
            {
                TshGameWindowTransitionSpeedMultiplier = 2.5f,
                TshSystemTimeFontSize = 14,
            };
            var options = new IniOptions();

            GameSettingsMapper.ApplyToOptions(profile, options);

            var tsh = options.AdditionalSections["TheSuperHackers"];
            Assert.Equal("2.5", tsh["GameWindowTransitionSpeedMultiplier"]);
            Assert.Equal("14", tsh["SystemTimeFontSize"]);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = currentCulture;
        }
    }
}
