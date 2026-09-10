using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
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
        var progressMock = new Mock<IProgress<double>>();

        // Act
        var result = await _service.CreateZipArchiveAsync(sourceDir, targetZip, progress: progressMock.Object);

        // Assert
        result.Success.Should().BeTrue();
        progressMock.Verify(p => p.Report(It.IsAny<double>()), Times.AtLeastOnce());
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
        result.Success.Should().BeTrue();
        File.Exists(targetBig).Should().BeTrue();
    }

    [Fact]
    public async Task CreateBigArchiveAsync_WithGeneralsGamePatch2Data_ProducesByteForByteExactMatch()
    {
        // Arrange
        var workspaceExtracted = FindExtractedPatchDir();
        if (workspaceExtracted == null)
        {
            return;
        }

        var unpackedDir = Path.Combine(workspaceExtracted, "unpacked");
        var officialBigFile = Path.Combine(workspaceExtracted, "500_900_CommunityPatch_CoreINI.big");

        if (!Directory.Exists(unpackedDir) || !File.Exists(officialBigFile))
        {
            return;
        }

        var targetBig = Path.Combine(_tempDirectory, "repacked.big");

        // Act
        var result = await _service.CreateBigArchiveAsync(unpackedDir, targetBig);

        // Assert
        result.Success.Should().BeTrue(result.FirstError);
        File.Exists(targetBig).Should().BeTrue();

        var expectedBytes = await File.ReadAllBytesAsync(officialBigFile);
        var actualBytes = await File.ReadAllBytesAsync(targetBig);

        actualBytes.Length.Should().Be(expectedBytes.Length);
        actualBytes.Should().Equal(expectedBytes);

        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(actualBytes);
        var hashString = Convert.ToHexString(hashBytes).ToLowerInvariant();
        hashString.Should().Be("6a02aca9aebe6602b3e4bb76bf6e2cf35086a33fec7c6f000d8e7a4048629775");
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

    private static string? FindExtractedPatchDir()
    {
        var current = AppDomain.CurrentDomain.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            var candidate = Path.Combine(current, "extracted_patch");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            var parent = Directory.GetParent(current);
            if (parent == null)
            {
                break;
            }

            current = parent.FullName;
        }

        return null;
    }

    [Fact]
    public async Task ExtractBigArchiveAsync_WithValidBigArchive_ExtractsFilesSuccessfully()
    {
        // Arrange
        var sourceDir = Path.Combine(_tempDirectory, "big_source");
        Directory.CreateDirectory(Path.Combine(sourceDir, "Data", "INI"));
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "Data", "INI", "GameData.ini"), "DefaultCameraHeight = 350.0");
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "Readme.txt"), "Sample Readme");

        var bigPath = Path.Combine(_tempDirectory, "TestArchive.big");
        var packResult = await _service.CreateBigArchiveAsync(sourceDir, bigPath);
        packResult.Success.Should().BeTrue();

        var extractDir = Path.Combine(_tempDirectory, "extracted_output");

        // Act
        var extractResult = await _service.ExtractBigArchiveAsync(bigPath, extractDir);

        // Assert
        extractResult.Success.Should().BeTrue();
        File.Exists(Path.Combine(extractDir, "Data", "INI", "GameData.ini")).Should().BeTrue();
        (await File.ReadAllTextAsync(Path.Combine(extractDir, "Data", "INI", "GameData.ini"))).Should().Be("DefaultCameraHeight = 350.0");
        File.Exists(Path.Combine(extractDir, "Readme.txt")).Should().BeTrue();
        (await File.ReadAllTextAsync(Path.Combine(extractDir, "Readme.txt"))).Should().Be("Sample Readme");
    }

    [Fact]
    public async Task ExtractBigArchivesAsync_WithMultipleArchives_ExtractsAllFiles()
    {
        // Arrange
        var source1 = Path.Combine(_tempDirectory, "big1_src");
        Directory.CreateDirectory(source1);
        await File.WriteAllTextAsync(Path.Combine(source1, "Mod1.txt"), "Mod 1 Content");
        var big1 = Path.Combine(_tempDirectory, "Mod1.big");
        (await _service.CreateBigArchiveAsync(source1, big1)).Success.Should().BeTrue();

        var source2 = Path.Combine(_tempDirectory, "big2_src");
        Directory.CreateDirectory(source2);
        await File.WriteAllTextAsync(Path.Combine(source2, "Mod2.txt"), "Mod 2 Content");
        var big2 = Path.Combine(_tempDirectory, "Mod2.big");
        (await _service.CreateBigArchiveAsync(source2, big2)).Success.Should().BeTrue();

        var extractDir = Path.Combine(_tempDirectory, "multi_extracted");

        // Act
        var result = await _service.ExtractBigArchivesAsync(new[] { big1, big2 }, extractDir);

        // Assert
        result.Success.Should().BeTrue();
        File.Exists(Path.Combine(extractDir, "Mod1.txt")).Should().BeTrue();
        File.Exists(Path.Combine(extractDir, "Mod2.txt")).Should().BeTrue();
    }

    [Fact]
    public async Task ExtractBigArchiveAsync_WithNonExistentFile_ReturnsFailure()
    {
        // Arrange
        var nonExistent = Path.Combine(_tempDirectory, "NonExistent.big");
        var extractDir = Path.Combine(_tempDirectory, "fail_output");

        // Act
        var result = await _service.ExtractBigArchiveAsync(nonExistent, extractDir);

        // Assert
        result.Success.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("not found"));
    }
}
