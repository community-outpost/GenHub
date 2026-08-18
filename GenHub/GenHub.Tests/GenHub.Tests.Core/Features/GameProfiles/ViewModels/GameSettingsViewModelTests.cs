using System.Text.Json;
using GenHub.Core.Constants;
using GenHub.Core.Extensions;
using GenHub.Core.Interfaces.GameSettings;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.GameSettings;
using GenHub.Core.Models.Results;
using GenHub.Features.GameProfiles.ViewModels;
using Microsoft.Extensions.Logging;
using Moq;

namespace GenHub.Tests.Core.Features.GameProfiles.ViewModels;

/// <summary>
/// Tests for <see cref="GameSettingsViewModel"/>.
/// </summary>
public class GameSettingsViewModelTests
{
    private readonly Mock<IGameSettingsService> _gameSettingsServiceMock = new();
    private readonly Mock<ILogger<GameSettingsViewModel>> _loggerMock = new();
    private readonly GameSettingsViewModel _viewModel;

    /// <summary>
    /// Initializes a new instance of the <see cref="GameSettingsViewModelTests"/> class.
    /// </summary>
    public GameSettingsViewModelTests()
    {
        _viewModel = new GameSettingsViewModel(_gameSettingsServiceMock.Object, _loggerMock.Object);
    }

    /// <summary>
    /// Should initialize with default values.
    /// </summary>
    [Fact]
    public void Constructor_Should_InitializeWithDefaultValues()
    {
        // Assert
        Assert.Equal(GameType.Generals, _viewModel.SelectedGameType);
        Assert.Equal(70, _viewModel.SoundVolume);
        Assert.Equal(70, _viewModel.ThreeDSoundVolume);
        Assert.Equal(70, _viewModel.SpeechVolume);
        Assert.Equal(70, _viewModel.MusicVolume);
        Assert.True(_viewModel.AudioEnabled);
        Assert.Equal(16, _viewModel.NumSounds);
        Assert.Equal(800, _viewModel.ResolutionWidth);
        Assert.Equal(600, _viewModel.ResolutionHeight);
        Assert.False(_viewModel.Windowed);
        Assert.Equal(TextureQuality.High, _viewModel.TextureQuality);
        Assert.True(_viewModel.Shadows);
        Assert.True(_viewModel.ParticleEffects);
        Assert.True(_viewModel.ExtraAnimations);
        Assert.True(_viewModel.BuildingAnimations);
        Assert.Equal(50, _viewModel.Gamma);
    }

    /// <summary>
    /// Should load settings from Options.ini when no profile settings exist.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task InitializeForProfileAsync_Should_LoadFromIniOptions_WhenNoProfileSettingsAsync()
    {
        // Arrange
        var profile = new GameProfile
        {
            Id = "test-profile",
            Name = "Test Profile",
            GameClient = new GameClient { GameType = GameType.Generals },
        };

        var options = new IniOptions
        {
            Audio = new AudioSettings
            {
                SFXVolume = 80,
                SFX3DVolume = 85,
                VoiceVolume = 90,
                MusicVolume = 95,
                AudioEnabled = false,
                NumSounds = 24,
            },
            Video = new VideoSettings
            {
                ResolutionWidth = 1920,
                ResolutionHeight = 1080,
                Windowed = true,
                TextureReduction = 0,
                UseShadowVolumes = false,
                ExtraAnimations = false,
                Gamma = 75,
            },
        };

        _gameSettingsServiceMock.Setup(x => x.LoadOptionsAsync(GameType.Generals))
            .ReturnsAsync(OperationResult<IniOptions>.CreateSuccess(options));

        // Act
        await _viewModel.InitializeForProfileAsync("test-profile", profile);

        // Assert
        Assert.Equal(80, _viewModel.SoundVolume);
        Assert.Equal(85, _viewModel.ThreeDSoundVolume);
        Assert.Equal(90, _viewModel.SpeechVolume);
        Assert.Equal(95, _viewModel.MusicVolume);
        Assert.False(_viewModel.AudioEnabled);
        Assert.Equal(24, _viewModel.NumSounds);
        Assert.Equal(1920, _viewModel.ResolutionWidth);
        Assert.Equal(1080, _viewModel.ResolutionHeight);
        Assert.True(_viewModel.Windowed);
        Assert.Equal(TextureQuality.High, _viewModel.TextureQuality); // 2 - 0 = 2 (high quality)
        Assert.False(_viewModel.Shadows);
        Assert.False(_viewModel.ExtraAnimations);
        Assert.Equal(75, _viewModel.Gamma);
        Assert.Equal("Loaded default settings from Options.ini. Save the profile to persist these settings.", _viewModel.StatusMessage);
    }

    /// <summary>
    /// Should load settings from profile when profile has settings.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task InitializeForProfileAsync_Should_LoadFromProfile_WhenProfileHasSettingsAsync()
    {
        // Arrange
        var profile = new GameProfile
        {
            Id = "test-profile",
            Name = "Test Profile",
            GameClient = new GameClient { GameType = GameType.ZeroHour },
            VideoResolutionWidth = 2560,
            VideoResolutionHeight = 1440,
            VideoWindowed = true,
            VideoTextureQuality = TextureQuality.Medium,
            EnableVideoShadows = false,
            VideoGamma = 80,
            AudioSoundVolume = 75,
            AudioEnabled = false,
        };

        // Act
        await _viewModel.InitializeForProfileAsync("test-profile", profile);

        // Assert
        Assert.Equal(GameType.ZeroHour, _viewModel.SelectedGameType);
        Assert.Equal(2560, _viewModel.ResolutionWidth);
        Assert.Equal(1440, _viewModel.ResolutionHeight);
        Assert.True(_viewModel.Windowed);
        Assert.Equal(TextureQuality.Medium, _viewModel.TextureQuality);
        Assert.False(_viewModel.Shadows);
        Assert.Equal(80, _viewModel.Gamma);
        Assert.Equal(75, _viewModel.SoundVolume);
        Assert.False(_viewModel.AudioEnabled);
        Assert.Contains("Loaded profile settings", _viewModel.StatusMessage);
    }

    /// <summary>
    /// Should create correct UpdateProfileRequest from ViewModel state.
    /// </summary>
    [Fact]
    public void GetProfileSettings_Should_ReturnCorrectUpdateRequest()
    {
        // Arrange
        _viewModel.ResolutionWidth = 1920;
        _viewModel.ResolutionHeight = 1080;
        _viewModel.Windowed = true;
        _viewModel.TextureQuality = TextureQuality.Low;
        _viewModel.Shadows = false;
        _viewModel.Gamma = 65;
        _viewModel.SoundVolume = 80;
        _viewModel.AudioEnabled = false;

        // Act
        var request = _viewModel.GetProfileSettings();

        // Assert
        Assert.Equal(1920, request.VideoResolutionWidth);
        Assert.Equal(1080, request.VideoResolutionHeight);
        Assert.True(request.VideoWindowed);
        Assert.Equal(TextureQuality.Low, request.VideoTextureQuality);
        Assert.False(request.EnableVideoShadows);
        Assert.Equal(65, request.VideoGamma);
        Assert.Equal(80, request.AudioSoundVolume);
        Assert.False(request.AudioEnabled);
    }

    /// <summary>
    /// Should detect profile with settings correctly.
    /// </summary>
    /// <param name="hasVideoWidth">Whether the profile has video resolution width set.</param>
    /// <param name="hasVideoHeight">Whether the profile has video resolution height set.</param>
    /// <param name="hasWindowed">Whether the profile has windowed mode set.</param>
    /// <param name="hasTextureQuality">Whether the profile has texture quality set.</param>
    /// <param name="hasShadows">Whether the profile has shadows set.</param>
    /// <param name="hasParticleEffects">Whether the profile has particle effects set.</param>
    /// <param name="hasExtraAnimations">Whether the profile has extra animations set.</param>
    /// <param name="hasBuildingAnimations">Whether the profile has building animations set.</param>
    /// <param name="hasGamma">Whether the profile has gamma set.</param>
    /// <param name="hasSoundVolume">Whether the profile has sound volume set.</param>
    /// <param name="hasThreeDSoundVolume">Whether the profile has 3D sound volume set.</param>
    /// <param name="hasSpeechVolume">Whether the profile has speech volume set.</param>
    /// <param name="hasMusicVolume">Whether the profile has music volume set.</param>
    /// <param name="hasAudioEnabled">Whether the profile has audio enabled set.</param>
    /// <param name="hasNumSounds">Whether the profile has number of sounds set.</param>
    /// <param name="expected">The expected result.</param>
    [Theory]
    [InlineData(true, true, false, false, false, false, false, false, false, false, false, false, false, false, false, true)]
    [InlineData(false, false, true, false, false, false, false, false, false, false, false, false, false, false, false, true)]
    [InlineData(false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false)]
    public void HasProfileSettings_Should_DetectSettingsCorrectly(
        bool hasVideoWidth,
        bool hasVideoHeight,
        bool hasWindowed,
        bool hasTextureQuality,
        bool hasShadows,
        bool hasParticleEffects,
        bool hasExtraAnimations,
        bool hasBuildingAnimations,
        bool hasGamma,
        bool hasSoundVolume,
        bool hasThreeDSoundVolume,
        bool hasSpeechVolume,
        bool hasMusicVolume,
        bool hasAudioEnabled,
        bool hasNumSounds,
        bool expected)
    {
        // Arrange
        var profile = new GameProfile
        {
            VideoResolutionWidth = hasVideoWidth ? 1920 : null,
            VideoResolutionHeight = hasVideoHeight ? 1080 : null,
            VideoWindowed = hasWindowed ? true : null,
            VideoTextureQuality = hasTextureQuality ? TextureQuality.Medium : null,
            EnableVideoShadows = hasShadows ? false : null,
            VideoParticleEffects = hasParticleEffects ? true : null,
            VideoExtraAnimations = hasExtraAnimations ? false : null,
            VideoBuildingAnimations = hasBuildingAnimations ? true : null,
            VideoGamma = hasGamma ? 50 : null,
            AudioSoundVolume = hasSoundVolume ? 70 : null,
            AudioThreeDSoundVolume = hasThreeDSoundVolume ? 70 : null,
            AudioSpeechVolume = hasSpeechVolume ? 70 : null,
            AudioMusicVolume = hasMusicVolume ? 70 : null,
            AudioEnabled = hasAudioEnabled ? true : null,
            AudioNumSounds = hasNumSounds ? 16 : null,
        };

        // Act
        var hasSettings = profile.HasCustomSettings();

        // Assert
        Assert.Equal(expected, hasSettings);
    }

    /// <summary>
    /// Should apply resolution preset correctly.
    /// </summary>
    [Fact]
    public void ApplyResolutionPreset_Should_ParseAndApplyValidPreset()
    {
        // Act
        _viewModel.ApplyResolutionPreset("1920x1080");

        // Assert
        Assert.Equal(1920, _viewModel.ResolutionWidth);
        Assert.Equal(1080, _viewModel.ResolutionHeight);
        Assert.Contains("Resolution set to 1920x1080", _viewModel.StatusMessage);
    }

    /// <summary>
    /// Should handle invalid resolution preset gracefully.
    /// </summary>
    /// <param name="preset">The preset to test.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("1920")]
    [InlineData("1920x")]
    [InlineData("x1080")]
    public void ApplyResolutionPreset_Should_HandleInvalidPreset(string? preset)
    {
        // Arrange
        var originalWidth = _viewModel.ResolutionWidth;
        var originalHeight = _viewModel.ResolutionHeight;

        // Act
        _viewModel.ApplyResolutionPreset(preset);

        // Assert
        Assert.Equal(originalWidth, _viewModel.ResolutionWidth);
        Assert.Equal(originalHeight, _viewModel.ResolutionHeight);
    }

    /// <summary>
    /// Should update selected resolution preset when resolution changes.
    /// </summary>
    [Fact]
    public void OnSelectedResolutionPresetChanged_Should_ApplyPreset()
    {
        // Act
        _viewModel.SelectedResolutionPreset = "1280x720";

        // Assert
        Assert.Equal(1280, _viewModel.ResolutionWidth);
        Assert.Equal(720, _viewModel.ResolutionHeight);
    }

    /// <summary>
    /// Should load settings when game type changes outside initialization.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task OnSelectedGameTypeChanged_Should_LoadSettings_WhenNotInitializingAsync()
    {
        // Arrange
        var options = new IniOptions
        {
            Audio = new AudioSettings { SFXVolume = 60 },
            Video = new VideoSettings { ResolutionWidth = 800, ResolutionHeight = 600 },
        };

        _gameSettingsServiceMock.Setup(x => x.LoadOptionsAsync(GameType.ZeroHour))
            .ReturnsAsync(OperationResult<IniOptions>.CreateSuccess(options));

        // Act
        _viewModel.SelectedGameType = GameType.ZeroHour;

        // Wait for async operation to complete
        await Task.Delay(100);

        // Assert
        _gameSettingsServiceMock.Verify(x => x.LoadOptionsAsync(GameType.ZeroHour), Times.Once);
    }

    /// <summary>
    /// Should not load settings when game type is set before initialization.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task OnSelectedGameTypeChanged_Should_NotLoadSettings_WhenSetBeforeInitializationAsync()
    {
        // Arrange
        var profile = new GameProfile
        {
            GameClient = new GameClient { GameType = GameType.ZeroHour },
            VideoResolutionWidth = 1920, // Add settings so initialization loads from profile, not Options.ini
        };

        _viewModel.SelectedGameType = GameType.Generals; // Set before initialization

        // Act - Start initialization
        await _viewModel.InitializeForProfileAsync("test", profile);

        // Assert - Should have loaded from profile during initialization, not from Options.ini
        _gameSettingsServiceMock.Verify(x => x.LoadOptionsAsync(It.IsAny<GameType>()), Times.Never);
    }

    /// <summary>
    /// Should handle load settings command failure gracefully.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task LoadSettings_Should_HandleFailureGracefullyAsync()
    {
        // Arrange
        _gameSettingsServiceMock.Setup(x => x.LoadOptionsAsync(GameType.Generals))
            .ReturnsAsync(OperationResult<IniOptions>.CreateFailure("File not found"));
        _gameSettingsServiceMock.Setup(x => x.GetOptionsFilePath(GameType.Generals))
            .Returns("C:\\Test\\Options.ini");
        _gameSettingsServiceMock.Setup(x => x.OptionsFileExists(GameType.Generals))
            .Returns(false);

        // Act
        await _viewModel.LoadSettingsCommand.ExecuteAsync(null);

        // Assert
        Assert.Contains("Failed to load settings", _viewModel.StatusMessage);
    }

    /// <summary>
    /// Should handle save settings command failure gracefully.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task SaveSettings_Should_HandleFailureGracefullyAsync()
    {
        // Arrange
        _gameSettingsServiceMock.Setup(x => x.SaveOptionsAsync(GameType.Generals, It.IsAny<IniOptions>()))
            .ReturnsAsync(OperationResult<bool>.CreateFailure("Permission denied"));

        // Act
        await _viewModel.SaveSettingsCommand.ExecuteAsync(null);

        // Assert
        Assert.Contains("Failed to save settings", _viewModel.StatusMessage);
    }

    /// <summary>
    /// Should keep settings.json keys the view model does not model when saving over them.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task SaveSettings_Should_PreserveUnknownGeneralsOnlineKeysAsync()
    {
        // Arrange
        var existing = new GeneralsOnlineSettings();
        existing.AdditionalSettings["auth_token"] = JsonSerializer.Deserialize<JsonElement>("\"preserve-me\"");

        _gameSettingsServiceMock.Setup(x => x.LoadOptionsAsync(GameType.ZeroHour))
            .ReturnsAsync(OperationResult<IniOptions>.CreateSuccess(new IniOptions()));
        _gameSettingsServiceMock.Setup(x => x.LoadGeneralsOnlineSettingsAsync())
            .ReturnsAsync(OperationResult<GeneralsOnlineSettings>.CreateSuccess(existing));
        _gameSettingsServiceMock.Setup(x => x.SaveOptionsAsync(GameType.ZeroHour, It.IsAny<IniOptions>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        GeneralsOnlineSettings? saved = null;
        _gameSettingsServiceMock.Setup(x => x.SaveGeneralsOnlineSettingsAsync(It.IsAny<GeneralsOnlineSettings>()))
            .Callback<GeneralsOnlineSettings>(s => saved = s)
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        // Act
        await _viewModel.InitializeForProfileAsync("go-profile", CreateGeneralsOnlineProfile());
        await _viewModel.SaveSettingsCommand.ExecuteAsync(null);

        // Assert
        Assert.NotNull(saved);
        Assert.True(saved.AdditionalSettings.ContainsKey("auth_token"), "client-owned key was dropped");
        Assert.Equal("preserve-me", saved.AdditionalSettings["auth_token"].GetString());
    }

    /// <summary>
    /// Should leave settings.json alone when it could not be read, because a missing file reads as
    /// defaults and reports success: a failed read means the client's own file exists and is
    /// unreadable, and rewriting it from defaults would discard everything the client owns.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task SaveSettings_Should_NotRewriteGeneralsOnlineSettings_WhenTheyCannotBeReadAsync()
    {
        // Arrange
        var profile = CreateGeneralsOnlineProfile();
        profile.GoShowFps = true;

        _gameSettingsServiceMock.Setup(x => x.LoadGeneralsOnlineSettingsAsync())
            .ReturnsAsync(OperationResult<GeneralsOnlineSettings>.CreateFailure("settings.json is locked"));
        _gameSettingsServiceMock.Setup(x => x.SaveOptionsAsync(GameType.ZeroHour, It.IsAny<IniOptions>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        // Act
        await _viewModel.InitializeForProfileAsync("go-profile", profile);
        await _viewModel.SaveSettingsCommand.ExecuteAsync(null);

        // Assert
        _gameSettingsServiceMock.Verify(
            x => x.SaveGeneralsOnlineSettingsAsync(It.IsAny<GeneralsOnlineSettings>()),
            Times.Never);
        Assert.Contains("settings.json is locked", _viewModel.StatusMessage);
    }

    /// <summary>
    /// Should save a settings.json that spells a nested section as an explicit null, which is
    /// valid JSON and leaves the section null once deserialized.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task SaveSettings_Should_HandleNullGeneralsOnlineSectionsAsync()
    {
        // Arrange
        var existing = new GeneralsOnlineSettings { Camera = null!, Chat = null!, Debug = null!, Render = null!, Social = null! };

        _gameSettingsServiceMock.Setup(x => x.LoadGeneralsOnlineSettingsAsync())
            .ReturnsAsync(OperationResult<GeneralsOnlineSettings>.CreateSuccess(existing));
        _gameSettingsServiceMock.Setup(x => x.SaveOptionsAsync(GameType.ZeroHour, It.IsAny<IniOptions>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        GeneralsOnlineSettings? saved = null;
        _gameSettingsServiceMock.Setup(x => x.SaveGeneralsOnlineSettingsAsync(It.IsAny<GeneralsOnlineSettings>()))
            .Callback<GeneralsOnlineSettings>(s => saved = s)
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        var profile = CreateGeneralsOnlineProfile();
        profile.GoCameraMinHeight = 200.0f;

        // Act
        await _viewModel.InitializeForProfileAsync("go-profile", profile);
        await _viewModel.SaveSettingsCommand.ExecuteAsync(null);

        // Assert
        Assert.NotNull(saved);
        Assert.Equal(200.0f, saved.Camera.MinHeight);
        Assert.Contains("saved successfully", _viewModel.StatusMessage);
    }

    /// <summary>
    /// Should read settings.json before rewriting it, including when the first read failed.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task SaveSettings_Should_ReadGeneralsOnlineSettings_BeforeRewritingAsync()
    {
        // Arrange
        var profile = CreateGeneralsOnlineProfile();
        profile.GoShowFps = true;
        _gameSettingsServiceMock.Setup(x => x.SaveOptionsAsync(GameType.ZeroHour, It.IsAny<IniOptions>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));
        _gameSettingsServiceMock.Setup(x => x.SaveGeneralsOnlineSettingsAsync(It.IsAny<GeneralsOnlineSettings>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        // Act
        await _viewModel.InitializeForProfileAsync("go-profile", profile);
        await _viewModel.SaveSettingsCommand.ExecuteAsync(null);

        // Assert
        _gameSettingsServiceMock.Verify(x => x.LoadGeneralsOnlineSettingsAsync(), Times.AtLeastOnce);
    }

    /// <summary>
    /// Should keep the values a user configured inside the GeneralsOnline client when saving a
    /// profile that declares only some GeneralsOnline options.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task SaveSettings_Should_NotOverwriteClientValues_TheProfileDoesNotDeclareAsync()
    {
        // Arrange - the client's values are all the opposite of the view model's defaults
        var existing = new GeneralsOnlineSettings
        {
            ShowPing = false,
            ShowPlayerRanks = false,
            RememberUsername = false,
            EnableNotifications = false,
            EnableSoundNotifications = false,
            ChatFontSize = 24,
        };

        var profile = CreateGeneralsOnlineProfile();
        profile.GoShowFps = true;

        _gameSettingsServiceMock.Setup(x => x.LoadGeneralsOnlineSettingsAsync())
            .ReturnsAsync(OperationResult<GeneralsOnlineSettings>.CreateSuccess(existing));
        _gameSettingsServiceMock.Setup(x => x.SaveOptionsAsync(GameType.ZeroHour, It.IsAny<IniOptions>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        GeneralsOnlineSettings? saved = null;
        _gameSettingsServiceMock.Setup(x => x.SaveGeneralsOnlineSettingsAsync(It.IsAny<GeneralsOnlineSettings>()))
            .Callback<GeneralsOnlineSettings>(s => saved = s)
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        // Act
        await _viewModel.InitializeForProfileAsync("go-profile", profile);
        await _viewModel.SaveSettingsCommand.ExecuteAsync(null);

        // Assert
        Assert.NotNull(saved);
        Assert.True(saved.ShowFps);
        Assert.False(saved.ShowPing);
        Assert.False(saved.ShowPlayerRanks);
        Assert.False(saved.RememberUsername);
        Assert.False(saved.EnableNotifications);
        Assert.False(saved.EnableSoundNotifications);
        Assert.Equal(24, saved.ChatFontSize);
    }

    /// <summary>
    /// Should not turn the client's enabled toggles off when nothing has read them, which is what
    /// a view model default of false would do to a model that defaults them to true.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task SaveSettings_Should_NotFlipEnabledTogglesOffAsync()
    {
        // Arrange - settings.json does not exist yet, which reads as defaults, so the defaults decide
        var profile = CreateGeneralsOnlineProfile();
        profile.GoShowFps = true;

        _gameSettingsServiceMock.Setup(x => x.LoadGeneralsOnlineSettingsAsync())
            .ReturnsAsync(OperationResult<GeneralsOnlineSettings>.CreateSuccess(new GeneralsOnlineSettings()));
        _gameSettingsServiceMock.Setup(x => x.SaveOptionsAsync(GameType.ZeroHour, It.IsAny<IniOptions>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        GeneralsOnlineSettings? saved = null;
        _gameSettingsServiceMock.Setup(x => x.SaveGeneralsOnlineSettingsAsync(It.IsAny<GeneralsOnlineSettings>()))
            .Callback<GeneralsOnlineSettings>(s => saved = s)
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        // Act
        await _viewModel.InitializeForProfileAsync("go-profile", profile);
        await _viewModel.SaveSettingsCommand.ExecuteAsync(null);

        // Assert
        Assert.NotNull(saved);
        var expected = new GeneralsOnlineSettings();
        Assert.Equal(expected.ShowPing, saved.ShowPing);
        Assert.Equal(expected.ShowPlayerRanks, saved.ShowPlayerRanks);
        Assert.Equal(expected.RememberUsername, saved.RememberUsername);
        Assert.Equal(expected.EnableNotifications, saved.EnableNotifications);
        Assert.Equal(expected.EnableSoundNotifications, saved.EnableSoundNotifications);
        Assert.Equal(expected.ChatFontSize, saved.ChatFontSize);
    }

    /// <summary>
    /// Should leave the GeneralsOnline client's global settings.json alone when the profile being
    /// edited runs some other client.
    /// </summary>
    /// <param name="publisherType">The publisher the profile's client belongs to.</param>
    /// <param name="gameType">The game the profile targets.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Theory]
    [InlineData(PublisherTypeConstants.TheSuperHackers, GameType.ZeroHour)]
    [InlineData(CommunityOutpostConstants.PublisherType, GameType.ZeroHour)]
    [InlineData(PublisherTypeConstants.TheSuperHackers, GameType.Generals)]
    public async Task SaveSettings_Should_NotWriteGeneralsOnlineSettings_ForOtherPublishersAsync(string publisherType, GameType gameType)
    {
        // Arrange
        var profile = new GameProfile
        {
            Id = "other-profile",
            Name = "Other Profile",
            GameClient = new GameClient { GameType = gameType, PublisherType = publisherType },
            VideoResolutionWidth = 1920,
        };

        _gameSettingsServiceMock.Setup(x => x.SaveOptionsAsync(gameType, It.IsAny<IniOptions>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        // Act
        await _viewModel.InitializeForProfileAsync("other-profile", profile);
        await _viewModel.SaveSettingsCommand.ExecuteAsync(null);

        // Assert
        _gameSettingsServiceMock.Verify(
            x => x.SaveGeneralsOnlineSettingsAsync(It.IsAny<GeneralsOnlineSettings>()),
            Times.Never);
        Assert.Contains("saved successfully", _viewModel.StatusMessage);
    }

    /// <summary>
    /// Should update selected preset when resolution matches preset.
    /// </summary>
    [Fact]
    public void ApplyOptionsToViewModel_Should_UpdateSelectedPreset_WhenResolutionMatches()
    {
        // Arrange
        var options = new IniOptions
        {
            Video = new VideoSettings { ResolutionWidth = 1920, ResolutionHeight = 1080 },
        };

        // Act - Simulate loading options
        _viewModel.ResolutionWidth = 1920;
        _viewModel.ResolutionHeight = 1080;

        // Manually trigger the logic that would happen in LoadSettings
        var currentRes = $"{_viewModel.ResolutionWidth}x{_viewModel.ResolutionHeight}";
        _viewModel.SelectedResolutionPreset = _viewModel.ResolutionPresets.Contains(currentRes) ? currentRes : null;

        // Assert
        Assert.Equal("1920x1080", _viewModel.SelectedResolutionPreset);
    }

    private static GameProfile CreateGeneralsOnlineProfile()
    {
        return new GameProfile
        {
            Id = "go-profile",
            Name = "GeneralsOnline Profile",
            GameClient = new GameClient
            {
                GameType = GameType.ZeroHour,
                PublisherType = PublisherTypeConstants.GeneralsOnline,
            },
        };
    }
}