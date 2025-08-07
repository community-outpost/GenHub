using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Enums;
using GenHub.Features.Settings.ViewModels;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace GenHub.Tests.Core.ViewModels;

/// <summary>
/// Unit tests for <see cref="SettingsViewModel"/>.
/// </summary>
public class SettingsViewModelTests
{
    private readonly Mock<IUserSettingsService> _mockConfigService;
    private readonly Mock<ILogger<SettingsViewModel>> _mockLogger;
    private readonly UserSettings _defaultSettings;

    /// <summary>
    /// Initializes a new instance of the <see cref="SettingsViewModelTests"/> class.
    /// </summary>
    public SettingsViewModelTests()
    {
        _mockConfigService = new Mock<IUserSettingsService>();
        _mockLogger = new Mock<ILogger<SettingsViewModel>>();
        _defaultSettings = new UserSettings();

        _mockConfigService.Setup(x => x.GetSettings()).Returns(_defaultSettings);
    }

    /// <summary>
    /// Verifies that the constructor loads settings from the configuration service.
    /// </summary>
    [Fact]
    public void Constructor_LoadsSettingsFromUserSettingsService()
    {
        // Arrange
        var customSettings = new UserSettings
        {
            Theme = "Light",
            MaxConcurrentDownloads = 5,
            EnableDetailedLogging = true,
            WorkspacePath = "/custom/path",
        };

        _mockConfigService.Setup(x => x.GetSettings()).Returns(customSettings);

        // Act
        var viewModel = new SettingsViewModel(_mockConfigService.Object, _mockLogger.Object);

        // Assert
        Assert.Equal("Light", viewModel.Theme);
        Assert.Equal(5, viewModel.MaxConcurrentDownloads);
        Assert.True(viewModel.EnableDetailedLogging);
        Assert.Equal("/custom/path", viewModel.WorkspacePath);
    }

    /// <summary>
    /// Verifies that SaveSettingsCommand updates the configuration service.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task SaveSettingsCommand_UpdatesUserSettingsService()
    {
        // Arrange
        var viewModel = new SettingsViewModel(_mockConfigService.Object, _mockLogger.Object);
        viewModel.Theme = "Light";
        viewModel.MaxConcurrentDownloads = 5;

        // Act
        await Task.Run(() => viewModel.SaveSettingsCommand.Execute(null));

        // Assert
        _mockConfigService.Verify(x => x.UpdateSettings(It.IsAny<Action<UserSettings>>()), Times.Once);
        _mockConfigService.Verify(x => x.SaveAsync(), Times.Once);
    }

    /// <summary>
    /// Verifies that ResetToDefaultsCommand resets all properties.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task ResetToDefaultsCommand_ResetsAllProperties()
    {
        // Arrange
        var viewModel = new SettingsViewModel(_mockConfigService.Object, _mockLogger.Object);
        viewModel.Theme = "Light";
        viewModel.MaxConcurrentDownloads = 10;
        viewModel.EnableDetailedLogging = true;

        // Act
        await Task.Run(() => viewModel.ResetToDefaultsCommand.Execute(null));

        // Assert
        Assert.Equal("Dark", viewModel.Theme);
        Assert.Equal(3, viewModel.MaxConcurrentDownloads);
        Assert.False(viewModel.EnableDetailedLogging);
        Assert.Equal(WorkspaceStrategy.HybridCopySymlink, viewModel.DefaultWorkspaceStrategy);
    }

    /// <summary>
    /// Verifies that MaxConcurrentDownloads is set within bounds.
    /// </summary>
    [Fact]
    public void MaxConcurrentDownloads_SetsValueWithinBounds()
    {
        // Arrange
        var viewModel = new SettingsViewModel(_mockConfigService.Object, _mockLogger.Object);

        // Act & Assert - Test lower bound
        viewModel.MaxConcurrentDownloads = 0;
        Assert.Equal(1, viewModel.MaxConcurrentDownloads); // ViewModel clamps to 1

        // Act & Assert - Test upper bound
        viewModel.MaxConcurrentDownloads = 15;
        Assert.Equal(10, viewModel.MaxConcurrentDownloads); // ViewModel clamps to 10

        // Act & Assert - Test valid value
        viewModel.MaxConcurrentDownloads = 5;
        Assert.Equal(5, viewModel.MaxConcurrentDownloads);
    }

    /// <summary>
    /// Verifies that AvailableThemes returns expected values.
    /// </summary>
    [Fact]
    public void AvailableThemes_ReturnsExpectedValues()
    {
        // Arrange
        var viewModel = new SettingsViewModel(_mockConfigService.Object, _mockLogger.Object);

        // Act
        var themes = viewModel.AvailableThemes.ToList();

        // Assert
        Assert.Contains("Dark", themes);
        Assert.Contains("Light", themes);
        Assert.Equal(2, themes.Count);
    }

    /// <summary>
    /// Verifies that AvailableWorkspaceStrategies returns all enum values.
    /// </summary>
    [Fact]
    public void AvailableWorkspaceStrategies_ReturnsAllEnumValues()
    {
        // Arrange
        var viewModel = new SettingsViewModel(_mockConfigService.Object, _mockLogger.Object);

        // Act
        var strategies = viewModel.AvailableWorkspaceStrategies.ToList();

        // Assert
        Assert.Contains(WorkspaceStrategy.HybridCopySymlink, strategies);

        // Add assertions for other workspace strategies as they're implemented
    }

    /// <summary>
    /// Verifies that SaveSettingsCommand handles configuration service exceptions.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task SaveSettingsCommand_HandlesUserSettingsServiceException()
    {
        // Arrange
        _mockConfigService.Setup(x => x.SaveAsync()).ThrowsAsync(new IOException("Disk full"));
        var viewModel = new SettingsViewModel(_mockConfigService.Object, _mockLogger.Object);

        // Act
        await Task.Run(() => viewModel.SaveSettingsCommand.Execute(null));

        // Assert
        _mockLogger.Verify(
            x => x.Log(
            LogLevel.Error,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v != null && v.ToString() !.Contains("Failed to save settings")),
            It.IsAny<IOException>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that the constructor handles configuration service exceptions and uses defaults.
    /// </summary>
    [Fact]
    public void Constructor_HandlesUserSettingsServiceException()
    {
        // Arrange
        _mockConfigService.Setup(x => x.GetSettings()).Throws(new Exception("Configuration error"));

        // Act
        var viewModel = new SettingsViewModel(_mockConfigService.Object, _mockLogger.Object);

        // Assert - Should not throw and use defaults
        Assert.Equal("Dark", viewModel.Theme);
        Assert.Equal(3, viewModel.MaxConcurrentDownloads);
    }
}
