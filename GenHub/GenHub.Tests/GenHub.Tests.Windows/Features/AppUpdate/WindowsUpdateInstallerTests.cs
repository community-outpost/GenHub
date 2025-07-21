using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GenHub.Core.Interfaces.AppUpdate;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.AppUpdate;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.GitHub;
using GenHub.Core.Models.Results;
using GenHub.Windows.Features.AppUpdate;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace GenHub.Tests.Windows.Features.AppUpdate;

/// <summary>
/// Tests for <see cref="WindowsUpdateInstaller"/>.
/// </summary>
public class WindowsUpdateInstallerTests : IDisposable
{
    private readonly Mock<ILogger<WindowsUpdateInstaller>> _mockLogger;
    private readonly Mock<IDownloadService> _mockDownloadService;
    private readonly string _tempDirectory;

    /// <summary>
    /// Initializes a new instance of the <see cref="WindowsUpdateInstallerTests"/> class.
    /// </summary>
    public WindowsUpdateInstallerTests()
    {
        _mockLogger = new Mock<ILogger<WindowsUpdateInstaller>>();
        _mockDownloadService = new Mock<IDownloadService>();
        _tempDirectory = Path.Combine(Path.GetTempPath(), "WindowsUpdateInstallerTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDirectory);
    }

    /// <summary>
    /// Tests that constructor with valid parameters should not throw.
    /// </summary>
    [Fact]
    public void Constructor_WithValidParameters_ShouldNotThrow()
    {
        // Act & Assert
        var installer = new WindowsUpdateInstaller(_mockDownloadService.Object, _mockLogger.Object);
        installer.Should().NotBeNull();
    }

    /// <summary>
    /// Tests that constructor with null download service should throw ArgumentNullException.
    /// </summary>
    [Fact]
    public void Constructor_WithNullDownloadService_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        var act = () => new WindowsUpdateInstaller(null!, _mockLogger.Object);
        act.Should().Throw<ArgumentNullException>().WithParameterName("downloadService");
    }

    /// <summary>
    /// Tests that constructor with null logger should throw ArgumentNullException.
    /// </summary>
    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        var act = () => new WindowsUpdateInstaller(_mockDownloadService.Object, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    /// <summary>
    /// Tests that GetPlatformDownloadUrl returns correct URL for Windows assets.
    /// </summary>
    [Fact]
    public void GetPlatformDownloadUrl_WithWindowsAssets_ShouldReturnCorrectUrl()
    {
        // Arrange
        var installer = new WindowsUpdateInstaller(_mockDownloadService.Object, _mockLogger.Object);
        var assets = new List<GitHubReleaseAsset>
        {
            new () { Name = "app-windows.exe", BrowserDownloadUrl = "https://example.com/windows" },
            new () { Name = "app-linux.tar.gz", BrowserDownloadUrl = "https://example.com/linux" },
            new () { Name = "app-mac.dmg", BrowserDownloadUrl = "https://example.com/mac" },
        };

        // Act
        var result = installer.GetPlatformDownloadUrl(assets);

        // Assert
        result.Should().Be("https://example.com/windows");
    }

    /// <summary>
    /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            try
            {
                Directory.Delete(_tempDirectory, true);
            }
            catch
            {
                // Ignore cleanup errors in tests
            }
        }

        GC.SuppressFinalize(this);
    }
}
