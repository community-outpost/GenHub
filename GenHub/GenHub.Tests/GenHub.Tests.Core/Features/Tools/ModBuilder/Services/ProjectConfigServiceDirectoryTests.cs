using FluentAssertions;
using GenHub.Core.Constants;
using GenHub.Features.Tools.ModBuilder.Services;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.ModBuilder.Services;

/// <summary>
/// Tests for <see cref="ProjectConfigService.CreateProjectFromDirectoryAsync"/>.
/// </summary>
public sealed class ProjectConfigServiceDirectoryTests : IDisposable
{
    private readonly Mock<ILogger<ProjectConfigService>> _mockLogger;
    private readonly ProjectConfigService _service;
    private readonly string _tempDirectory;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProjectConfigServiceDirectoryTests"/> class.
    /// </summary>
    public ProjectConfigServiceDirectoryTests()
    {
        _mockLogger = new Mock<ILogger<ProjectConfigService>>();
        _service = new ProjectConfigService(_mockLogger.Object);
        _tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDirectory);
    }

    /// <summary>
    /// Cleans up the temporary directory.
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    /// <summary>
    /// Loose files produce a project whose pack carries the SAGE override prefix.
    /// </summary>
    /// <returns>The async task.</returns>
    [Fact]
    public async Task CreateProjectFromDirectoryAsync_WithLooseFiles_CreatesPrefixedPackAsync()
    {
        // Arrange
        var sourceDir = Path.Combine(_tempDirectory, "source");
        WriteSourceFile(sourceDir, Path.Combine("window", "Menus", "MainMenu.wnd"), "WINDOW\r\nEND");
        WriteSourceFile(sourceDir, Path.Combine("Data", "INI", "MappedImages", "Test.ini"), "; test");
        WriteSourceFile(sourceDir, "README.md", "# Test");
        WriteSourceFile(sourceDir, ".gitignore", "*.tmp");
        var projectPath = Path.Combine(_tempDirectory, "MyMod", "MyMod.mbproj");

        // Act
        var result = await _service.CreateProjectFromDirectoryAsync(projectPath, "MyMod", sourceDir);

        // Assert
        result.Success.Should().BeTrue(result.FirstError);
        File.Exists(projectPath).Should().BeTrue();

        var projectDir = Path.GetDirectoryName(projectPath)!;
        File.Exists(Path.Combine(projectDir, ModBuilderConstants.GameFilesEditedDir, "window", "Menus", "MainMenu.wnd")).Should().BeTrue();
        File.Exists(Path.Combine(projectDir, ModBuilderConstants.GameFilesEditedDir, "Data", "INI", "MappedImages", "Test.ini")).Should().BeTrue();
        File.Exists(Path.Combine(projectDir, ModBuilderConstants.GameFilesEditedDir, "README.md")).Should().BeFalse();
        File.Exists(Path.Combine(projectDir, ModBuilderConstants.GameFilesEditedDir, ".gitignore")).Should().BeFalse();

        var packsJson = File.ReadAllText(Path.Combine(projectDir, ModBuilderConstants.ConfigDir, ModBuilderConstants.BundlePacksConfigFileName));
        packsJson.Should().Contain("0_MyMod.big");

        var itemsJson = File.ReadAllText(Path.Combine(projectDir, ModBuilderConstants.ConfigDir, ModBuilderConstants.BundleItemsConfigFileName));
        itemsJson.Should().Contain("MyMod");
        itemsJson.Should().Contain("namePrefix");
    }

    /// <summary>
    /// Repository configs are adopted instead of generated.
    /// </summary>
    /// <returns>The async task.</returns>
    [Fact]
    public async Task CreateProjectFromDirectoryAsync_WithAdoptedConfigs_PreservesThemAsync()
    {
        // Arrange
        var sourceDir = Path.Combine(_tempDirectory, "source");
        WriteSourceFile(sourceDir, Path.Combine("window", "Menus", "MainMenu.wnd"), "WINDOW\r\nEND");
        WriteSourceFile(
            sourceDir,
            Path.Combine("config", ModBuilderConstants.BundlePacksConfigFileName),
            "{\"bundlePacks\":[{\"name\":\"CustomPack\",\"items\":[\"CustomItem\"],\"outputFile\":\".Release/Custom.big\"}]}");
        WriteSourceFile(
            sourceDir,
            Path.Combine("config", ModBuilderConstants.BundleItemsConfigFileName),
            "{\"bundleItems\":[{\"name\":\"CustomItem\",\"sourceFiles\":[\"GameFilesEdited/**/*\"]}]}");
        var projectPath = Path.Combine(_tempDirectory, "MyMod", "MyMod.mbproj");

        // Act
        var result = await _service.CreateProjectFromDirectoryAsync(projectPath, "MyMod", sourceDir);

        // Assert
        result.Success.Should().BeTrue(result.FirstError);
        var projectDir = Path.GetDirectoryName(projectPath)!;
        var packsJson = File.ReadAllText(Path.Combine(projectDir, ModBuilderConstants.ConfigDir, ModBuilderConstants.BundlePacksConfigFileName));
        packsJson.Should().Contain("CustomPack");
        packsJson.Should().NotContain("0_MyMod.big");
    }

    /// <summary>
    /// A nested GameFilesEdited folder is used as the content root.
    /// </summary>
    /// <returns>The async task.</returns>
    [Fact]
    public async Task CreateProjectFromDirectoryAsync_WithNestedGameFilesEdited_UsesItAsRootAsync()
    {
        // Arrange
        var sourceDir = Path.Combine(_tempDirectory, "source");
        WriteSourceFile(sourceDir, Path.Combine(ModBuilderConstants.GameFilesEditedDir, "window", "MainMenu.wnd"), "WINDOW\r\nEND");
        WriteSourceFile(sourceDir, "unrelated.txt", "docs");
        var projectPath = Path.Combine(_tempDirectory, "MyMod", "MyMod.mbproj");

        // Act
        var result = await _service.CreateProjectFromDirectoryAsync(projectPath, "MyMod", sourceDir);

        // Assert
        result.Success.Should().BeTrue(result.FirstError);
        var projectDir = Path.GetDirectoryName(projectPath)!;
        File.Exists(Path.Combine(projectDir, ModBuilderConstants.GameFilesEditedDir, "window", "MainMenu.wnd")).Should().BeTrue();
        File.Exists(Path.Combine(projectDir, ModBuilderConstants.GameFilesEditedDir, "unrelated.txt")).Should().BeFalse();
    }

    /// <summary>
    /// A missing source directory fails without creating a project.
    /// </summary>
    /// <returns>The async task.</returns>
    [Fact]
    public async Task CreateProjectFromDirectoryAsync_WithMissingSource_ReturnsFailureAsync()
    {
        // Arrange
        var projectPath = Path.Combine(_tempDirectory, "MyMod", "MyMod.mbproj");
        var missingSource = Path.Combine(_tempDirectory, "missing");

        // Act
        var result = await _service.CreateProjectFromDirectoryAsync(projectPath, "MyMod", missingSource);

        // Assert
        result.Success.Should().BeFalse();
        File.Exists(projectPath).Should().BeFalse();
    }

    /// <summary>
    /// An empty source directory fails and rolls back the created shell.
    /// </summary>
    /// <returns>The async task.</returns>
    [Fact]
    public async Task CreateProjectFromDirectoryAsync_WithEmptySource_RollsBackAsync()
    {
        // Arrange
        var sourceDir = Path.Combine(_tempDirectory, "source");
        Directory.CreateDirectory(sourceDir);
        var projectPath = Path.Combine(_tempDirectory, "MyMod", "MyMod.mbproj");

        // Act
        var result = await _service.CreateProjectFromDirectoryAsync(projectPath, "MyMod", sourceDir);

        // Assert
        result.Success.Should().BeFalse();
        File.Exists(projectPath).Should().BeFalse();
    }

    /// <summary>
    /// Pack names drop unsafe characters and fall back when empty.
    /// </summary>
    /// <param name="projectName">The project name.</param>
    /// <param name="expected">The expected pack name.</param>
    [Theory]
    [InlineData("MyMod", "MyMod")]
    [InlineData("My Mod!", "MyMod")]
    [InlineData("mod_v2.1-beta", "mod_v2.1-beta")]
    [InlineData("!!!", "ImportedMod")]
    [InlineData("", "ImportedMod")]
    public void SanitizePackName_CleansProjectName(string projectName, string expected)
    {
        ProjectConfigService.SanitizePackName(projectName).Should().Be(expected);
    }

    private static void WriteSourceFile(string sourceDir, string relativePath, string content)
    {
        var fullPath = Path.Combine(sourceDir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
    }
}
