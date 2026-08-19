using GenHub.Core.Constants;
using GenHub.Core.Interfaces.GameSettings;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameSettings;
using GenHub.Features.GameSettings;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;

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
        var tempDir = Path.GetDirectoryName(tempFile)!;
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
    public async Task LoadOptionsAsync_Should_ParseValidIniFileAsync()
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
Gamma=60
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
            Assert.Equal(60, options.Video.Gamma);
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
    public async Task LoadOptionsAsync_Should_ReturnSuccessWithDefaults_WhenFileDoesNotExistAsync()
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
    public async Task LoadOptionsAsync_Should_HandleMalformedIniFileAsync()
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
    public async Task SaveOptionsAsync_Should_SaveOptionsToFileAsync()
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
                Gamma = 60,
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
    public async Task BoolToString_Should_SerializeCorrectlyAsync(bool value)
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
    public async Task SaveOptionsAsync_Should_PreserveUnknownSectionsAsync()
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

    /// <summary>
    /// Should replace settings.json by moving a completed file over it, leaving nothing behind,
    /// because a half-written settings.json costs the GeneralsOnline client every key it owns.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task SaveGeneralsOnlineSettingsAsync_Should_ReplaceTheFileWithoutTruncatingItAsync()
    {
        // Arrange
        var directory = Directory.CreateTempSubdirectory().FullName;
        var settingsPath = Path.Combine(directory, GameSettingsGeneralsOnlineConstants.SettingsFileName);
        await File.WriteAllTextAsync(settingsPath, "{ \"chat_font_size\": 8 }");
        var service = CreateServiceWritingGeneralsOnlineSettingsTo(settingsPath);

        try
        {
            // Act
            var result = await service.SaveGeneralsOnlineSettingsAsync(new GeneralsOnlineSettings { ChatFontSize = 24 });

            // Assert
            Assert.True(result.Success, result.FirstError);
            var reloaded = await service.LoadGeneralsOnlineSettingsAsync();
            Assert.True(reloaded.Success, reloaded.FirstError);
            Assert.Equal(24, reloaded.Data!.ChatFontSize);
            Assert.Empty(Directory.GetFiles(directory, $"*{GameSettingsGeneralsOnlineConstants.TemporarySettingsFileExtension}"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// Should report success for every one of a set of concurrent saves, which two GeneralsOnline
    /// launches produce because the launch lock is per profile while settings.json is a single
    /// global file. Which save wins is not defined, but none of them may be turned away: a launch
    /// that reports a settings failure has lost the settings the user chose for that profile.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task SaveGeneralsOnlineSettingsAsync_Should_SucceedForEverySave_WhenSavesOverlapAsync()
    {
        // Arrange
        var directory = Directory.CreateTempSubdirectory().FullName;
        var settingsPath = Path.Combine(directory, GameSettingsGeneralsOnlineConstants.SettingsFileName);
        var service = CreateServiceWritingGeneralsOnlineSettingsTo(settingsPath);

        try
        {
            // Act
            var fontSizes = Enumerable.Range(
                GameSettingsGeneralsOnlineConstants.MinChatFontSize,
                GameSettingsGeneralsOnlineConstants.MaxChatFontSize - GameSettingsGeneralsOnlineConstants.MinChatFontSize);
            var results = await Task.WhenAll(
                fontSizes.Select(fontSize => service.SaveGeneralsOnlineSettingsAsync(new GeneralsOnlineSettings { ChatFontSize = fontSize })));

            // Assert
            Assert.All(results, result => Assert.True(result.Success, result.FirstError));
            var reloaded = await service.LoadGeneralsOnlineSettingsAsync();
            Assert.True(reloaded.Success, reloaded.FirstError);
            Assert.InRange(
                reloaded.Data!.ChatFontSize,
                GameSettingsGeneralsOnlineConstants.MinChatFontSize,
                GameSettingsGeneralsOnlineConstants.MaxChatFontSize);
            Assert.Empty(Directory.GetFiles(directory, $"*{GameSettingsGeneralsOnlineConstants.TemporarySettingsFileExtension}"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// Should keep both concurrent saves and concurrent loads working against the one global
    /// settings.json. A load that overlaps the replacement of the file it is reading is the
    /// other half of the same race, because the GameLauncher reads settings.json before every
    /// save it makes.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task GeneralsOnlineSettings_Should_SucceedForEveryCall_WhenLoadsAndSavesOverlapAsync()
    {
        // Arrange
        var directory = Directory.CreateTempSubdirectory().FullName;
        var settingsPath = Path.Combine(directory, GameSettingsGeneralsOnlineConstants.SettingsFileName);
        var service = CreateServiceWritingGeneralsOnlineSettingsTo(settingsPath);
        await service.SaveGeneralsOnlineSettingsAsync(new GeneralsOnlineSettings { ChatFontSize = GameSettingsGeneralsOnlineConstants.DefaultChatFontSize });

        try
        {
            // Act
            var fontSizes = Enumerable.Range(
                GameSettingsGeneralsOnlineConstants.MinChatFontSize,
                GameSettingsGeneralsOnlineConstants.MaxChatFontSize - GameSettingsGeneralsOnlineConstants.MinChatFontSize)
                .ToList();
            var saves = Task.WhenAll(fontSizes.Select(fontSize => service.SaveGeneralsOnlineSettingsAsync(new GeneralsOnlineSettings { ChatFontSize = fontSize })));
            var loads = Task.WhenAll(fontSizes.Select(_ => service.LoadGeneralsOnlineSettingsAsync()));
            var saveResults = await saves;
            var loadResults = await loads;

            // Assert
            Assert.All(saveResults, result => Assert.True(result.Success, result.FirstError));
            Assert.All(loadResults, result => Assert.True(result.Success, result.FirstError));
            Assert.All(
                loadResults,
                result => Assert.InRange(
                    result.Data!.ChatFontSize,
                    GameSettingsGeneralsOnlineConstants.MinChatFontSize,
                    GameSettingsGeneralsOnlineConstants.MaxChatFontSize));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// Should report the failure once a replacement that cannot succeed has used up its
    /// attempts, rather than retrying a real fault forever or claiming a save that never
    /// happened, and should leave no temporary file behind when it does.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task SaveGeneralsOnlineSettingsAsync_Should_ReportFailure_WhenTheReplacementNeverSucceedsAsync()
    {
        // Arrange
        var directory = Directory.CreateTempSubdirectory().FullName;
        var settingsPath = Path.Combine(directory, GameSettingsGeneralsOnlineConstants.SettingsFileName);
        Directory.CreateDirectory(settingsPath);
        var service = CreateServiceWritingGeneralsOnlineSettingsTo(settingsPath);

        try
        {
            // Act
            var result = await service.SaveGeneralsOnlineSettingsAsync(new GeneralsOnlineSettings());

            // Assert
            Assert.False(result.Success);
            Assert.Empty(Directory.GetFiles(directory, $"*{GameSettingsGeneralsOnlineConstants.TemporarySettingsFileExtension}"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private GameSettingsService CreateServiceWritingGeneralsOnlineSettingsTo(string settingsPath)
    {
        var mockService = new Mock<GameSettingsService>(MockBehavior.Loose, _loggerMock.Object, _pathProviderMock.Object)
        {
            CallBase = true,
        };
        mockService.Protected().Setup<string>("GetGeneralsOnlineSettingsPath").Returns(settingsPath);
        return mockService.Object;
    }
}
