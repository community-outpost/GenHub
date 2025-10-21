using GenHub.Core.Interfaces.GameSettings;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameSettings;
using GenHub.Features.GameSettings;
using Microsoft.Extensions.Logging;
using Moq;

namespace GenHub.Tests.Core.Features.GameSettings;

/// <summary>
/// Tests for <see cref="GameSettingsService"/>.
/// </summary>
public class GameSettingsServiceTests
{
    private readonly Mock<ILogger<GameSettingsService>> _loggerMock = new();
    private readonly Mock<IGamePathProvider> _pathProviderMock = new();
    private readonly GameSettingsService _service;

    /// <summary>
    /// Initializes a new instance of the <see cref="GameSettingsServiceTests"/> class.
    /// </summary>
    public GameSettingsServiceTests()
    {
        _service = new GameSettingsService(_loggerMock.Object, _pathProviderMock.Object);
    }

    /// <summary>
    /// Should return correct path for Generals.
    /// </summary>
    [Fact]
    public void GetOptionsFilePath_Should_ReturnCorrectPath_ForGenerals()
    {
        // Arrange
        var expectedPath = Path.Combine("C:\\Users\\Test\\Documents\\Command and Conquer Generals Data", "Options.ini");
        _pathProviderMock.Setup(x => x.GetOptionsDirectory(GameType.Generals))
            .Returns("C:\\Users\\Test\\Documents\\Command and Conquer Generals Data");

        // Act
        var path = _service.GetOptionsFilePath(GameType.Generals);

        // Assert
        Assert.Equal(expectedPath, path);
    }

    /// <summary>
    /// Should return correct path for Zero Hour.
    /// </summary>
    [Fact]
    public void GetOptionsFilePath_Should_ReturnCorrectPath_ForZeroHour()
    {
        // Arrange
        var expectedPath = Path.Combine("C:\\Users\\Test\\Documents\\Command and Conquer Generals Zero Hour Data", "Options.ini");
        _pathProviderMock.Setup(x => x.GetOptionsDirectory(GameType.ZeroHour))
            .Returns("C:\\Users\\Test\\Documents\\Command and Conquer Generals Zero Hour Data");

        // Act
        var path = _service.GetOptionsFilePath(GameType.ZeroHour);

        // Assert
        Assert.Equal(expectedPath, path);
    }

    /// <summary>
    /// Should return true when file exists.
    /// </summary>
    [Fact]
    public void OptionsFileExists_Should_ReturnTrue_WhenFileExists()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        var tempDir = Path.GetDirectoryName(tempFile) !;
        var optionsPath = Path.Combine(tempDir, "Options.ini");
        File.Move(tempFile, optionsPath);

        try
        {
            var mockService = new Mock<GameSettingsService>(MockBehavior.Loose, _loggerMock.Object, _pathProviderMock.Object)
            {
                CallBase = true,
            };
            mockService.Setup(x => x.GetOptionsFilePath(It.IsAny<GameType>())).Returns(optionsPath);

            // Act
            var exists = mockService.Object.OptionsFileExists(GameType.Generals);

            // Assert
            Assert.True(exists);
        }
        finally
        {
            File.Delete(optionsPath);
        }
    }

    /// <summary>
    /// Should return false when file does not exist.
    /// </summary>
    [Fact]
    public void OptionsFileExists_Should_ReturnFalse_WhenFileDoesNotExist()
    {
        // Arrange
        var mockService = new Mock<GameSettingsService>(MockBehavior.Loose, _loggerMock.Object, _pathProviderMock.Object)
        {
            CallBase = true,
        };
        mockService.Setup(x => x.GetOptionsFilePath(It.IsAny<GameType>())).Returns("nonexistent.ini");

        // Act
        var exists = mockService.Object.OptionsFileExists(GameType.Generals);

        // Assert
        Assert.False(exists);
    }

    /// <summary>
    /// Should parse valid INI file correctly.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task LoadOptionsAsync_Should_ParseValidIniFile()
    {
        // Arrange
        var iniContent = @"[AUDIO]
SFXVolume=75
SFX3DVolume=80
VoiceVolume=85
MusicVolume=90
AudioEnabled=yes
NumSounds=20

[VIDEO]
Resolution=1920 1080
Windowed=no
TextureReduction=1
AntiAliasing=2
UseShadowVolumes=yes
UseShadowDecals=yes
ExtraAnimations=yes
Gamma=120
";

        var tempFile = Path.GetTempFileName();
        await File.WriteAllTextAsync(tempFile, iniContent);

        try
        {
            var mockService = new Mock<GameSettingsService>(MockBehavior.Loose, _loggerMock.Object, _pathProviderMock.Object)
            {
                CallBase = true,
            };
            mockService.Setup(x => x.GetOptionsFilePath(It.IsAny<GameType>())).Returns(tempFile);

            // Act
            var result = await mockService.Object.LoadOptionsAsync(GameType.Generals);

            // Assert
            Assert.True(result.Success);
            Assert.NotNull(result.Data);

            var options = result.Data!;
            Assert.Equal(75, options.Audio.SFXVolume);
            Assert.Equal(80, options.Audio.SFX3DVolume);
            Assert.Equal(85, options.Audio.VoiceVolume);
            Assert.Equal(90, options.Audio.MusicVolume);
            Assert.True(options.Audio.AudioEnabled);
            Assert.Equal(20, options.Audio.NumSounds);

            Assert.Equal(1920, options.Video.ResolutionWidth);
            Assert.Equal(1080, options.Video.ResolutionHeight);
            Assert.False(options.Video.Windowed);
            Assert.Equal(1, options.Video.TextureReduction);
            Assert.Equal(2, options.Video.AntiAliasing);
            Assert.True(options.Video.UseShadowVolumes);
            Assert.True(options.Video.UseShadowDecals);
            Assert.True(options.Video.ExtraAnimations);
            Assert.Equal(120, options.Video.Gamma);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    /// <summary>
    /// Should return success with defaults when file does not exist.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task LoadOptionsAsync_Should_ReturnSuccessWithDefaults_WhenFileDoesNotExist()
    {
        // Arrange
        var mockService = new Mock<GameSettingsService>(MockBehavior.Loose, _loggerMock.Object, _pathProviderMock.Object)
        {
            CallBase = true,
        };
        mockService.Setup(x => x.GetOptionsFilePath(It.IsAny<GameType>())).Returns("nonexistent.ini");

        // Act
        var result = await mockService.Object.LoadOptionsAsync(GameType.Generals);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal(70, result.Data.Audio.SFXVolume); // Default value
    }

    /// <summary>
    /// Should handle malformed INI file gracefully.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task LoadOptionsAsync_Should_HandleMalformedIniFile()
    {
        // Arrange
        var iniContent = @"[AUDIO]
SFXVolume=notanumber
InvalidLine
[AUDIO
MissingBracket
";

        var tempFile = Path.GetTempFileName();
        await File.WriteAllTextAsync(tempFile, iniContent);

        try
        {
            var mockService = new Mock<GameSettingsService>(MockBehavior.Loose, _loggerMock.Object, _pathProviderMock.Object)
            {
                CallBase = true,
            };
            mockService.Setup(x => x.GetOptionsFilePath(It.IsAny<GameType>())).Returns(tempFile);

            // Act
            var result = await mockService.Object.LoadOptionsAsync(GameType.Generals);

            // Assert
            Assert.True(result.Success); // Should not fail on malformed content
            Assert.NotNull(result.Data);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    /// <summary>
    /// Should save options to file correctly.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task SaveOptionsAsync_Should_SaveOptionsToFile()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        var options = new IniOptions
        {
            Audio = new AudioSettings
            {
                SFXVolume = 75,
                SFX3DVolume = 80,
                VoiceVolume = 85,
                MusicVolume = 90,
                AudioEnabled = true,
                NumSounds = 20,
            },
            Video = new VideoSettings
            {
                ResolutionWidth = 1920,
                ResolutionHeight = 1080,
                Windowed = false,
                TextureReduction = 1,
                AntiAliasing = 2,
                UseShadowVolumes = true,
                UseShadowDecals = true,
                ExtraAnimations = true,
                Gamma = 120,
            },
        };

        var mockService = new Mock<GameSettingsService>(MockBehavior.Loose, _loggerMock.Object, _pathProviderMock.Object)
        {
            CallBase = true,
        };
        mockService.Setup(x => x.GetOptionsFilePath(It.IsAny<GameType>())).Returns(tempFile);

        try
        {
            // Act
            var result = await mockService.Object.SaveOptionsAsync(GameType.Generals, options);

            // Assert
            Assert.True(result.Success);

            var savedContent = await File.ReadAllTextAsync(tempFile);
            Assert.Contains("[AUDIO]", savedContent);
            Assert.Contains("SFXVolume=75", savedContent);
            Assert.Contains("Resolution=1920 1080", savedContent);
            Assert.Contains("Windowed=no", savedContent);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    /// <summary>
    /// Should handle boolean values correctly in serialization.
    /// </summary>
    /// <param name="value">The boolean value to test.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BoolToString_Should_SerializeCorrectly(bool value)
    {
        // This is testing the private BoolToString method indirectly through SaveOptionsAsync
        var options = new IniOptions
        {
            Audio = new AudioSettings { AudioEnabled = value },
            Video = new VideoSettings { Windowed = value },
        };

        var tempFile = Path.GetTempFileName();

        var mockService = new Mock<GameSettingsService>(MockBehavior.Loose, _loggerMock.Object, _pathProviderMock.Object)
        {
            CallBase = true,
        };
        mockService.Setup(x => x.GetOptionsFilePath(It.IsAny<GameType>())).Returns(tempFile);

        try
        {
            // Act - Save and then reload to verify round-trip
            await mockService.Object.SaveOptionsAsync(GameType.Generals, options);
            var loadResult = await mockService.Object.LoadOptionsAsync(GameType.Generals);

            var loadedOptions = loadResult.Data!;

            // Assert
            Assert.Equal(value, loadedOptions.Audio.AudioEnabled);
            Assert.Equal(value, loadedOptions.Video.Windowed);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    /// <summary>
    /// Should preserve unknown sections when saving.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task SaveOptionsAsync_Should_PreserveUnknownSections()
    {
        // Arrange
        var originalContent = @"[AUDIO]
SFXVolume=70

[CUSTOM_SECTION]
CustomKey=CustomValue
AnotherKey=AnotherValue

[VIDEO]
Resolution=1024 768
";

        var tempFile = Path.GetTempFileName();
        await File.WriteAllTextAsync(tempFile, originalContent);

        var mockService = new Mock<GameSettingsService>(MockBehavior.Loose, _loggerMock.Object, _pathProviderMock.Object)
        {
            CallBase = true,
        };
        mockService.Setup(x => x.GetOptionsFilePath(It.IsAny<GameType>())).Returns(tempFile);

        // Load and then save back
        var loadResult = await mockService.Object.LoadOptionsAsync(GameType.Generals);
        var saveResult = await mockService.Object.SaveOptionsAsync(GameType.Generals, loadResult.Data!);

        // Assert
        Assert.True(saveResult.Success);

        var savedContent = await File.ReadAllTextAsync(tempFile);
        Assert.Contains("[CUSTOM_SECTION]", savedContent);
        Assert.Contains("CustomKey=CustomValue", savedContent);
        Assert.Contains("AnotherKey=AnotherValue", savedContent);
    }
}
