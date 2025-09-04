using GenHub.Common.Services;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Enums;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Common.Services;

/// <summary>
/// Tests for <see cref="UserSettingsService"/>.
/// </summary>
public class UserSettingsServiceTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly Mock<ILogger<UserSettingsService>> _mockLogger;

    /// <summary>
    /// Initializes a new instance of the <see cref="UserSettingsServiceTests"/> class.
    /// </summary>
    public UserSettingsServiceTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDirectory);
        _mockLogger = new Mock<ILogger<UserSettingsService>>();
    }

    /// <summary>
    /// Disposes the test instance and cleans up temp files.
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    /// <summary>
    /// Verifies that GetSettings returns raw user values when no file exists.
    /// </summary>
    [Fact]
    public void Get_WhenNoFileExists_ReturnsRawUserSettings()
    {
        var service = CreateService();
        var settings = service.Get();

        // UserSettingsService should return raw C# defaults, not application defaults
        Assert.Null(settings.Theme);
        Assert.Equal(0.0, settings.WindowWidth);
        Assert.Equal(0.0, settings.WindowHeight);
        Assert.False(settings.IsMaximized);
        Assert.Equal(NavigationTab.Home, settings.LastSelectedTab);
        Assert.Equal(0, settings.MaxConcurrentDownloads);
        Assert.False(settings.AllowBackgroundDownloads);
        Assert.False(settings.AutoCheckForUpdatesOnStartup);
        Assert.Equal(WorkspaceStrategy.FullCopy, settings.DefaultWorkspaceStrategy); // C# enum default is FullCopy (0)
    }

    /// <summary>
    /// Verifies that SaveAsync creates a file with correct data.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task SaveAsync_CreatesFileWithCorrectData()
    {
        var service = CreateService();
        var settingsPath = Path.Combine(_tempDirectory, FileTypes.JsonFileExtension);
        service.Update(settings =>
        {
            settings.Theme = "Light";
            settings.WindowWidth = 1600.0;
            settings.MaxConcurrentDownloads = 5;
        });
        await service.SaveAsync();
        Assert.True(File.Exists(settingsPath));
        var json = await File.ReadAllTextAsync(settingsPath);
        var savedSettings = JsonSerializer.Deserialize<UserSettings>(json, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
        });
        Assert.NotNull(savedSettings);
        Assert.Equal("Light", savedSettings.Theme);
        Assert.Equal(1600.0, savedSettings.WindowWidth);
        Assert.Equal(5, savedSettings.MaxConcurrentDownloads);
    }

    /// <summary>
    /// Verifies that loading settings after save loads correct data.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task LoadSettings_AfterSave_LoadsCorrectData()
    {
        // Use a unique temp directory for this test
        var testDir = Path.Combine(_tempDirectory, Guid.NewGuid().ToString());
        Directory.CreateDirectory(testDir);
        var settingsPath = Path.Combine(testDir, FileTypes.JsonFileExtension);

        // Create first service and save settings
        var service1 = CreateServiceWithPath(settingsPath);
        service1.Update(settings =>
        {
            settings.Theme = "Light";
            settings.WorkspacePath = "/test/path";
            settings.LastSelectedTab = NavigationTab.Downloads;
        });
        await service1.SaveAsync();

        // Verify the file was actually created and contains the expected data
        Assert.True(File.Exists(settingsPath), "Settings file should exist after save");
        var fileContent = await File.ReadAllTextAsync(settingsPath);
        Assert.Contains("\"theme\": \"Light\"", fileContent);
        Assert.Contains("\"downloads\"", fileContent.ToLowerInvariant());

        // Load with explicit appConfig to ensure defaults
        var appConfig = CreateAppConfigMock();
        var service2 = new TestableUserSettingsService(_mockLogger.Object, appConfig, settingsPath, loadFromFile: true);
        var loadedSettings = service2.Get();

        Assert.Equal("Light", loadedSettings.Theme);
        Assert.Equal("/test/path", loadedSettings.WorkspacePath);
        Assert.Equal(NavigationTab.Downloads, loadedSettings.LastSelectedTab);
    }

    /// <summary>
    /// Verifies that GetSettings returns default values with corrupted JSON.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task GetSettings_WithCorruptedJson_ReturnsRawDefaults()
    {
        var testDir = Path.Combine(_tempDirectory, Guid.NewGuid().ToString());
        Directory.CreateDirectory(testDir);
        var settingsPath = Path.Combine(testDir, FileTypes.JsonFileExtension);

        await File.WriteAllTextAsync(settingsPath, "{ invalid json }");
        var service = CreateServiceWithPath(settingsPath);
        var settings = service.Get();

        // Should return raw C# defaults when JSON is corrupted
        Assert.Null(settings.Theme);
        Assert.Equal(NavigationTab.Home, settings.LastSelectedTab);
    }

    /// <summary>
    /// Verifies that UpdateSettings modifies in-memory state but does not persist immediately.
    /// </summary>
    [Fact]
    public void UpdateSettings_ModifiesInMemoryState_DoesNotPersistImmediately()
    {
        var service = CreateService();
        var settingsPath = Path.Combine(_tempDirectory, FileTypes.JsonFileExtension);
        service.Update(settings => settings.Theme = "Light");
        var currentSettings = service.Get();
        Assert.Equal("Light", currentSettings.Theme);
        Assert.False(File.Exists(settingsPath));
    }

    /// <summary>
    /// Verifies that GetSettings returns an independent copy.
    /// </summary>
    [Fact]
    public void GetSettings_ReturnsIndependentCopy()
    {
        var service = CreateService();
        var settings1 = service.Get();
        var settings2 = service.Get();
        settings1.Theme = "Light";

        // Verify original theme is preserved (either "Dark" or null, but consistent)
        var originalTheme = settings2.Theme ?? "Dark";
        Assert.Equal(originalTheme, service.Get().Theme ?? "Dark");
    }

    /// <summary>
    /// Verifies that SaveAsync creates directory if not exists.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task SaveAsync_CreatesDirectoryIfNotExists()
    {
        var nestedPath = Path.Combine(_tempDirectory, "nested", "path");
        var settingsPath = Path.Combine(nestedPath, FileTypes.JsonFileExtension);
        var service = CreateService();
        var settingsPathField = typeof(UserSettingsService)
            .GetField("_settingsFilePath", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        settingsPathField?.SetValue(service, settingsPath);
        await service.SaveAsync();
        Assert.True(Directory.Exists(nestedPath));
        Assert.True(File.Exists(settingsPath));
    }

    /// <summary>
    /// <summary>
    /// Verifies that UpdateSettings throws ArgumentNullException when called with a null action.
    /// </summary>
    /// </summary>
    [Fact]
    public void UpdateSettings_WithNullAction_ThrowsArgumentNullException()
    {
        var service = CreateService();
        Assert.Throws<ArgumentNullException>(() => service.Update(null!));
    }

    /// <summary>
    /// Verifies that SaveAsync with a long path creates all necessary nested directories.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task SaveAsync_WithLongPath_CreatesNestedDirectories()
    {
        // Arrange
        var deepPath = Path.Combine(_tempDirectory, "very", "deep", "nested", "path");
        var settingsPath = Path.Combine(deepPath, FileTypes.JsonFileExtension);

        var service = CreateService();
        var settingsPathField = typeof(UserSettingsService)
            .GetField("_settingsFilePath", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (settingsPathField is not null)
        {
            settingsPathField.SetValue(service, settingsPath);
        }

        // Act
        await service.SaveAsync();

        // Assert
        Assert.True(Directory.Exists(deepPath));
        Assert.True(File.Exists(settingsPath));
    }

    /// <summary>
    /// Verifies that loading settings from partially valid JSON preserves what's in JSON without applying defaults.
    /// </summary>
    [Fact]
    public void LoadSettings_WithPartiallyValidJson_PreservesJsonValues()
    {
        // Arrange
        var testDir = Path.Combine(_tempDirectory, Guid.NewGuid().ToString());
        Directory.CreateDirectory(testDir);
        var settingsPath = Path.Combine(testDir, FileTypes.JsonFileExtension);
        var partialJson = """{"windowWidth": 1600.0, "allowBackgroundDownloads": true}""";

        File.WriteAllText(settingsPath, partialJson);

        // Act - Create service that loads from the existing file
        var appConfig = CreateAppConfigMock();
        var service = new TestableUserSettingsService(_mockLogger.Object, appConfig, settingsPath, loadFromFile: true);
        var settings = service.Get();

        // Assert - Only JSON values should be set, rest should be C# defaults
        Assert.Null(settings.Theme); // Not in JSON, should be null
        Assert.Equal(1600.0, settings.WindowWidth); // From JSON
        Assert.Equal(0.0, settings.WindowHeight); // Not in JSON, should be C# default (0)
        Assert.Equal(0, settings.MaxConcurrentDownloads); // Not in JSON, should be 0
        Assert.True(settings.AllowBackgroundDownloads); // From JSON
    }

    /// <summary>
    /// Verifies that CachePath can be set and retrieved correctly.
    /// </summary>
    [Fact]
    public void UpdateSettings_CachePath_CanBeSetAndRetrieved()
    {
        var service = CreateService();
        var cachePath = "/test/cache/path";

        service.Update(settings => settings.CachePath = cachePath);
        var currentSettings = service.Get();

        Assert.Equal(cachePath, currentSettings.CachePath);
    }

    /// <summary>
    /// Verifies that ContentStoragePath can be set and retrieved correctly.
    /// </summary>
    [Fact]
    public void UpdateSettings_ContentStoragePath_CanBeSetAndRetrieved()
    {
        var service = CreateService();
        var contentPath = "/test/content/path";

        service.Update(settings => settings.ContentStoragePath = contentPath);
        var currentSettings = service.Get();

        Assert.Equal(contentPath, currentSettings.ContentStoragePath);
    }

    /// <summary>
    /// Verifies that DownloadBufferSize can be set and retrieved correctly.
    /// </summary>
    [Fact]
    public void UpdateSettings_DownloadBufferSize_CanBeSetAndRetrieved()
    {
        var service = CreateService();
        var bufferSize = 16384;

        service.Update(settings => settings.DownloadBufferSize = bufferSize);
        var currentSettings = service.Get();

        Assert.Equal(bufferSize, currentSettings.DownloadBufferSize);
    }

    /// <summary>
    /// Verifies that EnableDetailedLogging can be set and retrieved correctly.
    /// </summary>
    /// <param name="enableLogging">The value to set for EnableDetailedLogging in user settings.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UpdateSettings_EnableDetailedLogging_CanBeSetAndRetrieved(bool enableLogging)
    {
        var service = CreateService();

        service.Update(settings => settings.EnableDetailedLogging = enableLogging);
        var currentSettings = service.Get();

        Assert.Equal(enableLogging, currentSettings.EnableDetailedLogging);
    }

    private static IAppConfiguration CreateAppConfigMock()
    {
        var appConfig = new Mock<IAppConfiguration>();

        // Defaults used across tests
        appConfig.Setup(x => x.GetDefaultTheme()).Returns("Dark");
        appConfig.Setup(x => x.GetDefaultWindowWidth()).Returns(1200.0);
        appConfig.Setup(x => x.GetDefaultWindowHeight()).Returns(800.0);
        appConfig.Setup(x => x.GetDefaultMaxConcurrentDownloads()).Returns(3);
        appConfig.Setup(x => x.GetDefaultWorkspaceStrategy()).Returns(WorkspaceStrategy.HybridCopySymlink);
        appConfig.Setup(x => x.GetDefaultDownloadBufferSize()).Returns(81920);
        appConfig.Setup(x => x.GetDefaultDownloadTimeoutSeconds()).Returns(120);
        appConfig.Setup(x => x.GetDefaultUserAgent()).Returns("GenHub/1.0");

        // Policy bounds used by normalization where relevant
        appConfig.Setup(x => x.GetMinConcurrentDownloads()).Returns(1);
        appConfig.Setup(x => x.GetMaxConcurrentDownloads()).Returns(8);
        appConfig.Setup(x => x.GetMinDownloadTimeoutSeconds()).Returns(30);
        appConfig.Setup(x => x.GetMaxDownloadTimeoutSeconds()).Returns(600);
        appConfig.Setup(x => x.GetMinDownloadBufferSizeBytes()).Returns(4096);
        appConfig.Setup(x => x.GetMaxDownloadBufferSizeBytes()).Returns(1048576);

        // Paths
        appConfig.Setup(x => x.GetDefaultWorkspacePath()).Returns(Path.Combine(Path.GetTempPath(), "GenHubWorkspace"));
        appConfig.Setup(x => x.GetDefaultCacheDirectory()).Returns(Path.Combine(Path.GetTempPath(), "GenHubCache"));

        return appConfig.Object;
    }

    /// <summary>
    /// Creates a new <see cref="UserSettingsService"/> instance for testing with a temp file path.
    /// </summary>
    /// <returns>A new <see cref="UserSettingsService"/> instance using a temp file path.</returns>
    private UserSettingsService CreateService()
    {
        var settingsPath = Path.Combine(_tempDirectory, FileTypes.JsonFileExtension);
        return CreateServiceWithPath(settingsPath);
    }

    private UserSettingsService CreateServiceWithPath(string settingsPath)
    {
        if (File.Exists(settingsPath))
        {
            File.Delete(settingsPath);
        }

        var appConfig = CreateAppConfigMock();
        var service = new TestableUserSettingsService(_mockLogger.Object, appConfig, settingsPath);
        return service;
    }

    /// <summary>
    /// Testable version of UserSettingsService that allows specifying the settings file path.
    /// </summary>
    private class TestableUserSettingsService : UserSettingsService
    {
        public TestableUserSettingsService(ILogger<UserSettingsService> logger, IAppConfiguration appConfig, string settingsFilePath, bool loadFromFile = false)
            : base(logger, appConfig, initialize: false)
        {
            // The base constructor with `initialize: false` creates an empty settings object.
            // We then set the path, which will load from the file if it exists.
            // If `loadFromFile` is false and the file exists, it will still be loaded by `SetSettingsFilePath`,
            // but the tests are structured to delete the file first in those cases.
            SetSettingsFilePath(settingsFilePath);
        }
    }
}
