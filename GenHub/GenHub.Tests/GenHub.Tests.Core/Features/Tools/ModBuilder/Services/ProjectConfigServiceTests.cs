using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GenHub.Core.Models.Tools.ModBuilder;
using GenHub.Features.Tools.ModBuilder.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace GenHub.Tests.Core.Features.Tools.ModBuilder.Services;

/// <summary>
/// Unit tests for <see cref="ProjectConfigService"/>.
/// </summary>
public sealed class ProjectConfigServiceTests : IDisposable
{
    private readonly Mock<ILogger<ProjectConfigService>> _mockLogger;
    private readonly ProjectConfigService _service;
    private readonly string _tempDirectory;

    public ProjectConfigServiceTests()
    {
        _mockLogger = new Mock<ILogger<ProjectConfigService>>();
        _service = new ProjectConfigService(_mockLogger.Object);
        _tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void Constructor_WithValidDependencies_DoesNotThrow()
    {
        // Act
        var service = new ProjectConfigService(_mockLogger.Object);

        // Assert
        service.Should().NotBeNull();
    }

    [Fact]
    public async Task CreateProjectAsync_WithValidParameters_CreatesProject()
    {
        // Arrange
        var projectPath = Path.Combine(_tempDirectory, "TestProject.mbproj");
        var projectName = "TestProject";

        // Act
        var result = await _service.CreateProjectAsync(projectPath, projectName);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.Name.Should().Be(projectName);
        File.Exists(projectPath).Should().BeTrue();
    }

    [Fact]
    public async Task CreateProjectAsync_WithEmptyPath_ReturnsFailure()
    {
        // Arrange
        var projectPath = string.Empty;
        var projectName = "TestProject";

        // Act
        var result = await _service.CreateProjectAsync(projectPath, projectName);

        // Assert
        result.Success.Should().BeFalse();
        result.Errors.Should().Contain("Project path cannot be empty");
    }

    [Fact]
    public async Task CreateProjectAsync_WithEmptyName_ReturnsFailure()
    {
        // Arrange
        var projectPath = Path.Combine(_tempDirectory, "TestProject.mbproj");
        var projectName = string.Empty;

        // Act
        var result = await _service.CreateProjectAsync(projectPath, projectName);

        // Assert
        result.Success.Should().BeFalse();
        result.Errors.Should().Contain("Project name cannot be empty");
    }

    [Fact]
    public async Task CreateProjectAsync_WithExistingProject_ReturnsFailure()
    {
        // Arrange
        var projectPath = Path.Combine(_tempDirectory, "TestProject.mbproj");
        var projectName = "TestProject";
        await _service.CreateProjectAsync(projectPath, projectName);

        // Act
        var result = await _service.CreateProjectAsync(projectPath, projectName);

        // Assert
        result.Success.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("already exists"));
    }

    [Fact]
    public async Task CreateProjectAsync_WithoutExtension_AddsExtension()
    {
        // Arrange
        var projectPath = Path.Combine(_tempDirectory, "TestProject");
        var projectName = "TestProject";

        // Act
        var result = await _service.CreateProjectAsync(projectPath, projectName);

        // Assert
        result.Success.Should().BeTrue();
        File.Exists(Path.Combine(_tempDirectory, "TestProject.mbproj")).Should().BeTrue();
    }

    [Fact]
    public async Task CreateProjectAsync_WithTemplate_AppliesTemplate()
    {
        // Arrange
        var projectPath = Path.Combine(_tempDirectory, "TestProject.mbproj");
        var projectName = "TestProject";
        var template = new ProjectTemplate
        {
            Name = "Test Template",
            DefaultBundleConfigs = new List<string> { "config1.json", "config2.json" },
            CreateSampleFiles = false
        };

        // Act
        var result = await _service.CreateProjectAsync(projectPath, projectName, template: template);

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.BundleConfigs.Should().Contain("config1.json");
        result.Data.BundleConfigs.Should().Contain("config2.json");
    }

    [Fact]
    public async Task LoadProjectAsync_WithValidProject_LoadsProject()
    {
        // Arrange
        var projectPath = Path.Combine(_tempDirectory, "TestProject.mbproj");
        var projectName = "TestProject";
        await _service.CreateProjectAsync(projectPath, projectName);

        // Act
        var result = await _service.LoadProjectAsync(projectPath);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.Name.Should().Be(projectName);
    }

    [Fact]
    public async Task LoadProjectAsync_WithNonExistentFile_ReturnsFailure()
    {
        // Arrange
        var projectPath = Path.Combine(_tempDirectory, "NonExistent.mbproj");

        // Act
        var result = await _service.LoadProjectAsync(projectPath);

        // Assert
        result.Success.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("not found"));
    }

    [Fact]
    public async Task SaveProjectAsync_WithValidProject_SavesProject()
    {
        // Arrange
        var projectPath = Path.Combine(_tempDirectory, "TestProject.mbproj");
        var project = new ModBuilderProject
        {
            Name = "TestProject",
            Directories = new ProjectDirectories(),
            BundleConfigs = new List<string>(),
            CreatedAt = DateTime.UtcNow,
            LastModified = DateTime.UtcNow
        };

        // Act
        var result = await _service.SaveProjectAsync(projectPath, project);

        // Assert
        result.Success.Should().BeTrue();
        File.Exists(projectPath).Should().BeTrue();
    }

    [Fact]
    public async Task SaveProjectAsync_UpdatesLastModified()
    {
        // Arrange
        var projectPath = Path.Combine(_tempDirectory, "TestProject.mbproj");
        var project = new ModBuilderProject
        {
            Name = "TestProject",
            Directories = new ProjectDirectories(),
            BundleConfigs = new List<string>(),
            CreatedAt = DateTime.UtcNow.AddDays(-1),
            LastModified = DateTime.UtcNow.AddDays(-1)
        };
        var oldLastModified = project.LastModified;

        // Act
        await Task.Delay(10); // Ensure time difference
        var result = await _service.SaveProjectAsync(projectPath, project);

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.LastModified.Should().BeAfter(oldLastModified);
    }

    [Fact]
    public async Task ValidateProjectAsync_WithValidProject_ReturnsSuccess()
    {
        // Arrange
        var projectPath = Path.Combine(_tempDirectory, "TestProject.mbproj");
        var projectName = "TestProject";
        var createResult = await _service.CreateProjectAsync(projectPath, projectName);
        var project = createResult.Data!;

        // Act
        var result = await _service.ValidateProjectAsync(projectPath, project);

        // Assert
        result.Success.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateProjectAsync_WithNonExistentProject_ReturnsFailure()
    {
        // Arrange
        var projectPath = Path.Combine(_tempDirectory, "NonExistent.mbproj");
        var project = new ModBuilderProject { Name = "NonExistent" };

        // Act
        var result = await _service.ValidateProjectAsync(projectPath, project);

        // Assert
        result.Success.Should().BeFalse();
        result.Errors.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetRecentProjectsAsync_ReturnsRecentProjects()
    {
        // Arrange
        var projectPath1 = Path.Combine(_tempDirectory, "Project1.mbproj");
        var projectPath2 = Path.Combine(_tempDirectory, "Project2.mbproj");
        await _service.CreateProjectAsync(projectPath1, "Project1");
        await _service.CreateProjectAsync(projectPath2, "Project2");

        // Act
        var result = await _service.GetRecentProjectsAsync();

        // Assert
        result.Should().NotBeNull();
        result.Data.Should().NotBeNull();
        result.Data.Should().Contain(p => p.Contains("Project1.mbproj") || p.Contains("Project2.mbproj"));
    }

    [Fact]
    public async Task CreateProjectAsync_WithCancellation_ThrowsOperationCanceledException()
    {
        // Arrange
        var projectPath = Path.Combine(_tempDirectory, "TestProject.mbproj");
        var projectName = "TestProject";
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await _service.CreateProjectAsync(projectPath, projectName, cancellationToken: cts.Token));
    }

    [Fact]
    public async Task LoadProjectAsync_WithCorruptedFile_ReturnsFailure()
    {
        // Arrange
        var projectPath = Path.Combine(_tempDirectory, "Corrupted.mbproj");
        await File.WriteAllTextAsync(projectPath, "{ invalid json }");

        // Act
        var result = await _service.LoadProjectAsync(projectPath);

        // Assert
        result.Success.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Invalid") || e.Contains("parse"));
    }
}
