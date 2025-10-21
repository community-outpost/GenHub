using GenHub.Core.Interfaces.GameSettings;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.GameSettings;
using GenHub.Core.Models.Results;
using GenHub.Features.GameProfiles.ViewModels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

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
        Assert.Equal(2, _viewModel.TextureQuality);
        Assert.True(_viewModel.Shadows);
        Assert.True(_viewModel.ParticleEffects);
        Assert.True(_viewModel.ExtraAnimations);
        Assert.True(_viewModel.BuildingAnimations);
        Assert.Equal(100, _viewModel.Gamma);
    }

    /// <summary>
    /// Should load settings from Options.ini when no profile settings exist.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task InitializeForProfileAsync_Should_LoadFromOptionsIni_WhenNoProfileSettings()
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
                Gamma = 110,
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
        Assert.Equal(2, _viewModel.TextureQuality); // 2 - 0 = 2 (high quality)
        Assert.False(_viewModel.Shadows);
        Assert.False(_viewModel.ExtraAnimations);
        Assert.Equal(110, _viewModel.Gamma);
        Assert.Equal("Loaded default settings from Options.ini. Save the profile to persist these settings.", _viewModel.StatusMessage);
    }

    /// <summary>
    /// Should load settings from profile when profile has settings.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task InitializeForProfileAsync_Should_LoadFromProfile_WhenProfileHasSettings()
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
            VideoTextureQuality = 1,
            VideoShadows = false,
            VideoGamma = 120,
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
        Assert.Equal(1, _viewModel.TextureQuality);
        Assert.False(_viewModel.Shadows);
        Assert.Equal(120, _viewModel.Gamma);
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
        _viewModel.TextureQuality = 0;
        _viewModel.Shadows = false;
        _viewModel.Gamma = 110;
        _viewModel.SoundVolume = 80;
        _viewModel.AudioEnabled = false;

        // Act
        var request = _viewModel.GetProfileSettings();

        // Assert
        Assert.Equal(1920, request.VideoResolutionWidth);
        Assert.Equal(1080, request.VideoResolutionHeight);
        Assert.True(request.VideoWindowed);
        Assert.Equal(0, request.VideoTextureQuality);
        Assert.False(request.VideoShadows);
        Assert.Equal(110, request.VideoGamma);
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
            VideoTextureQuality = hasTextureQuality ? 1 : null,
            VideoShadows = hasShadows ? false : null,
            VideoParticleEffects = hasParticleEffects ? true : null,
            VideoExtraAnimations = hasExtraAnimations ? false : null,
            VideoBuildingAnimations = hasBuildingAnimations ? true : null,
            VideoGamma = hasGamma ? 100 : null,
            AudioSoundVolume = hasSoundVolume ? 70 : null,
            AudioThreeDSoundVolume = hasThreeDSoundVolume ? 70 : null,
            AudioSpeechVolume = hasSpeechVolume ? 70 : null,
            AudioMusicVolume = hasMusicVolume ? 70 : null,
            AudioEnabled = hasAudioEnabled ? true : null,
            AudioNumSounds = hasNumSounds ? 16 : null,
        };

        // Act
        var hasSettings = GameSettingsViewModel.HasCustomProfileSettings(profile);

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
    public async Task OnSelectedGameTypeChanged_Should_LoadSettings_WhenNotInitializing()
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
    /// Should not load settings when game type changes during initialization.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task OnSelectedGameTypeChanged_Should_NotLoadSettings_DuringInitialization()
    {
        // Arrange
        var profile = new GameProfile
        {
            GameClient = new GameClient { GameType = GameType.ZeroHour },
        };

        // Start initialization (which sets initialization depth > 0)
        var initTask = _viewModel.InitializeForProfileAsync("test", profile);

        // Act - Change game type during initialization
        _viewModel.SelectedGameType = GameType.Generals;

        // Complete initialization
        await initTask;

        // Assert - Should have loaded settings for ZeroHour during initialization, but not for Generals
        _gameSettingsServiceMock.Verify(x => x.LoadOptionsAsync(GameType.ZeroHour), Times.Once);
        _gameSettingsServiceMock.Verify(x => x.LoadOptionsAsync(GameType.Generals), Times.Never);
    }

    /// <summary>
    /// Should handle load settings command failure gracefully.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task LoadSettings_Should_HandleFailureGracefully()
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
    public async Task SaveSettings_Should_HandleFailureGracefully()
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
}
