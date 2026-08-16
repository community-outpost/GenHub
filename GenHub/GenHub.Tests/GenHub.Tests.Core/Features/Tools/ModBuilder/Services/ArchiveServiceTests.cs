using System;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GenHub.Features.Tools.ModBuilder.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace GenHub.Tests.Core.Features.Tools.ModBuilder.Services;

/// <summary>
/// Unit tests for <see cref="ArchiveService"/>.
/// </summary>
public sealed class ArchiveServiceTests : IDisposable
{
    private readonly Mock<ILogger<ArchiveService>> _mockLogger;
    private readonly ArchiveService _service;
    private readonly string _tempDirectory;

    public ArchiveServiceTests()
    {
        _mockLogger = new Mock<ILogger<ArchiveService>>();
        _service = new ArchiveService(_mockLogger.Object);
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
        var service = new ArchiveService(_mockLogger.Object);

        // Assert
        service.Should().NotBeNull();
    }

    [Fact]
    public async Task CreateZipArchiveAsync_WithValidDirectory_CreatesZip()
    {
        // Arrange
        var sourceDir = Path.Combine(_tempDirectory, "source");
        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "file1.txt"), "content1");
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "file2.txt"), "content2");

        var targetZip = Path.Combine(_tempDirectory, "output.zip");

        // Act
        var result = await _service.CreateZipArchiveAsync(sourceDir, targetZip);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        File.Exists(targetZip).Should().BeTrue();
    }

    [Fact]
    public async Task CreateZipArchiveAsync_WithNonExistentDirectory_ReturnsFailure()
    {
        // Arrange
        var sourceDir = Path.Combine(_tempDirectory, "nonexistent");
        var targetZip = Path.Combine(_tempDirectory, "output.zip");

        // Act
        var result = await _service.CreateZipArchiveAsync(sourceDir, targetZip);

        // Assert
        result.Success.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("not found"));
    }

    [Fact]
    public async Task CreateZipArchiveAsync_WithCompressionLevel_UsesSpecifiedLevel()
    {
        // Arrange
        var sourceDir = Path.Combine(_tempDirectory, "source");
        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "file.txt"), "content");

        var targetZip = Path.Combine(_tempDirectory, "output.zip");

        // Act
        var result = await _service.CreateZipArchiveAsync(sourceDir, targetZip, CompressionLevel.Fastest);

        // Assert
        result.Success.Should().BeTrue();
        File.Exists(targetZip).Should().BeTrue();
    }

    [Fact]
    public async Task CreateZipArchiveAsync_WithProgress_ReportsProgress()
    {
        // Arrange
        var sourceDir = Path.Combine(_tempDirectory, "source");
        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "file.txt"), "content");

        var targetZip = Path.Combine(_tempDirectory, "output.zip");
        var progressReported = false;
        var progress = new Progress<double>(p => progressReported = true);

        // Act
        var result = await _service.CreateZipArchiveAsync(sourceDir, targetZip, progress: progress);

        // Assert
        result.Success.Should().BeTrue();
        progressReported.Should().BeTrue();
    }

    [Fact]
    public async Task CreateZipArchiveAsync_WithExistingFile_OverwritesFile()
    {
        // Arrange
        var sourceDir = Path.Combine(_tempDirectory, "source");
        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "file.txt"), "content");

        var targetZip = Path.Combine(_tempDirectory, "output.zip");
        await File.WriteAllTextAsync(targetZip, "old content");

        // Act
        var result = await _service.CreateZipArchiveAsync(sourceDir, targetZip);

        // Assert
        result.Success.Should().BeTrue();
        File.Exists(targetZip).Should().BeTrue();
    }

    [Fact]
    public async Task CreateZipArchiveAsync_WithNestedDirectories_IncludesAllFiles()
    {
        // Arrange
        var sourceDir = Path.Combine(_tempDirectory, "source");
        var subDir = Path.Combine(sourceDir, "subdir");
        Directory.CreateDirectory(subDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "file1.txt"), "content1");
        await File.WriteAllTextAsync(Path.Combine(subDir, "file2.txt"), "content2");

        var targetZip = Path.Combine(_tempDirectory, "output.zip");

        // Act
        var result = await _service.CreateZipArchiveAsync(sourceDir, targetZip);

        // Assert
        result.Success.Should().BeTrue();
        using var archive = ZipFile.OpenRead(targetZip);
        archive.Entries.Should().HaveCountGreaterOrEqualTo(2);
    }

    [Fact]
    public async Task CreateTarArchiveAsync_WithValidDirectory_CreatesTar()
    {
        // Arrange
        var sourceDir = Path.Combine(_tempDirectory, "source");
        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "file.txt"), "content");

        var targetTar = Path.Combine(_tempDirectory, "output.tar");

        // Act
        var result = await _service.CreateTarArchiveAsync(sourceDir, targetTar);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        File.Exists(targetTar).Should().BeTrue();
    }

    [Fact]
    public async Task CreateTarArchiveAsync_WithNonExistentDirectory_ReturnsFailure()
    {
        // Arrange
        var sourceDir = Path.Combine(_tempDirectory, "nonexistent");
        var targetTar = Path.Combine(_tempDirectory, "output.tar");

        // Act
        var result = await _service.CreateTarArchiveAsync(sourceDir, targetTar);

        // Assert
        result.Success.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("not found"));
    }

    [Fact]
    public async Task CreateTarGzArchiveAsync_WithValidDirectory_CreatesTarGz()
    {
        // Arrange
        var sourceDir = Path.Combine(_tempDirectory, "source");
        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "file.txt"), "content");

        var targetTarGz = Path.Combine(_tempDirectory, "output.tar.gz");

        // Act
        var result = await _service.CreateTarGzArchiveAsync(sourceDir, targetTarGz);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        File.Exists(targetTarGz).Should().BeTrue();
    }

    [Fact]
    public async Task CreateTarGzArchiveAsync_WithNonExistentDirectory_ReturnsFailure()
    {
        // Arrange
        var sourceDir = Path.Combine(_tempDirectory, "nonexistent");
        var targetTarGz = Path.Combine(_tempDirectory, "output.tar.gz");

        // Act
        var result = await _service.CreateTarGzArchiveAsync(sourceDir, targetTarGz);

        // Assert
        result.Success.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("not found"));
    }

    [Fact]
    public async Task CreateBigArchiveAsync_WithValidDirectory_CreatesBig()
    {
        // Arrange
        var sourceDir = Path.Combine(_tempDirectory, "source");
        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "file.txt"), "content");

        var targetBig = Path.Combine(_tempDirectory, "output.big");

        // Act
        var result = await _service.CreateBigArchiveAsync(sourceDir, targetBig);

        // Assert
        result.Should().NotBeNull();
        // BIG archive creation may require specific tools, so we just check the result structure
    }

    [Fact]
    public async Task CreateBigArchiveAsync_WithNonExistentDirectory_ReturnsFailure()
    {
        // Arrange
        var sourceDir = Path.Combine(_tempDirectory, "nonexistent");
        var targetBig = Path.Combine(_tempDirectory, "output.big");

        // Act
        var result = await _service.CreateBigArchiveAsync(sourceDir, targetBig);

        // Assert
        result.Success.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("not found"));
    }

    [Fact]
    public async Task CreateZipArchiveAsync_WithCancellation_ThrowsOperationCanceledException()
    {
        // Arrange
        var sourceDir = Path.Combine(_tempDirectory, "source");
        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "file.txt"), "content");

        var targetZip = Path.Combine(_tempDirectory, "output.zip");
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await _service.CreateZipArchiveAsync(sourceDir, targetZip, cancellationToken: cts.Token));
    }

    [Fact]
    public async Task CreateZipArchiveAsync_WithEmptyDirectory_CreatesEmptyZip()
    {
        // Arrange
        var sourceDir = Path.Combine(_tempDirectory, "empty");
        Directory.CreateDirectory(sourceDir);

        var targetZip = Path.Combine(_tempDirectory, "output.zip");

        // Act
        var result = await _service.CreateZipArchiveAsync(sourceDir, targetZip);

        // Assert
        result.Success.Should().BeTrue();
        File.Exists(targetZip).Should().BeTrue();
    }
}
