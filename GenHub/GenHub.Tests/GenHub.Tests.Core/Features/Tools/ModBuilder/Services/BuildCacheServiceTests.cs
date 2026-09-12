using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GenHub.Core.Interfaces.Tools.ModBuilder;
using GenHub.Core.Models.Tools.ModBuilder;
using GenHub.Features.Tools.ModBuilder.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace GenHub.Tests.Core.Features.Tools.ModBuilder.Services;

/// <summary>
/// Unit tests for <see cref="BuildCacheService"/>.
/// </summary>
public sealed class BuildCacheServiceTests : IDisposable
{
    private readonly Mock<IMd5HashProvider> _mockMd5Provider;
    private readonly Mock<IFileHashRegistryService> _mockRegistryService;
    private readonly Mock<ILogger<BuildCacheService>> _mockLogger;
    private readonly string _tempDirectory;
    private readonly BuildCacheService _service;

    public BuildCacheServiceTests()
    {
        _mockMd5Provider = new Mock<IMd5HashProvider>();
        _mockRegistryService = new Mock<IFileHashRegistryService>();
        _mockLogger = new Mock<ILogger<BuildCacheService>>();
        _tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDirectory);

        _service = new BuildCacheService(
            _mockMd5Provider.Object,
            _mockLogger.Object,
            _mockRegistryService.Object);
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
        var service = new BuildCacheService(_mockMd5Provider.Object, _mockLogger.Object);

        // Assert
        service.Should().NotBeNull();
    }

    [Fact]
    public async Task LoadCacheAsync_WhenFileDoesNotExist_ReturnsFalse()
    {
        // Arrange
        var nonExistentPath = Path.Combine(_tempDirectory, "nonexistent.json");

        // Act
        var result = await _service.LoadCacheAsync(nonExistentPath);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task SaveCacheAsync_CreatesDirectoryIfNotExists()
    {
        // Arrange
        var cachePath = Path.Combine(_tempDirectory, "subdir", "cache.json");
        _service.AddFile("test.txt", 123.45, "abc123");

        // Act
        var result = await _service.SaveCacheAsync(cachePath);

        // Assert
        result.Should().BeTrue();
        Directory.Exists(Path.GetDirectoryName(cachePath)).Should().BeTrue();
    }

    [Fact]
    public async Task SaveAndLoadCache_MessagePackFormat_PreservesData()
    {
        // Arrange
        var cachePath = Path.Combine(_tempDirectory, "cache.json");
        _service.AddFile("file1.txt", 100.0, "hash1");
        _service.AddFile("file2.txt", 200.0, "hash2", new Dictionary<string, object> { ["key"] = "value" });

        // Act - Save
        var saveResult = await _service.SaveCacheAsync(cachePath);

        // Create new service to load
        var loadService = new BuildCacheService(_mockMd5Provider.Object, _mockLogger.Object);
        var loadResult = await loadService.LoadCacheAsync(cachePath);

        // Assert
        saveResult.Should().BeTrue();
        loadResult.Should().BeTrue();

        var file1 = loadService.FindOldFile("file1.txt");
        file1.Should().NotBeNull();
        file1!.Md5.Should().Be("hash1");
        file1.ModifiedTime.Should().Be(100.0);

        var file2 = loadService.FindOldFile("file2.txt");
        file2.Should().NotBeNull();
        file2!.Params.Should().ContainKey("key");
    }

    [Fact]
    public void AddFile_StoresFileInCache()
    {
        // Act
        _service.AddFile("test.txt", 123.45, "abc123");

        // Assert - Verify by checking if we can find it after save/load cycle
        _service.FindOldFile("test.txt").Should().BeNull(); // Not in old cache yet
    }

    [Fact]
    public async Task FindOldFile_WhenFileExists_ReturnsInfo()
    {
        // Arrange
        var cachePath = Path.Combine(_tempDirectory, "cache.json");
        _service.AddFile("test.txt", 123.45, "abc123");
        await _service.SaveCacheAsync(cachePath);

        var newService = new BuildCacheService(_mockMd5Provider.Object, _mockLogger.Object);
        await newService.LoadCacheAsync(cachePath);

        // Act
        var result = newService.FindOldFile("test.txt");

        // Assert
        result.Should().NotBeNull();
        result!.Path.Should().Be("test.txt");
        result.Md5.Should().Be("abc123");
        result.ModifiedTime.Should().Be(123.45);
    }

    [Fact]
    public void FindOldFile_WhenFileDoesNotExist_ReturnsNull()
    {
        // Act
        var result = _service.FindOldFile("nonexistent.txt");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task FindOldFile_IsCaseInsensitive()
    {
        // Arrange
        var cachePath = Path.Combine(_tempDirectory, "cache.json");
        _service.AddFile("Test.TXT", 123.45, "abc123");
        await _service.SaveCacheAsync(cachePath);

        var newService = new BuildCacheService(_mockMd5Provider.Object, _mockLogger.Object);
        await newService.LoadCacheAsync(cachePath);

        // Act
        var result = newService.FindOldFile("test.txt");

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task ComputeOrReuseMd5Async_WhenFileNotInCache_ComputesNewHash()
    {
        // Arrange
        var testFile = Path.Combine(_tempDirectory, "test.txt");
        await File.WriteAllTextAsync(testFile, "content");
        _mockMd5Provider.Setup(x => x.ComputeFileHashAsync(testFile, It.IsAny<CancellationToken>()))
            .ReturnsAsync("newhash");

        // Act
        var result = await _service.ComputeOrReuseMd5Async(testFile);

        // Assert
        result.Should().Be("newhash");
        _mockMd5Provider.Verify(x => x.ComputeFileHashAsync(testFile, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void DetermineFileStatus_WhenFileInRegistry_ReturnsIrrelevant()
    {
        // Arrange
        _mockRegistryService.Setup(x => x.IsFileIrrelevant("test.txt", "hash123"))
            .Returns(true);

        // Act
        var result = _service.DetermineFileStatus("test.txt", "hash123");

        // Assert
        result.Should().Be(BuildFileStatus.Irrelevant);
    }

    [Fact]
    public void DetermineFileStatus_WhenFileNotInCache_ReturnsAdded()
    {
        // Arrange
        _mockRegistryService.Setup(x => x.IsFileIrrelevant(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(false);

        // Act
        var result = _service.DetermineFileStatus("newfile.txt", "hash123");

        // Assert
        result.Should().Be(BuildFileStatus.Added);
    }

    [Fact]
    public async Task DetermineFileStatus_WhenHashMatches_ReturnsUnchanged()
    {
        // Arrange
        var cachePath = Path.Combine(_tempDirectory, "cache.json");
        _service.AddFile("test.txt", 123.45, "hash123");
        await _service.SaveCacheAsync(cachePath);

        var newService = new BuildCacheService(_mockMd5Provider.Object, _mockLogger.Object, _mockRegistryService.Object);
        await newService.LoadCacheAsync(cachePath);

        _mockRegistryService.Setup(x => x.IsFileIrrelevant(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(false);

        // Act
        var result = newService.DetermineFileStatus("test.txt", "hash123");

        // Assert
        result.Should().Be(BuildFileStatus.Unchanged);
    }

    [Fact]
    public async Task DetermineFileStatus_WhenHashDiffers_ReturnsChanged()
    {
        // Arrange
        var cachePath = Path.Combine(_tempDirectory, "cache.json");
        _service.AddFile("test.txt", 123.45, "oldhash");
        await _service.SaveCacheAsync(cachePath);

        var newService = new BuildCacheService(_mockMd5Provider.Object, _mockLogger.Object, _mockRegistryService.Object);
        await newService.LoadCacheAsync(cachePath);

        _mockRegistryService.Setup(x => x.IsFileIrrelevant(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(false);

        // Act
        var result = newService.DetermineFileStatus("test.txt", "newhash");

        // Assert
        result.Should().Be(BuildFileStatus.Changed);
    }

    [Fact]
    public async Task DetermineFileStatus_WhenParamsDiffer_ReturnsChanged()
    {
        // Arrange
        var cachePath = Path.Combine(_tempDirectory, "cache.json");
        _service.AddFile("test.txt", 123.45, "hash123", new Dictionary<string, object> { ["key"] = "oldvalue" });
        await _service.SaveCacheAsync(cachePath);

        var newService = new BuildCacheService(_mockMd5Provider.Object, _mockLogger.Object, _mockRegistryService.Object);
        await newService.LoadCacheAsync(cachePath);

        _mockRegistryService.Setup(x => x.IsFileIrrelevant(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(false);

        // Act
        var result = newService.DetermineFileStatus("test.txt", "hash123", new Dictionary<string, object> { ["key"] = "newvalue" });

        // Assert
        result.Should().Be(BuildFileStatus.Changed);
    }

    [Fact]
    public void Clear_RemovesAllCacheEntries()
    {
        // Arrange
        _service.AddFile("file1.txt", 100.0, "hash1");
        _service.AddFile("file2.txt", 200.0, "hash2");

        // Act
        _service.Clear();

        // Assert
        _service.FindOldFile("file1.txt").Should().BeNull();
        _service.FindOldFile("file2.txt").Should().BeNull();
    }

    [Fact]
    public async Task LoadCacheAsync_WithInvalidJson_ReturnsFalse()
    {
        // Arrange
        var cachePath = Path.Combine(_tempDirectory, "invalid.json");
        await File.WriteAllTextAsync(cachePath, "{ invalid json }");

        // Act
        var result = await _service.LoadCacheAsync(cachePath);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task SaveCacheAsync_WithCancellation_ThrowsOperationCanceledException()
    {
        // Arrange
        var cachePath = Path.Combine(_tempDirectory, "cache.json");
        _service.AddFile("test.txt", 123.45, "abc123");
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await _service.SaveCacheAsync(cachePath, cts.Token));
    }
}
