using GenHub.Common.Services;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Enums;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Common.Services;

/// <summary>
/// Tests for <see cref="ConfigurationService"/>.
/// </summary>
public class ConfigurationServiceTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly Mock<ILogger<ConfigurationService>> _mockLogger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigurationServiceTests"/> class.
    /// </summary>
    public ConfigurationServiceTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDirectory);
        _mockLogger = new Mock<ILogger<ConfigurationService>>();
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
    /// Verifies that GetSettings returns default values when no file exists.
    /// </summary>
    [Fact]
    public void GetSettings_WhenNoFileExists_ReturnsDefaultValues()
    {
        var service = CreateService();
        var settings = service.GetSettings();
        Assert.Equal("Dark", settings.Theme);
        Assert.Equal(1200.0, settings.WindowWidth);
        Assert.Equal(800.0, settings.WindowHeight);
        Assert.False(settings.IsMaximized);
        Assert.Equal(NavigationTab.GameProfiles, settings.LastSelectedTab); // Use actual default
        Assert.Equal(3, settings.MaxConcurrentDownloads);
        Assert.True(settings.AllowBackgroundDownloads);
        Assert.True(settings.AutoCheckForUpdatesOnStartup);
        Assert.Equal(WorkspaceStrategy.HybridCopySymlink, settings.DefaultWorkspaceStrategy);
    }

    /// <summary>
    /// Verifies that SaveAsync creates a file with correct data.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task SaveAsync_CreatesFileWithCorrectData()
    {
        var service = CreateService();
        var settingsPath = Path.Combine(_tempDirectory, "settings.json");
        service.UpdateSettings(settings =>
        {
            settings.Theme = "Light";
            settings.WindowWidth = 1600.0;
            settings.MaxConcurrentDownloads = 5;
        });
        await service.SaveAsync();
        Assert.True(File.Exists(settingsPath));
        var json = await File.ReadAllTextAsync(settingsPath);
        var savedSettings = JsonSerializer.Deserialize<AppSettings>(json, new JsonSerializerOptions
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
        var settingsPath = Path.Combine(testDir, "settings.json");

        // Create first service and save settings
        var service1 = CreateServiceWithPath(settingsPath);
        service1.UpdateSettings(settings =>
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

        // Important: Don't delete the file this time - we want the second service to load it
        var service2 = new TestableConfigurationService(_mockLogger.Object, settingsPath, loadFromFile: true);
        var loadedSettings = service2.GetSettings();

        Assert.Equal("Light", loadedSettings.Theme);
        Assert.Equal("/test/path", loadedSettings.WorkspacePath);
        Assert.Equal(NavigationTab.Downloads, loadedSettings.LastSelectedTab);
    }

    /// <summary>
    /// Verifies that GetSettings returns default values with corrupted JSON.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task GetSettings_WithCorruptedJson_ReturnsDefaultValues()
    {
        var testDir = Path.Combine(_tempDirectory, Guid.NewGuid().ToString());
        Directory.CreateDirectory(testDir);
        var settingsPath = Path.Combine(testDir, "settings.json");

        await File.WriteAllTextAsync(settingsPath, "{ invalid json }");
        var service = CreateServiceWithPath(settingsPath);
        var settings = service.GetSettings();
        Assert.Equal("Dark", settings.Theme);
        Assert.Equal(NavigationTab.GameProfiles, settings.LastSelectedTab); // Use actual default
    }

    /// <summary>
    /// Verifies that UpdateSettings modifies in-memory state but does not persist immediately.
    /// </summary>
    [Fact]
    public void UpdateSettings_ModifiesInMemoryState_DoesNotPersistImmediately()
    {
        var service = CreateService();
        var settingsPath = Path.Combine(_tempDirectory, "settings.json");
        service.UpdateSettings(settings => settings.Theme = "Light");
        var currentSettings = service.GetSettings();
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
        var settings1 = service.GetSettings();
        var settings2 = service.GetSettings();
        settings1.Theme = "Light";
        Assert.Equal("Dark", settings2.Theme);
        Assert.Equal("Dark", service.GetSettings().Theme);
    }

    /// <summary>
    /// Verifies that SaveAsync creates directory if not exists.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task SaveAsync_CreatesDirectoryIfNotExists()
    {
        var nestedPath = Path.Combine(_tempDirectory, "nested", "path");
        var settingsPath = Path.Combine(nestedPath, "settings.json");
        var service = CreateService();
        var settingsPathField = typeof(ConfigurationService)
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
        Assert.Throws<ArgumentNullException>(() => service.UpdateSettings(null!));
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
        var settingsPath = Path.Combine(deepPath, "settings.json");

        var service = CreateService();
        var settingsPathField = typeof(ConfigurationService)
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
    /// Verifies that loading settings from partially valid JSON fills missing properties with default values.
    /// </summary>
    [Fact]
    public void LoadSettings_WithPartiallyValidJson_FillsDefaults()
    {
        // Arrange
        var testDir = Path.Combine(_tempDirectory, Guid.NewGuid().ToString());
        Directory.CreateDirectory(testDir);
        var settingsPath = Path.Combine(testDir, "settings.json");
        var partialJson = """{"windowWidth": 1600.0}""";

        File.WriteAllText(settingsPath, partialJson);

        // Verify the file was written correctly
        var writtenContent = File.ReadAllText(settingsPath);
        Assert.Contains("1600", writtenContent);

        // Act - Create service that loads from the existing file
        var service = new TestableConfigurationService(_mockLogger.Object, settingsPath, loadFromFile: true);
        var settings = service.GetSettings();

        // Assert
        Assert.Equal("Dark", settings.Theme); // Should use default
        Assert.Equal(1600.0, settings.WindowWidth); // Should use JSON value
        Assert.Equal(800.0, settings.WindowHeight); // Should use default
        Assert.Equal(3, settings.MaxConcurrentDownloads); // Should use default
    }

    /// <summary>
    /// Creates a new <see cref="ConfigurationService"/> instance for testing with a temp file path.
    /// </summary>
    /// <returns>A new <see cref="ConfigurationService"/> instance using a temp file path.</returns>
    private ConfigurationService CreateService()
    {
        var settingsPath = Path.Combine(_tempDirectory, "settings.json");
        return CreateServiceWithPath(settingsPath);
    }

    private ConfigurationService CreateServiceWithPath(string settingsPath)
    {
        // Ensure the test file doesn't exist initially
        if (File.Exists(settingsPath))
            File.Delete(settingsPath);

        // Create a service that bypasses normal initialization
        var service = new TestableConfigurationService(_mockLogger.Object, settingsPath);
        return service;
    }

    /// <summary>
    /// Testable version of ConfigurationService that allows specifying the settings file path.
    /// </summary>
    private class TestableConfigurationService : ConfigurationService
    {
        public TestableConfigurationService(ILogger<ConfigurationService> logger, string settingsFilePath, bool loadFromFile = false)
            : base(logger, false) // Bypass normal initialization
        {
            // Set the custom path and load settings from it
            SetSettingsFilePath(settingsFilePath);

            // If loadFromFile is true and file exists, force reload from file
            if (loadFromFile && File.Exists(settingsFilePath))
            {
                SetSettingsFilePath(settingsFilePath); // This will reload from the file
            }
        }
    }
}
