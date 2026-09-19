using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GenHub.Core.Models.Tools.ModBuilder;
using GenHub.Features.Content.Services.CommunityOutpost;
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
        if (workspaceExtracted != null)
        {
            var unpackedDir = Path.Combine(workspaceExtracted, "unpacked");
            var officialBigFile = Path.Combine(workspaceExtracted, "500_900_CommunityPatch_CoreINI.big");

            if (Directory.Exists(unpackedDir) && File.Exists(officialBigFile))
            {
                var targetBig = Path.Combine(_tempDirectory, "repacked.big");

                // Act
                var result = await _service.CreateBigArchiveAsync(unpackedDir, targetBig);

                // Assert
                result.Success.Should().BeTrue(result.FirstError);
                File.Exists(targetBig).Should().BeTrue();

                var expectedBytes = await File.ReadAllBytesAsync(officialBigFile);
                var actualBytes = await File.ReadAllBytesAsync(targetBig);

                actualBytes.Should().Equal(expectedBytes);
                return;
            }
        }

        // Fallback deterministic verification for CI when the 300MB reference fixture is not cloned
        var fixtureDir = Path.Combine(_tempDirectory, "synthetic_patch");
        Directory.CreateDirectory(Path.Combine(fixtureDir, "Data", "INI"));
        await File.WriteAllTextAsync(Path.Combine(fixtureDir, "Data", "INI", "GameData.ini"), "GameData\n  Windowed = Yes\nEnd\n");
        await File.WriteAllTextAsync(Path.Combine(fixtureDir, "Data", "INI", "ControlBar.ini"), "ControlBar\nEnd\n");

        var packedBig = Path.Combine(_tempDirectory, "synthetic.big");
        var packResult = await _service.CreateBigArchiveAsync(fixtureDir, packedBig);
        packResult.Success.Should().BeTrue(packResult.FirstError);
        File.Exists(packedBig).Should().BeTrue();

        var unpackDir = Path.Combine(_tempDirectory, "unpacked_synthetic");
        var unpackResult = await _service.ExtractBigArchiveAsync(packedBig, unpackDir);
        unpackResult.Success.Should().BeTrue(unpackResult.FirstError);
        unpackResult.Data.Should().Be(2);

        var originalContent = await File.ReadAllTextAsync(Path.Combine(fixtureDir, "Data", "INI", "GameData.ini"));
        var unpackedContent = await File.ReadAllTextAsync(Path.Combine(unpackDir, "Data", "INI", "GameData.ini"));
        unpackedContent.Should().Be(originalContent);
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

    [Fact]
    public async Task CreateBigArchiveAsync_WithManifest_PreservesExactLayoutAndTrailer()
    {
        // Arrange
        var sourceDir = Path.Combine(_tempDirectory, "manifest_source");
        Directory.CreateDirectory(Path.Combine(sourceDir, "Data", "INI"));
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "Data", "INI", "GameData.ini"), "[GameData]\nWindowed = Yes\n");
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "Data", "INI", "ControlBar.ini"), "[ControlBar]\nEnabled = 1\n");

        var firstBig = Path.Combine(_tempDirectory, "first.big");
        var firstResult = await _service.CreateBigArchiveAsync(sourceDir, firstBig);
        firstResult.Success.Should().BeTrue();

        // Extract manifest from first archive
        var manifest = BigFilePacker.ExtractManifest(firstBig);
        manifest.Should().NotBeNull();
        manifest.EntryOrder.Should().HaveCount(2);

        // Customize manifest with a custom trailer to simulate FinalBIG or special publishers
        manifest.TrailerHex = "4C3232350000000000";
        var manifestPath = Path.Combine(_tempDirectory, "custom.manifest.json");
        await BigFilePacker.SaveManifestAsync(manifest, manifestPath);

        // Repack using the manifest
        var secondBig = Path.Combine(_tempDirectory, "second.big");
        var secondResult = await _service.CreateBigArchiveAsync(sourceDir, secondBig, manifestFilePath: manifestPath);
        secondResult.Success.Should().BeTrue();

        // Verify the repacked big has the custom trailer
        var secondBytes = await File.ReadAllBytesAsync(secondBig);
        var secondManifest = BigFilePacker.ExtractManifest(secondBig);
        secondManifest.TrailerHex.Should().Be("4C3232350000000000");

        // Repack a third time with the exact same manifest -> byte for byte identical to secondBig
        var thirdBig = Path.Combine(_tempDirectory, "third.big");
        var thirdResult = await _service.CreateBigArchiveAsync(sourceDir, thirdBig, manifestFilePath: manifestPath);
        thirdResult.Success.Should().BeTrue();

        var thirdBytes = await File.ReadAllBytesAsync(thirdBig);
        thirdBytes.Should().Equal(secondBytes);
    }

    [Fact]
    public async Task CreateBigArchiveAsync_WithCorruptExplicitManifest_ReturnsSpecificFailure()
    {
        // Arrange
        var sourceDir = Path.Combine(_tempDirectory, "corrupt_manifest_source");
        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "file.txt"), "content");

        var manifestPath = Path.Combine(_tempDirectory, "corrupt.manifest.json");
        await File.WriteAllTextAsync(manifestPath, "{ this is not valid json");

        var targetBig = Path.Combine(_tempDirectory, "corrupt_manifest.big");

        // Act
        var result = await _service.CreateBigArchiveAsync(sourceDir, targetBig, manifestFilePath: manifestPath);

        // Assert
        result.Success.Should().BeFalse();
        result.FirstError.Should().Contain("could not be parsed");
        result.FirstError.Should().Contain(manifestPath);
        File.Exists(targetBig).Should().BeFalse();
    }

    [Fact]
    public async Task CreateBigArchiveAsync_WithEmptyExplicitManifest_ReturnsSpecificFailure()
    {
        // Arrange
        var sourceDir = Path.Combine(_tempDirectory, "empty_manifest_source");
        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "file.txt"), "content");

        var manifestPath = Path.Combine(_tempDirectory, "empty.manifest.json");
        await File.WriteAllTextAsync(manifestPath, "null");

        var targetBig = Path.Combine(_tempDirectory, "empty_manifest.big");

        // Act
        var result = await _service.CreateBigArchiveAsync(sourceDir, targetBig, manifestFilePath: manifestPath);

        // Assert
        result.Success.Should().BeFalse();
        result.FirstError.Should().Contain("could not be loaded");
        result.FirstError.Should().Contain(manifestPath);
        File.Exists(targetBig).Should().BeFalse();
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

    [Fact]
    public async Task CreateBigArchiveAsync_WithProgress_ReportsGranularProgress()
    {
        // Arrange
        var sourceDir = Path.Combine(_tempDirectory, "source");
        Directory.CreateDirectory(sourceDir);
        for (var i = 0; i < 40; i++)
        {
            await File.WriteAllTextAsync(Path.Combine(sourceDir, $"file{i:000}.txt"), new string('x', 1024));
        }

        var targetBig = Path.Combine(_tempDirectory, "output.big");
        var reported = new List<double>();
        var progressMock = new Mock<IProgress<double>>();
        progressMock.Setup(p => p.Report(It.IsAny<double>()))
            .Callback<double>(reported.Add);

        // Act
        var result = await _service.CreateBigArchiveAsync(sourceDir, targetBig, progress: progressMock.Object);

        // Assert
        result.Success.Should().BeTrue();
        reported.Should().HaveCountGreaterThan(2, "packing must report intermediate progress, not just completion");
        reported.First().Should().Be(0.0);
        reported.Last().Should().Be(1.0);
        reported.Should().OnlyContain(p => p >= 0.0 && p <= 1.0);
        reported.Should().BeInAscendingOrder();
    }
}
