using System;
using System.IO;
using GenHub.Common.Services;
using GenHub.Core.Models.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace GenHub.Tests.Core.Common.Services;

/// <summary>
/// Tests for <see cref="AppConfiguration"/>.
/// </summary>
public class AppConfigurationTests
{
    private readonly Mock<IConfiguration> _mockConfiguration;
    private readonly Mock<IConfigurationSection> _mockSection;
    private readonly Mock<ILogger<AppConfiguration>> _mockLogger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AppConfigurationTests"/> class.
    /// </summary>
    public AppConfigurationTests()
    {
        _mockConfiguration = new Mock<IConfiguration>();
        _mockSection = new Mock<IConfigurationSection>();
        _mockLogger = new Mock<ILogger<AppConfiguration>>();
    }

    /// <summary>
    /// Verifies that the constructor initializes correctly with valid dependencies.
    /// </summary>
    [Fact]
    public void Constructor_WithValidDependencies_DoesNotThrow()
    {
        var service = new AppConfiguration(_mockConfiguration.Object, _mockLogger.Object);
        Assert.NotNull(service);
    }

    /// <summary>
    /// Verifies that the constructor throws ArgumentNullException when configuration is null.
    /// </summary>
    [Fact]
    public void Constructor_WithNullConfiguration_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new AppConfiguration(null!, _mockLogger.Object));
    }

    /// <summary>
    /// Verifies that the constructor throws ArgumentNullException when logger is null.
    /// </summary>
    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new AppConfiguration(_mockConfiguration.Object, null!));
    }

    /// <summary>
    /// Verifies that GetDefaultWorkspacePath returns configured value when available.
    /// </summary>
    [Fact]
    public void GetDefaultWorkspacePath_WithConfiguredValue_ReturnsConfiguredValue()
    {
        // Arrange
        var configuredPath = "/custom/workspace/path";
        SetupConfigurationValue("GenHub:Workspace:DefaultPath", configuredPath);

        var service = CreateService();

        // Act
        var result = service.GetDefaultWorkspacePath();

        // Assert
        Assert.Equal(configuredPath, result);
    }

    /// <summary>
    /// Verifies that GetDefaultWorkspacePath returns default path when configuration is null.
    /// </summary>
    [Fact]
    public void GetDefaultWorkspacePath_WithNullConfiguration_ReturnsDefaultPath()
    {
        // Arrange
        SetupConfigurationValue("GenHub:Workspace:DefaultPath", null);

        var service = CreateService();

        // Act
        var result = service.GetDefaultWorkspacePath();

        // Assert
        var expectedPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "GenHub",
            "Workspace");
        Assert.Equal(expectedPath, result);
    }

    /// <summary>
    /// Verifies that GetDefaultWorkspacePath returns default path when configuration is empty.
    /// </summary>
    [Fact]
    public void GetDefaultWorkspacePath_WithEmptyConfiguration_ReturnsDefaultPath()
    {
        // Arrange
        SetupConfigurationValue("GenHub:Workspace:DefaultPath", string.Empty);

        var service = CreateService();

        // Act
        var result = service.GetDefaultWorkspacePath();

        // Assert
        var expectedPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "GenHub",
            "Workspace");
        Assert.Equal(expectedPath, result);
    }

    /// <summary>
    /// Verifies that GetDefaultCacheDirectory returns configured value when available.
    /// </summary>
    [Fact]
    public void GetDefaultCacheDirectory_WithConfiguredValue_ReturnsConfiguredValue()
    {
        // Arrange
        var configuredPath = "/custom/cache/path";
        SetupConfigurationValue("GenHub:Cache:DefaultPath", configuredPath);

        var service = CreateService();

        // Act
        var result = service.GetDefaultCacheDirectory();

        // Assert
        Assert.Equal(configuredPath, result);
    }

    /// <summary>
    /// Verifies that GetDefaultCacheDirectory returns default path when configuration is null.
    /// </summary>
    [Fact]
    public void GetDefaultCacheDirectory_WithNullConfiguration_ReturnsDefaultPath()
    {
        // Arrange
        SetupConfigurationValue("GenHub:Cache:DefaultPath", null);

        var service = CreateService();

        // Act
        var result = service.GetDefaultCacheDirectory();

        // Assert
        var expectedPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "GenHub",
            "Cache");
        Assert.Equal(expectedPath, result);
    }

    /// <summary>
    /// Verifies that GetDefaultDownloadTimeoutSeconds returns configured value when available.
    /// </summary>
    [Fact]
    public void GetDefaultDownloadTimeoutSeconds_WithConfiguredValue_ReturnsConfiguredValue()
    {
        // Arrange
        var configuredTimeout = 1200;
        SetupConfigurationValue("GenHub:Downloads:DefaultTimeoutSeconds", configuredTimeout.ToString());

        var service = CreateService();

        // Act
        var result = service.GetDefaultDownloadTimeoutSeconds();

        // Assert
        Assert.Equal(configuredTimeout, result);
    }

    /// <summary>
    /// Verifies that GetDefaultDownloadTimeoutSeconds returns default value when configuration is missing.
    /// </summary>
    [Fact]
    public void GetDefaultDownloadTimeoutSeconds_WithMissingConfiguration_ReturnsDefaultValue()
    {
        // Arrange
        SetupConfigurationValue("GenHub:Downloads:DefaultTimeoutSeconds", null);

        var service = CreateService();

        // Act
        var result = service.GetDefaultDownloadTimeoutSeconds();

        // Assert
        Assert.Equal(600, result);
    }

    /// <summary>
    /// Verifies that GetDefaultUserAgent returns configured value when available.
    /// </summary>
    [Fact]
    public void GetDefaultUserAgent_WithConfiguredValue_ReturnsConfiguredValue()
    {
        // Arrange
        var configuredAgent = "CustomGenHub/2.0";
        SetupConfigurationValue("GenHub:Downloads:DefaultUserAgent", configuredAgent);

        var service = CreateService();

        // Act
        var result = service.GetDefaultUserAgent();

        // Assert
        Assert.Equal(configuredAgent, result);
    }

    /// <summary>
    /// Verifies that GetDefaultUserAgent returns default value when configuration is missing.
    /// </summary>
    [Fact]
    public void GetDefaultUserAgent_WithMissingConfiguration_ReturnsDefaultValue()
    {
        // Arrange
        SetupConfigurationValue("GenHub:Downloads:DefaultUserAgent", null);

        var service = CreateService();

        // Act
        var result = service.GetDefaultUserAgent();

        // Assert
        Assert.Equal("GenHub/1.0", result);
    }

    /// <summary>
    /// Verifies that GetDefaultLogLevel returns configured value when available.
    /// </summary>
    /// <param name="configuredValue">The log level value to configure and test.</param>
    /// <param name="expectedLevel">The expected <see cref="LogLevel"/> result.</param>
    [Theory]
    [InlineData("Debug", LogLevel.Debug)]
    [InlineData("Information", LogLevel.Information)]
    [InlineData("Warning", LogLevel.Warning)]
    [InlineData("Error", LogLevel.Error)]
    public void GetDefaultLogLevel_WithConfiguredValue_ReturnsConfiguredValue(string configuredValue, LogLevel expectedLevel)
    {
        // Arrange
        SetupConfigurationValue("Logging:LogLevel:Default", configuredValue);

        var service = CreateService();

        // Act
        var result = service.GetDefaultLogLevel();

        // Assert
        Assert.Equal(expectedLevel, result);
    }

    /// <summary>
    /// Verifies that GetDefaultLogLevel returns default value when configuration is missing.
    /// </summary>
    [Fact]
    public void GetDefaultLogLevel_WithMissingConfiguration_ReturnsDefaultValue()
    {
        // Arrange
        SetupConfigurationValue("Logging:LogLevel:Default", null);

        var service = CreateService();

        // Act
        var result = service.GetDefaultLogLevel();

        // Assert
        Assert.Equal(LogLevel.Information, result);
    }

    /// <summary>
    /// Verifies that GetDefaultMaxConcurrentDownloads returns configured value when available.
    /// </summary>
    /// <param name="configuredMax">The maximum number of concurrent downloads to configure and test.</param>
    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(20)]
    public void GetDefaultMaxConcurrentDownloads_WithConfiguredValue_ReturnsConfiguredValue(int configuredMax)
    {
        // Arrange
        SetupConfigurationValue("GenHub:Downloads:DefaultMaxConcurrent", configuredMax.ToString());

        var service = CreateService();

        // Act
        var result = service.GetDefaultMaxConcurrentDownloads();

        // Assert
        Assert.Equal(configuredMax, result);
    }

    /// <summary>
    /// Verifies that GetDefaultMaxConcurrentDownloads returns default value when configuration is missing.
    /// </summary>
    [Fact]
    public void GetDefaultMaxConcurrentDownloads_WithMissingConfiguration_ReturnsDefaultValue()
    {
        // Arrange
        SetupConfigurationValue("GenHub:Downloads:DefaultMaxConcurrent", null);

        var service = CreateService();

        // Act
        var result = service.GetDefaultMaxConcurrentDownloads();

        // Assert
        Assert.Equal(3, result);
    }

    /// <summary>
    /// Verifies that GetDefaultDownloadBufferSize returns configured value when available.
    /// </summary>
    /// <param name="configuredBuffer">The buffer size to configure and test.</param>
    [Theory]
    [InlineData(4096)]
    [InlineData(8192)]
    [InlineData(81920)]
    [InlineData(1048576)]
    public void GetDefaultDownloadBufferSize_WithConfiguredValue_ReturnsConfiguredValue(int configuredBuffer)
    {
        // Arrange
        SetupConfigurationValue("GenHub:Downloads:DefaultBufferSize", configuredBuffer.ToString());

        var service = CreateService();

        // Act
        var result = service.GetDefaultDownloadBufferSize();

        // Assert
        Assert.Equal(configuredBuffer, result);
    }

    /// <summary>
    /// Verifies that GetDefaultDownloadBufferSize returns default value when configuration is missing.
    /// </summary>
    [Fact]
    public void GetDefaultDownloadBufferSize_WithMissingConfiguration_ReturnsDefaultValue()
    {
        // Arrange
        SetupConfigurationValue("GenHub:Downloads:DefaultBufferSize", null);

        var service = CreateService();

        // Act
        var result = service.GetDefaultDownloadBufferSize();

        // Assert
        Assert.Equal(81920, result);
    }

    /// <summary>
    /// Verifies that GetDefaultWorkspaceStrategy returns configured value when available.
    /// </summary>
    /// <param name="configuredStrategy">The workspace strategy to configure and test.</param>
    [Theory]
    [InlineData(WorkspaceStrategy.HybridCopySymlink)]
    [InlineData(WorkspaceStrategy.FullCopy)]
    [InlineData(WorkspaceStrategy.HardLink)]
    [InlineData(WorkspaceStrategy.SymlinkOnly)]
    public void GetDefaultWorkspaceStrategy_WithConfiguredValue_ReturnsConfiguredValue(WorkspaceStrategy configuredStrategy)
    {
        // Arrange
        SetupConfigurationValue("GenHub:Workspace:DefaultStrategy", configuredStrategy.ToString());

        var service = CreateService();

        // Act
        var result = service.GetDefaultWorkspaceStrategy();

        // Assert
        Assert.Equal(configuredStrategy, result);
    }

    /// <summary>
    /// Verifies that GetDefaultWorkspaceStrategy returns default value when configuration is missing.
    /// </summary>
    [Fact]
    public void GetDefaultWorkspaceStrategy_WithMissingConfiguration_ReturnsDefaultValue()
    {
        // Arrange
        SetupConfigurationValue("GenHub:Workspace:DefaultStrategy", null);

        var service = CreateService();

        // Act
        var result = service.GetDefaultWorkspaceStrategy();

        // Assert
        Assert.Equal(WorkspaceStrategy.HybridCopySymlink, result);
    }

    /// <summary>
    /// Verifies that all methods work correctly when called multiple times.
    /// </summary>
    [Fact]
    public void AllMethods_CalledMultipleTimes_WorkCorrectly()
    {
        // Arrange
        SetupConfigurationValue("GenHub:Workspace:DefaultPath", "/workspace");
        SetupConfigurationValue("GenHub:Cache:DefaultPath", "/cache");
        SetupConfigurationValue("GenHub:Downloads:DefaultTimeoutSeconds", "900");
        SetupConfigurationValue("GenHub:Downloads:DefaultUserAgent", "TestAgent/1.0");

        var service = CreateService();

        // Act & Assert - Call each method multiple times
        for (int i = 0; i < 3; i++)
        {
            Assert.Equal("/workspace", service.GetDefaultWorkspacePath());
            Assert.Equal("/cache", service.GetDefaultCacheDirectory());
            Assert.Equal(900, service.GetDefaultDownloadTimeoutSeconds());
            Assert.Equal("TestAgent/1.0", service.GetDefaultUserAgent());
        }
    }

    /// <summary>
    /// Verifies that paths are correctly constructed on different operating systems.
    /// </summary>
    [Fact]
    public void GetDefaultPaths_WithNullConfiguration_ReturnsCorrectPathsForCurrentOS()
    {
        // Arrange
        SetupConfigurationValue("GenHub:Workspace:DefaultPath", null);
        SetupConfigurationValue("GenHub:Cache:DefaultPath", null);

        var service = CreateService();

        // Act
        var workspacePath = service.GetDefaultWorkspacePath();
        var cachePath = service.GetDefaultCacheDirectory();

        // Assert
        Assert.Contains("GenHub", workspacePath);
        Assert.Contains("Workspace", workspacePath);
        Assert.Contains("GenHub", cachePath);
        Assert.Contains("Cache", cachePath);

        // Verify paths are valid for the current OS
        Assert.True(Path.IsPathRooted(workspacePath));
        Assert.True(Path.IsPathRooted(cachePath));
    }

    /// <summary>
    /// Creates an AppConfiguration instance for testing.
    /// </summary>
    /// <returns>A new AppConfiguration instance.</returns>
    private AppConfiguration CreateService()
    {
        return new AppConfiguration(_mockConfiguration.Object, _mockLogger.Object);
    }

    /// <summary>
    /// Sets up the mock configuration to return a specific value for a given key.
    /// </summary>
    /// <param name="key">The configuration key.</param>
    /// <param name="value">The value to return.</param>
    private void SetupConfigurationValue(string key, string? value)
    {
        var mockSection = new Mock<IConfigurationSection>();
        mockSection.Setup(x => x.Value).Returns(value);
        mockSection.Setup(x => x.Path).Returns(key);
        mockSection.Setup(x => x.Key).Returns(key.Split(':').Last());

        _mockConfiguration.Setup(x => x.GetSection(key)).Returns(mockSection.Object);
    }
}
