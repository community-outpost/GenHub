using GenHub.Core.Interfaces.Content;

using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Features.Manifest;
using Microsoft.Extensions.Logging;
using Moq;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Manifest;

/// <summary>
/// Tests for <see cref="ContentManifestPool"/>.
/// </summary>
public class ContentManifestPoolTests : IDisposable
{
    private readonly Mock<IContentStorageService> _storageServiceMock;
    private readonly Mock<ILogger<ContentManifestPool>> _loggerMock;
    private readonly ContentManifestPool _manifestPool;
    private readonly string _tempDirectory;

    /// <summary>
    /// Initializes a new instance of the <see cref="ContentManifestPoolTests"/> class.
    /// </summary>
    public ContentManifestPoolTests()
    {
        _storageServiceMock = new Mock<IContentStorageService>();
        _loggerMock = new Mock<ILogger<ContentManifestPool>>();
        _manifestPool = new ContentManifestPool(_storageServiceMock.Object, _loggerMock.Object);
        _tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDirectory);
    }

    /// <summary>
    /// Should add manifest successfully when content is already stored.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task AddManifestAsync_WithStoredContent_ShouldSucceed()
    {
        // Arrange
        var manifest = CreateTestManifest();
        var manifestPath = Path.Combine(_tempDirectory, $"{manifest.Id}.manifest.json");

        _storageServiceMock.Setup(x => x.IsContentStoredAsync(manifest.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));
        _storageServiceMock.Setup(x => x.GetManifestStoragePath(manifest.Id))
            .Returns(manifestPath);

        // Act
        var result = await _manifestPool.AddManifestAsync(manifest);

        // Assert
        Assert.True(result.Success, $"Expected success but got: {result.FirstError}");
        Assert.True(result.Data, "Expected result.Data to be true");
        _storageServiceMock.Verify(x => x.IsContentStoredAsync(manifest.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Should fail to add manifest when content is not stored.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task AddManifestAsync_WithoutStoredContent_ShouldFail()
    {
        // Arrange
        var manifest = CreateTestManifest();
        _storageServiceMock.Setup(x => x.IsContentStoredAsync(manifest.Id, default))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(false));

        // Act
        var result = await _manifestPool.AddManifestAsync(manifest);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Cannot add manifest", result.FirstError!);
    }

    /// <summary>
    /// Should add manifest with source directory successfully.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task AddManifestAsync_WithSourceDirectory_ShouldSucceed()
    {
        // Arrange
        var manifest = CreateTestManifest();
        var sourceDirectory = Path.Combine(_tempDirectory, "source");
        Directory.CreateDirectory(sourceDirectory);

        _storageServiceMock.Setup(x => x.StoreContentAsync(manifest, sourceDirectory, default))
            .ReturnsAsync(OperationResult<ContentManifest>.CreateSuccess(manifest));

        // Act
        var result = await _manifestPool.AddManifestAsync(manifest, sourceDirectory);

        // Assert
        Assert.True(result.Success);
        Assert.True(result.Data);
        _storageServiceMock.Verify(x => x.StoreContentAsync(manifest, sourceDirectory, default), Times.Once);
    }

    /// <summary>
    /// Should return manifest when it exists.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task GetManifestAsync_WhenExists_ShouldReturnManifest()
    {
        // Arrange
        var manifest = CreateTestManifest();
        var manifestPath = Path.Combine(_tempDirectory, "manifest.json");
        await File.WriteAllTextAsync(manifestPath, System.Text.Json.JsonSerializer.Serialize(manifest));

        _storageServiceMock.Setup(x => x.GetManifestStoragePath(manifest.Id))
            .Returns(manifestPath);

        // Act
        var result = await _manifestPool.GetManifestAsync(manifest.Id);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal(manifest.Id, result.Data.Id);
    }

    /// <summary>
    /// Should return null when manifest does not exist.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task GetManifestAsync_WhenNotExists_ShouldReturnNull()
    {
        // Arrange
        var manifestId = "1.0.test.nonexistent.mod";
        var manifestPath = Path.Combine(_tempDirectory, "non-existent.json");

        _storageServiceMock.Setup(x => x.GetManifestStoragePath(manifestId))
            .Returns(manifestPath);

        // Act
        var result = await _manifestPool.GetManifestAsync(manifestId);

        // Assert
        Assert.True(result.Success);
        Assert.Null(result.Data);
    }

    /// <summary>
    /// Should return all manifests from storage.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task GetAllManifestsAsync_ShouldReturnAllManifests()
    {
        // Arrange
        var manifests = new List<ContentManifest>
            {
                CreateTestManifest("1.0.test.manifest1.mod"),
                CreateTestManifest("1.0.test.manifest2.mod"),
                CreateTestManifest("1.0.test.manifest3.mod"),
            };

        var manifestsDir = Path.Combine(_tempDirectory, "Manifests");
        Directory.CreateDirectory(manifestsDir);

        foreach (var manifest in manifests)
        {
            var manifestPath = Path.Combine(manifestsDir, $"{manifest.Id}.manifest.json");
            await File.WriteAllTextAsync(manifestPath, System.Text.Json.JsonSerializer.Serialize(manifest));
        }

        _storageServiceMock.Setup(x => x.GetContentStorageRoot())
            .Returns(_tempDirectory);

        // Act
        var result = await _manifestPool.GetAllManifestsAsync();

        // Assert
        Assert.True(result.Success);
        Assert.Equal(3, result.Data!.Count());
        Assert.Contains(result.Data!, m => m.Id == "1.0.test.manifest1.mod");
        Assert.Contains(result.Data!, m => m.Id == "1.0.test.manifest2.mod");
        Assert.Contains(result.Data!, m => m.Id == "1.0.test.manifest3.mod");
    }

    /// <summary>
    /// Should return empty list when no manifests directory exists.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task GetAllManifestsAsync_WhenNoDirectory_ShouldReturnEmptyList()
    {
        // Arrange
        _storageServiceMock.Setup(x => x.GetContentStorageRoot())
            .Returns(Path.Combine(_tempDirectory, "non-existent"));

        // Act
        var result = await _manifestPool.GetAllManifestsAsync();

        // Assert
        Assert.True(result.Success);
        Assert.Empty(result.Data!);
    }

    /// <summary>
    /// Should search manifests by query criteria.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task SearchManifestsAsync_WithQuery_ShouldReturnFilteredResults()
    {
        // Arrange
        var manifests = new List<ContentManifest>
            {
                CreateTestManifest("1.0.test.mod1.mod", "Test Mod 1", ContentType.Mod, GameType.Generals),
                CreateTestManifest("1.0.test.map1.map", "Test Map 1", ContentType.MapPack, GameType.Generals),
                CreateTestManifest("1.0.test.mod2.mod", "Another Mod", ContentType.Mod, GameType.ZeroHour),
            };

        SetupManifestsInStorage(manifests);

        var query = new ContentSearchQuery
        {
            SearchTerm = "Mod",
            ContentType = ContentType.Mod,
        };

        // Act
        var result = await _manifestPool.SearchManifestsAsync(query);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, result.Data!.Count());
        Assert.All(result.Data!, m => Assert.Equal(ContentType.Mod, m.ContentType));
    }

    /// <summary>
    /// Should remove manifest successfully.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task RemoveManifestAsync_ShouldSucceed()
    {
        // Arrange
        var manifestId = "1.0.test.publisher.mod";
        _storageServiceMock.Setup(x => x.RemoveContentAsync(manifestId, default))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        // Act
        var result = await _manifestPool.RemoveManifestAsync(manifestId);

        // Assert
        Assert.True(result.Success);
        Assert.True(result.Data);
        _storageServiceMock.Verify(x => x.RemoveContentAsync(manifestId, default), Times.Once);
    }

    /// <summary>
    /// Should fail to remove manifest when storage service fails.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task RemoveManifestAsync_WhenStorageFails_ShouldFail()
    {
        // Arrange
        var manifestId = "1.0.test.publisher.mod";
        _storageServiceMock.Setup(x => x.RemoveContentAsync(manifestId, default))
            .ReturnsAsync(OperationResult<bool>.CreateFailure("Storage error"));

        // Act
        var result = await _manifestPool.RemoveManifestAsync(manifestId);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Failed to remove content", result.FirstError!);
    }

    /// <summary>
    /// Should check if manifest is acquired correctly.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task IsManifestAcquiredAsync_ShouldReturnCorrectStatus()
    {
        // Arrange
        var manifestId = "1.0.test.publisher.mod";
        _storageServiceMock.Setup(x => x.IsContentStoredAsync(manifestId, default))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        // Act
        var result = await _manifestPool.IsManifestAcquiredAsync(manifestId);

        // Assert
        Assert.True(result.Success);
        Assert.True(result.Data);
    }

    /// <summary>
    /// Should return content directory when it exists.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task GetContentDirectoryAsync_WhenExists_ShouldReturnPath()
    {
        // Arrange
        var manifestId = "1.0.test.publisher.mod";
        var contentDir = Path.Combine(_tempDirectory, "content");
        Directory.CreateDirectory(contentDir);

        _storageServiceMock.Setup(x => x.GetContentDirectoryPath(manifestId))
            .Returns(contentDir);

        // Act
        var result = await _manifestPool.GetContentDirectoryAsync(manifestId);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(contentDir, result.Data);
    }

    /// <summary>
    /// Should return null when content directory does not exist.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task GetContentDirectoryAsync_WhenNotExists_ShouldReturnNull()
    {
        // Arrange
        var manifestId = "1.0.test.publisher.mod";
        var contentDir = Path.Combine(_tempDirectory, "non-existent");

        _storageServiceMock.Setup(x => x.GetContentDirectoryPath(manifestId))
            .Returns(contentDir);

        // Act
        var result = await _manifestPool.GetContentDirectoryAsync(manifestId);

        // Assert
        Assert.True(result.Success);
        Assert.Null(result.Data);
    }

    /// <summary>
    /// Should handle exceptions gracefully.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task GetManifestAsync_WhenExceptionThrown_ShouldReturnFailure()
    {
        // Arrange
        var manifestId = "1.0.test.publisher.mod";
        _storageServiceMock.Setup(x => x.GetManifestStoragePath(manifestId))
            .Throws(new InvalidOperationException("Test exception"));

        // Act
        var result = await _manifestPool.GetManifestAsync(manifestId);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Failed to read manifest", result.FirstError!);
    }

    /// <summary>
    /// Performs cleanup by disposing of temporary resources.
    /// </summary>
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    /// <summary>
    /// Creates a test content manifest.
    /// </summary>
    /// <param name="id">The manifest ID.</param>
    /// <param name="name">The manifest name.</param>
    /// <param name="contentType">The content type.</param>
    /// <param name="targetGame">The target game.</param>
    /// <returns>A <see cref="ContentManifest"/> instance.</returns>
    private ContentManifest CreateTestManifest(
        string id = "1.0.test.publisher.mod",
        string name = "Test Manifest",
        ContentType contentType = ContentType.Mod,
        GameType targetGame = GameType.Generals)
    {
        return new ContentManifest
        {
            Id = id,
            Name = name,
            ContentType = contentType,
            TargetGame = targetGame,
            Version = "1.0.0",
            Metadata = new ContentMetadata
            {
                Description = "Test manifest for unit tests",
            },
            Files = new List<ManifestFile>
                {
                    new() { RelativePath = "test.txt", Size = 100, SourceType = ContentSourceType.LocalFile, },
                },
        };
    }

    /// <summary>
    /// Sets up manifests in storage for testing.
    /// </summary>
    /// <param name="manifests">The list of manifests to set up.</param>
    private void SetupManifestsInStorage(List<ContentManifest> manifests)
    {
        var manifestsDir = Path.Combine(_tempDirectory, "Manifests");
        Directory.CreateDirectory(manifestsDir);

        foreach (var manifest in manifests)
        {
            var manifestPath = Path.Combine(manifestsDir, $"{manifest.Id}.manifest.json");
            File.WriteAllText(manifestPath, System.Text.Json.JsonSerializer.Serialize(manifest));
        }

        _storageServiceMock.Setup(x => x.GetContentStorageRoot())
            .Returns(_tempDirectory);
    }
}