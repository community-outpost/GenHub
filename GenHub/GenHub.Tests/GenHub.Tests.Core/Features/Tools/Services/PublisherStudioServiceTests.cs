using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Publishers;
using System.Collections.ObjectModel;
using GenHub.Core.Models.Results;
using GenHub.Features.Tools.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit.Abstractions;
using ContentType = GenHub.Core.Models.Enums.ContentType;
using GameType = GenHub.Core.Models.Enums.GameType;

namespace GenHub.Tests.Core.Features.Tools.Services;

/// <summary>
/// Unit tests for <see cref="PublisherStudioService"/>.
/// </summary>
public class PublisherStudioServiceTests
{
    private readonly Mock<ILogger<PublisherStudioService>> _loggerMock;
    private readonly Mock<IPublisherCatalogParser> _catalogParserMock;
    private readonly PublisherStudioService _service;
    private readonly ITestOutputHelper _output;

    /// <summary>
    /// Initializes a new instance of the <see cref="PublisherStudioServiceTests"/> class.
    /// </summary>
    /// <param name="output">The test output helper.</param>
    public PublisherStudioServiceTests(ITestOutputHelper output)
    {
        _output = output;
        _loggerMock = new Mock<ILogger<PublisherStudioService>>();
        _catalogParserMock = new Mock<IPublisherCatalogParser>();
        _service = new PublisherStudioService(_loggerMock.Object, _catalogParserMock.Object);
    }

    /// <summary>
    /// Verifies that CreateProjectAsync creates a new project.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CreateProjectAsync_ValidName_ReturnsProject()
    {
        // Arrange
        var projectName = "Test Publisher";

        // Act
        var result = await _service.CreateProjectAsync(projectName);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal(projectName, result.Data.ProjectName);
        Assert.True(result.Data.IsDirty);
        Assert.Equal(string.Empty, result.Data.ProjectPath);
        Assert.NotNull(result.Data.Catalog);
        Assert.Equal(1, result.Data.Catalog.SchemaVersion);
    }

    /// <summary>
    /// Verifies that CreateProjectAsync validates project name.
    /// </summary>
    /// <param name="projectName">The invalid project name to test.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task CreateProjectAsync_InvalidName_ReturnsFailure(string? projectName)
    {
        // Act
        var result = await _service.CreateProjectAsync(projectName!);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("empty", result.FirstError, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that SaveProjectAsync saves a project to disk.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task SaveProjectAsync_ValidPath_SavesSuccessfully()
    {
        // Arrange
        var projectPath = GetTempFilePath();
        var project = new PublisherStudioProject
        {
            ProjectName = "Test Project",
            ProjectPath = projectPath,
            Catalog = new PublisherCatalog
            {
                Publisher = new PublisherProfile
                {
                    Id = "test-publisher",
                    Name = "Test Publisher",
                },
            },
        };

        // Act
        var result = await _service.SaveProjectAsync(project);

        // Assert
        Assert.True(result.Success);
        Assert.True(File.Exists(projectPath));
        Assert.False(project.IsDirty);

        // Cleanup
        try
        {
            File.Delete(projectPath);
        }
        catch
        {
        }
    }

    /// <summary>
    /// Verifies that SaveProjectAsync validates project path.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task SaveProjectAsync_NullPath_ReturnsFailure()
    {
        // Arrange
        var project = new PublisherStudioProject
        {
            ProjectName = "Test Project",
            ProjectPath = null!,
            Catalog = new PublisherCatalog(),
        };

        // Act
        var result = await _service.SaveProjectAsync(project);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("path", result.FirstError, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that LoadProjectAsync loads a project from disk.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task LoadProjectAsync_ValidFile_LoadsSuccessfully()
    {
        // Arrange
        var projectPath = GetTempFilePath();
        var originalProject = new PublisherStudioProject
        {
            ProjectName = "Test Project",
            ProjectPath = projectPath,
            Catalog = new PublisherCatalog
            {
                Publisher = new PublisherProfile
                {
                    Id = "test-publisher",
                    Name = "Test Publisher",
                },
            },
        };

        // Save the project first
        await _service.SaveProjectAsync(originalProject);

        // Act
        var result = await _service.LoadProjectAsync(projectPath);

        // Assert
        Assert.True(result.Success, $"Failed to load project: {result.FirstError}");
        Assert.NotNull(result.Data);
        Assert.Equal("Test Project", result.Data.ProjectName);
        Assert.Equal(projectPath, result.Data.ProjectPath);
        Assert.False(result.Data.IsDirty);

        // Cleanup
        try
        {
            File.Delete(projectPath);
        }
        catch
        {
        }
    }

    /// <summary>
    /// Verifies that LoadProjectAsync handles non-existent files.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task LoadProjectAsync_FileNotFound_ReturnsFailure()
    {
        // Arrange
        var nonExistentPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");

        // Act
        var result = await _service.LoadProjectAsync(nonExistentPath);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("not found", result.FirstError, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that ExportCatalogAsync generates valid JSON.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ExportCatalogAsync_ValidProject_ReturnsJson()
    {
        // Arrange
        var project = new PublisherStudioProject
        {
            ProjectName = "Test Project",
            Catalog = new PublisherCatalog
            {
                Publisher = new PublisherProfile
                {
                    Id = "test-publisher",
                    Name = "Test Publisher",
                },
                Content = new List<CatalogContentItem>
                {
                    new()
                    {
                        Id = "test-mod",
                        Name = "Test Mod",
                        ContentType = ContentType.Mod,
                        TargetGame = GameType.ZeroHour,
                        Releases = new ObservableCollection<ContentRelease>
                        {
                            new()
                            {
                                Version = "1.0.0",
                                IsLatest = true,
                                Artifacts = new List<ReleaseArtifact>
                                {
                                    new()
                                    {
                                        Filename = "mod.zip",
                                        DownloadUrl = "https://example.com/mod.zip",
                                        IsPrimary = true,
                                    },
                                },
                            },
                        },
                    },
                },
            },
        };

        // Act
        var result = await _service.ExportCatalogAsync(project);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Contains("test-publisher", result.Data);
        Assert.Contains("test-mod", result.Data);
    }

    /// <summary>
    /// Verifies that ValidateCatalogAsync validates required fields.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ValidateCatalogAsync_MissingPublisherId_ReturnsFailure()
    {
        // Arrange
        var catalog = new PublisherCatalog
        {
            Publisher = new PublisherProfile
            {
                Id = string.Empty,
                Name = "Test Publisher",
            },
            Content = new List<CatalogContentItem>
            {
                new()
                {
                    Id = "test-mod",
                    Name = "Test Mod",
                    Releases = new ObservableCollection<ContentRelease>
                    {
                        new()
                        {
                            Version = "1.0.0",
                            Artifacts = new List<ReleaseArtifact>
                            {
                                new()
                                {
                                    Filename = "mod.zip",
                                    DownloadUrl = "https://example.com/mod.zip",
                                    IsPrimary = true,
                                },
                            },
                        },
                    },
                },
            },
        };

        _catalogParserMock.Setup(p => p.ParseCatalogAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<PublisherCatalog>.CreateSuccess(catalog));

        // Act
        var result = await _service.ValidateCatalogAsync(catalog);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Publisher ID", result.FirstError);
    }

    /// <summary>
    /// Verifies that ValidateCatalogAsync validates publisher ID format.
    /// </summary>
    /// <param name="publisherId">The invalid publisher ID format to test.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Theory]
    [InlineData("Invalid-UPPERCASE")]
    [InlineData("invalid_with_underscores")]
    [InlineData("invalid.with.dots")]
    [InlineData("invalid with spaces")]
    public async Task ValidateCatalogAsync_InvalidPublisherIdFormat_ReturnsFailure(string publisherId)
    {
        // Arrange
        var catalog = new PublisherCatalog
        {
            Publisher = new PublisherProfile
            {
                Id = publisherId,
                Name = "Test Publisher",
            },
            Content = new List<CatalogContentItem>(),
        };

        _catalogParserMock.Setup(p => p.ParseCatalogAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<PublisherCatalog>.CreateSuccess(catalog));

        // Act
        var result = await _service.ValidateCatalogAsync(catalog);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("lowercase", result.FirstError, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that ValidateCatalogAsync validates content has releases.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ValidateCatalogAsync_ContentWithoutReleases_ReturnsFailure()
    {
        // Arrange
        var catalog = new PublisherCatalog
        {
            Publisher = new PublisherProfile
            {
                Id = "test-publisher",
                Name = "Test Publisher",
            },
            Content = new List<CatalogContentItem>
            {
                new()
                {
                    Id = "test-mod",
                    Name = "Test Mod",
                    Releases = new ObservableCollection<ContentRelease>(), // No releases
                },
            },
        };

        _catalogParserMock.Setup(p => p.ParseCatalogAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<PublisherCatalog>.CreateSuccess(catalog));

        // Act
        var result = await _service.ValidateCatalogAsync(catalog);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("no releases", result.FirstError, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that ValidateCatalogAsync validates releases have artifacts.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ValidateCatalogAsync_ReleaseWithoutArtifacts_ReturnsFailure()
    {
        // Arrange
        var catalog = new PublisherCatalog
        {
            Publisher = new PublisherProfile
            {
                Id = "test-publisher",
                Name = "Test Publisher",
            },
            Content = new List<CatalogContentItem>
            {
                new()
                {
                    Id = "test-mod",
                    Name = "Test Mod",
                    Releases = new ObservableCollection<ContentRelease>
                    {
                        new()
                        {
                            Version = "1.0.0",
                            Artifacts = new List<ReleaseArtifact>(), // No artifacts
                        },
                    },
                },
            },
        };

        _catalogParserMock.Setup(p => p.ParseCatalogAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<PublisherCatalog>.CreateSuccess(catalog));

        // Act
        var result = await _service.ValidateCatalogAsync(catalog);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("no artifacts", result.FirstError, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that ValidateCatalogAsync passes for valid catalog.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ValidateCatalogAsync_ValidCatalog_ReturnsSuccess()
    {
        // Arrange
        var catalog = new PublisherCatalog
        {
            Publisher = new PublisherProfile
            {
                Id = "test-publisher",
                Name = "Test Publisher",
            },
            Content = new List<CatalogContentItem>
            {
                new()
                {
                    Id = "test-mod",
                    Name = "Test Mod",
                    Releases = new ObservableCollection<ContentRelease>
                    {
                        new()
                        {
                            Version = "1.0.0",
                            Artifacts = new List<ReleaseArtifact>
                            {
                                new()
                                {
                                    Filename = "mod.zip",
                                    DownloadUrl = "https://example.com/mod.zip",
                                    IsPrimary = true,
                                },
                            },
                        },
                    },
                },
            },
        };

        _catalogParserMock.Setup(p => p.ParseCatalogAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<PublisherCatalog>.CreateSuccess(catalog));

        // Act
        var result = await _service.ValidateCatalogAsync(catalog);

        // Assert
        Assert.True(result.Success);
    }

    /// <summary>
    /// Verifies that GenerateSubscriptionUrl generates correct URL format.
    /// </summary>
    [Fact]
    public void GenerateSubscriptionUrl_ValidUrl_ReturnsCorrectFormat()
    {
        // Arrange
        var catalogUrl = "https://example.com/catalog.json";

        // Act
        var result = _service.GenerateSubscriptionUrl(catalogUrl);

        // Assert
        Assert.StartsWith("genhub://subscribe?url=", result);
        Assert.Contains(Uri.EscapeDataString(catalogUrl), result);
    }

    /// <summary>
    /// Verifies that GenerateSubscriptionUrl handles empty URL.
    /// </summary>
    [Fact]
    public void GenerateSubscriptionUrl_EmptyUrl_ReturnsEmpty()
    {
        // Arrange
        var catalogUrl = string.Empty;

        // Act
        var result = _service.GenerateSubscriptionUrl(catalogUrl);

        // Assert
        Assert.Equal(string.Empty, result);
    }

    /// <summary>
    /// Verifies that GenerateSubscriptionUrl handles null URL.
    /// </summary>
    [Fact]
    public void GenerateSubscriptionUrl_NullUrl_ReturnsEmpty()
    {
        // Arrange
        string? catalogUrl = null;

        // Act
        var result = _service.GenerateSubscriptionUrl(catalogUrl!);

        // Assert
        Assert.Equal(string.Empty, result);
    }

    /// <summary>
    /// Creates a temporary test file path.
    /// </summary>
    /// <returns>A temporary file path.</returns>
    private string GetTempFilePath()
    {
        return Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
    }
}
