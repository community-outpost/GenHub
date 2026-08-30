using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using GenHub.Features.Tools.ModBuilder.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace GenHub.Tests.Core.Features.Tools.ModBuilder.Services;

/// <summary>
/// Unit tests for <see cref="FileHashRegistryService"/>.
/// </summary>
public sealed class FileHashRegistryServiceTests
{
    private readonly Mock<ILogger<FileHashRegistryService>> _mockLogger;
    private readonly FileHashRegistryService _service;
    private readonly string _tempDirectory;

    public FileHashRegistryServiceTests()
    {
        _mockLogger = new Mock<ILogger<FileHashRegistryService>>();
        _service = new FileHashRegistryService(_mockLogger.Object);
        _tempDirectory = Path.Combine(Path.GetTempPath(), "FileHashRegistryTests");
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public void Constructor_WithValidDependencies_DoesNotThrow()
    {
        // Act
        var service = new FileHashRegistryService(_mockLogger.Object);

        // Assert
        service.Should().NotBeNull();
    }

    [Fact]
    public async Task LoadRegistryAsync_WithValidCsvFile_LoadsSuccessfullyAsync()
    {
        // Arrange
        var csvPath = Path.Combine(_tempDirectory, "registry.csv");
        await File.WriteAllTextAsync(csvPath, "file1.txt,hash1\nfile2.txt,hash2\n");

        // Act
        await _service.LoadRegistryAsync(csvPath);

        // Assert
        _service.IsFileIrrelevant("file1.txt", "hash1").Should().BeTrue();
        _service.IsFileIrrelevant("file2.txt", "hash2").Should().BeTrue();
    }

    [Fact]
    public async Task LoadRegistryAsync_WithNonExistentFile_DoesNotThrowAsync()
    {
        // Arrange
        var csvPath = Path.Combine(_tempDirectory, "nonexistent.csv");

        // Act
        var act = async () => await _service.LoadRegistryAsync(csvPath);

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task LoadRegistryAsync_WithEmptyFile_LoadsSuccessfullyAsync()
    {
        // Arrange
        var csvPath = Path.Combine(_tempDirectory, "empty.csv");
        await File.WriteAllTextAsync(csvPath, string.Empty);

        // Act
        await _service.LoadRegistryAsync(csvPath);

        // Assert
        _service.IsFileIrrelevant("anyfile.txt", "anyhash").Should().BeFalse();
    }

    [Fact]
    public async Task IsFileIrrelevant_WhenFileAndHashMatch_ReturnsTrueAsync()
    {
        // Arrange
        var csvPath = Path.Combine(_tempDirectory, "test.csv");
        await File.WriteAllTextAsync(csvPath, "test.txt,hash123\n");
        await _service.LoadRegistryAsync(csvPath);

        // Act
        var result = _service.IsFileIrrelevant("test.txt", "hash123");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsFileIrrelevant_WhenFileNotInRegistry_ReturnsFalseAsync()
    {
        // Arrange
        var csvPath = Path.Combine(_tempDirectory, "test2.csv");
        await File.WriteAllTextAsync(csvPath, "test.txt,hash123\n");
        await _service.LoadRegistryAsync(csvPath);

        // Act
        var result = _service.IsFileIrrelevant("other.txt", "hash123");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsFileIrrelevant_WhenHashNotInRegistry_ReturnsFalseAsync()
    {
        // Arrange
        var csvPath = Path.Combine(_tempDirectory, "test3.csv");
        await File.WriteAllTextAsync(csvPath, "test.txt,hash123\n");
        await _service.LoadRegistryAsync(csvPath);

        // Act
        var result = _service.IsFileIrrelevant("test.txt", "differenthash");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsFileIrrelevant_IsCaseInsensitiveAsync()
    {
        // Arrange
        var csvPath = Path.Combine(_tempDirectory, "test4.csv");
        await File.WriteAllTextAsync(csvPath, "Test.TXT,HASH123\n");
        await _service.LoadRegistryAsync(csvPath);

        // Act
        var result = _service.IsFileIrrelevant("test.txt", "hash123");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void IsFileIrrelevant_BeforeLoadRegistry_ReturnsFalse()
    {
        // Act
        var result = _service.IsFileIrrelevant("test.txt", "hash123");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task LoadRegistryAsync_CalledTwice_ReplacesOldRegistryAsync()
    {
        // Arrange
        var csvPath1 = Path.Combine(_tempDirectory, "registry1.csv");
        var csvPath2 = Path.Combine(_tempDirectory, "registry2.csv");
        await File.WriteAllTextAsync(csvPath1, "file1.txt,hash1\n");
        await File.WriteAllTextAsync(csvPath2, "file2.txt,hash2\n");

        // Act
        await _service.LoadRegistryAsync(csvPath1);
        await _service.LoadRegistryAsync(csvPath2);

        // Assert
        _service.IsFileIrrelevant("file1.txt", "hash1").Should().BeFalse();
        _service.IsFileIrrelevant("file2.txt", "hash2").Should().BeTrue();
    }

    [Fact]
    public async Task IsFileIrrelevant_WithEmptyHash_ReturnsFalseAsync()
    {
        // Arrange
        var csvPath = Path.Combine(_tempDirectory, "test5.csv");
        await File.WriteAllTextAsync(csvPath, "test.txt,hash123\n");
        await _service.LoadRegistryAsync(csvPath);

        // Act
        var result = _service.IsFileIrrelevant("test.txt", string.Empty);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsFileIrrelevant_WithEmptyFilePath_ReturnsFalseAsync()
    {
        // Arrange
        var csvPath = Path.Combine(_tempDirectory, "test6.csv");
        await File.WriteAllTextAsync(csvPath, "test.txt,hash123\n");
        await _service.LoadRegistryAsync(csvPath);

        // Act
        var result = _service.IsFileIrrelevant(string.Empty, "hash123");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsFileIrrelevant_WithNormalizedPaths_WorksCorrectlyAsync()
    {
        // Arrange
        var csvPath = Path.Combine(_tempDirectory, "test7.csv");
        await File.WriteAllTextAsync(csvPath, "file.txt,hash123\n");
        await _service.LoadRegistryAsync(csvPath);

        // Act - Service normalizes to filename only
        var result = _service.IsFileIrrelevant("path/to/file.txt", "hash123");

        // Assert
        result.Should().BeTrue();
    }
}
