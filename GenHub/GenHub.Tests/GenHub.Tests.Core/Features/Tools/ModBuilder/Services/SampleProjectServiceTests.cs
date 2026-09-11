using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Tools.ModBuilder;
using GenHub.Features.Content.Services.CommunityOutpost;
using GenHub.Features.Tools.ModBuilder.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.ModBuilder.Services;

/// <summary>
/// Unit tests for <see cref="SampleProjectService"/>.
/// </summary>
public sealed class SampleProjectServiceTests : IDisposable
{
    private readonly Mock<IDownloadService> _mockDownloadService;
    private readonly Mock<IStringTableConversionService> _mockStringTableService;
    private readonly Mock<ILogger<SampleProjectService>> _mockLogger;
    private readonly Mock<ILogger<CompressedImageToTgaConverter>> _mockConverterLogger;
    private readonly SampleProjectService _service;
    private readonly string _tempDirectory;

    public SampleProjectServiceTests()
    {
        _mockDownloadService = new Mock<IDownloadService>();
        _mockStringTableService = new Mock<IStringTableConversionService>();
        _mockLogger = new Mock<ILogger<SampleProjectService>>();
        _mockConverterLogger = new Mock<ILogger<CompressedImageToTgaConverter>>();

        var imageConverter = new CompressedImageToTgaConverter(_mockConverterLogger.Object);

        _service = new SampleProjectService(
            _mockDownloadService.Object,
            imageConverter,
            _mockLogger.Object,
            _mockStringTableService.Object);

        _tempDirectory = Path.Combine(Path.GetTempPath(), "GenHub_SampleServiceTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            try
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
            catch
            {
                // Ignore cleanup failures
            }
        }
    }

    [Theory]
    [InlineData("GeneralsGamePatch2", true)]
    [InlineData("ImprovedMenus", true)]
    [InlineData("Hotkeys", true)]
    [InlineData("CustomIcons", true)]
    [InlineData(@"C:\Projects\GeneralsGamePatch2\GeneralsGamePatch2.mbproj", true)]
    [InlineData("/home/user/Samples/ImprovedMenus/ImprovedMenus.mbproj", true)]
    [InlineData(@"C:\Projects\Hotkeys\Hotkeys.mbproj", true)]
    [InlineData("BasicMod", false)]
    [InlineData("BalancePatch", false)]
    [InlineData("TextureOverhaul", false)]
    [InlineData("UnknownCustomMod", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    public void IsSampleProject_ValidatesKnownSampleNamesAndPaths(string projectPath, bool expected)
    {
        // Act
        var result = _service.IsSampleProject(projectPath);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void HasSampleAssets_WhenDirectoryDoesNotExist_ReturnsFalse()
    {
        // Arrange
        var nonExistentDir = Path.Combine(_tempDirectory, "DoesNotExist");

        // Act
        var result = _service.HasSampleAssets(nonExistentDir);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void HasSampleAssets_WhenGameFilesEditedIsEmpty_ReturnsFalse()
    {
        // Arrange
        var projectDir = Path.Combine(_tempDirectory, "EmptyProject");
        var editedDir = Path.Combine(projectDir, "GameFilesEdited");
        Directory.CreateDirectory(editedDir);

        // Act
        var result = _service.HasSampleAssets(projectDir);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void HasSampleAssets_WhenOnlyReadmeExists_ReturnsFalse()
    {
        // Arrange
        var projectDir = Path.Combine(_tempDirectory, "ReadmeOnlyProject");
        var editedDir = Path.Combine(projectDir, "GameFilesEdited");
        Directory.CreateDirectory(editedDir);
        File.WriteAllText(Path.Combine(editedDir, "README.md"), "# Sample placeholder");

        // Act
        var result = _service.HasSampleAssets(projectDir);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void HasSampleAssets_WhenAuthenticFilesExist_ReturnsTrue()
    {
        // Arrange
        var projectDir = Path.Combine(_tempDirectory, "PopulatedProject");
        var editedDir = Path.Combine(projectDir, "GameFilesEdited", "Data", "INI");
        Directory.CreateDirectory(editedDir);
        File.WriteAllText(Path.Combine(editedDir, "GameData.ini"), "[GameData]\nWindowed = Yes");

        // Act
        var result = _service.HasSampleAssets(projectDir);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task EnsureSampleAssetsAsync_WithUnknownProject_ReturnsFailure()
    {
        // Arrange
        var projectDir = Path.Combine(_tempDirectory, "InvalidProject");
        Directory.CreateDirectory(projectDir);

        // Act
        var result = await _service.EnsureSampleAssetsAsync(projectDir, "InvalidProjectName");

        // Assert
        result.Success.Should().BeFalse();
        result.FirstError.Should().Contain("Unknown sample project");
    }

    [Fact]
    public async Task EnsureSampleAssetsAsync_WhenAssetsAlreadyExist_ReturnsSuccessImmediately()
    {
        // Arrange
        var projectDir = Path.Combine(_tempDirectory, "AlreadyPopulated");
        var editedDir = Path.Combine(projectDir, "GameFilesEdited", "Data", "INI");
        Directory.CreateDirectory(editedDir);
        File.WriteAllText(Path.Combine(editedDir, "GameData.ini"), "[GameData]\nWindowed = Yes");

        // Act
        var result = await _service.EnsureSampleAssetsAsync(projectDir, "GeneralsGamePatch2");

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().BeTrue();
        _mockDownloadService.Verify(
            d => d.DownloadFileAsync(It.IsAny<Uri>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<IProgress<GenHub.Core.Models.Common.DownloadProgress>?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
