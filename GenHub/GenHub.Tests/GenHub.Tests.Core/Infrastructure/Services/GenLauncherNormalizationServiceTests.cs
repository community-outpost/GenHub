using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Utilities;
using GenHub.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit.Abstractions;

namespace GenHub.Tests.Core.Infrastructure.Services;

/// <summary>
/// Tests for <see cref="GenLauncherNormalizationService"/>.
/// </summary>
public class GenLauncherNormalizationServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly GenLauncherNormalizationService _service;
    private readonly ITestOutputHelper _testOutput;

    /// <summary>
    /// Initializes a new instance of the <see cref="GenLauncherNormalizationServiceTests"/> class.
    /// </summary>
    /// <param name="testOutput">The test output helper.</param>
    public GenLauncherNormalizationServiceTests(ITestOutputHelper testOutput)
    {
        _testOutput = testOutput;
        _tempDir = Path.Combine(Path.GetTempPath(), "GenHubTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
        _service = new GenLauncherNormalizationService(new Mock<ILogger<GenLauncherNormalizationService>>().Object);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch
        {
            // Allowed to fail during cleanup
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Tests that .gib files are converted to .big.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task NormalizeFilesAsync_ConvertsGibToBigAsync()
    {
        var gibPath = Path.Combine(_tempDir, "data.gib");
        await File.WriteAllTextAsync(gibPath, "gib-content");

        var result = await _service.NormalizeFilesAsync(_tempDir);

        Assert.True(result.Success);
        Assert.Equal(1, result.Data.NormalizedCount);
        Assert.False(File.Exists(gibPath));
        Assert.True(File.Exists(Path.Combine(_tempDir, "data.big")));
    }

    /// <summary>
    /// Tests that GenLauncher suffixes are removed from files.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task NormalizeFilesAsync_RemovesSuffixesAsync()
    {
        var glrPath = Path.Combine(_tempDir, "sound.wav.GLR");
        var gofPath = Path.Combine(_tempDir, "texture.tga.GOF");
        var gltcPath = Path.Combine(_tempDir, "map.map.GLTC");
        await File.WriteAllTextAsync(glrPath, "a");
        await File.WriteAllTextAsync(gofPath, "b");
        await File.WriteAllTextAsync(gltcPath, "c");

        var result = await _service.NormalizeFilesAsync(_tempDir);

        Assert.True(result.Success);
        Assert.Equal(3, result.Data.NormalizedCount);
        Assert.True(File.Exists(Path.Combine(_tempDir, "sound.wav")));
        Assert.True(File.Exists(Path.Combine(_tempDir, "texture.tga")));
        Assert.True(File.Exists(Path.Combine(_tempDir, "map.map")));
    }

    /// <summary>
    /// Tests that files with both a .gib extension and a GenLauncher suffix are fully normalized.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task NormalizeFilesAsync_ConvertsGibWithSuffixToBigAsync()
    {
        var sourcePath = Path.Combine(_tempDir, "sound.gib.GLR");
        await File.WriteAllTextAsync(sourcePath, "gib-content");

        var result = await _service.NormalizeFilesAsync(_tempDir);

        Assert.True(result.Success);
        Assert.Equal(2, result.Data.NormalizedCount);
        Assert.False(File.Exists(sourcePath));
        Assert.False(File.Exists(Path.Combine(_tempDir, "sound.gib")));
        Assert.True(File.Exists(Path.Combine(_tempDir, "sound.big")));
    }

    /// <summary>
    /// Tests that normalization skips moves when the destination already exists.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task NormalizeFilesAsync_SkipsWhenDestinationExistsAsync()
    {
        var gibPath = Path.Combine(_tempDir, "data.gib");
        var bigPath = Path.Combine(_tempDir, "data.big");
        await File.WriteAllTextAsync(gibPath, "gib-content");
        await File.WriteAllTextAsync(bigPath, "existing-big");

        var result = await _service.NormalizeFilesAsync(_tempDir);

        Assert.True(result.Success);
        Assert.Equal(0, result.Data.NormalizedCount);
        Assert.False(result.Data.IsFullySuccessful);
        Assert.Contains(gibPath, result.Data.FailedFiles);
        Assert.True(File.Exists(gibPath));
        Assert.Equal("existing-big", await File.ReadAllTextAsync(bigPath));
    }

    /// <summary>
    /// Tests that directory with .GLTC suffix is detected, renamed, and contents preserved.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task NormalizeFilesAsync_DetectsAndRenamesSuffixDirectory_PreservingContentsAsync()
    {
        var gltcDir = Path.Combine(_tempDir, "Maps.GLTC");
        Directory.CreateDirectory(gltcDir);
        var mapPath = Path.Combine(gltcDir, "map1.map");
        var gibPath = Path.Combine(gltcDir, "map2.gib");
        await File.WriteAllTextAsync(mapPath, "map-content");
        await File.WriteAllTextAsync(gibPath, "gib-content");

        var result = await _service.NormalizeFilesAsync(_tempDir);

        Assert.True(result.Success);
        Assert.Equal(2, result.Data.NormalizedCount);
        var targetDir = Path.Combine(_tempDir, "Maps");
        Assert.True(Directory.Exists(targetDir));
        Assert.False(Directory.Exists(gltcDir));
        Assert.True(File.Exists(Path.Combine(targetDir, "map1.map")));
        Assert.True(File.Exists(Path.Combine(targetDir, "map2.big")));
    }

    /// <summary>
    /// Tests that directory suffix normalization skips when destination directory already exists.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task NormalizeFilesAsync_SkipsDirectorySuffixRemoval_WhenDestinationExistsAsync()
    {
        var gltcDir = Path.Combine(_tempDir, "Maps.GLTC");
        Directory.CreateDirectory(gltcDir);
        await File.WriteAllTextAsync(Path.Combine(gltcDir, "map1.map"), "map-content");

        var existingTargetDir = Path.Combine(_tempDir, "Maps");
        Directory.CreateDirectory(existingTargetDir);
        await File.WriteAllTextAsync(Path.Combine(existingTargetDir, "existing.txt"), "existing-content");

        var result = await _service.NormalizeFilesAsync(_tempDir);

        Assert.True(result.Success);
        Assert.False(result.Data.IsFullySuccessful);
        Assert.Contains(gltcDir, result.Data.FailedFiles);
        Assert.True(Directory.Exists(gltcDir));
        Assert.True(Directory.Exists(existingTargetDir));
    }

    /// <summary>
    /// Tests that file symbolic links are removed during normalization.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task NormalizeFilesAsync_RemovesFileSymlinkAsync()
    {
        if (!TryCreateFileSymlink(out var skipReason))
        {
            _testOutput.WriteLine($"Not exercised: file symbolic links are unavailable here ({skipReason}).");
            return;
        }

        var targetPath = Path.Combine(_tempDir, "target.txt");
        var linkPath = Path.Combine(_tempDir, "link.txt");
        await File.WriteAllTextAsync(targetPath, "target-content");
        File.CreateSymbolicLink(linkPath, targetPath);

        var result = await _service.NormalizeFilesAsync(_tempDir);

        Assert.True(result.Success);
        Assert.Equal(1, result.Data.SymbolicLinksRemoved);
        Assert.False(File.Exists(linkPath));
        Assert.True(File.Exists(targetPath));
    }

    /// <summary>
    /// Tests that dangling file symbolic links are removed during normalization.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task NormalizeFilesAsync_RemovesDanglingFileSymlinkAsync()
    {
        if (!TryCreateFileSymlink(out var skipReason))
        {
            _testOutput.WriteLine($"Not exercised: file symbolic links are unavailable here ({skipReason}).");
            return;
        }

        var targetPath = Path.Combine(_tempDir, "nonexistent-target.txt");
        var linkPath = Path.Combine(_tempDir, "dangling-link.txt");
        File.CreateSymbolicLink(linkPath, targetPath);

        var result = await _service.NormalizeFilesAsync(_tempDir);

        Assert.True(result.Success);
        Assert.Equal(1, result.Data.SymbolicLinksRemoved);
        Assert.False(File.Exists(linkPath));
    }

    /// <summary>
    /// Tests that dangling directory symbolic links are removed during normalization.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task NormalizeFilesAsync_RemovesDanglingDirectorySymlinkAsync()
    {
        if (!TryCreateDirectorySymlink(out var skipReason))
        {
            _testOutput.WriteLine($"Not exercised: directory symbolic links are unavailable here ({skipReason}).");
            return;
        }

        var targetPath = Path.Combine(_tempDir, "nonexistent-dir-target");
        var linkPath = Path.Combine(_tempDir, "dangling-dir-link");
        Directory.CreateSymbolicLink(linkPath, targetPath);

        var result = await _service.NormalizeFilesAsync(_tempDir);

        Assert.True(result.Success);
        Assert.Equal(1, result.Data.SymbolicLinksRemoved);
        Assert.False(Directory.Exists(linkPath));
    }

    /// <summary>
    /// Tests that normalization honors cancellation.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task NormalizeFilesAsync_HonorsCancellationAsync()
    {
        var gibPath = Path.Combine(_tempDir, "data.gib");
        await File.WriteAllTextAsync(gibPath, "gib-content");

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _service.NormalizeFilesAsync(_tempDir, cts.Token));

        Assert.True(File.Exists(gibPath));
    }

    /// <summary>
    /// Tests that detection reports GenLauncher files in nested directories.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task DetectGenLauncherFilesAsync_FindsNestedFilesAsync()
    {
        var nestedDir = Path.Combine(_tempDir, "mods", "audio");
        Directory.CreateDirectory(nestedDir);
        await File.WriteAllTextAsync(Path.Combine(nestedDir, "clip.gib"), "gib");

        var detection = await _service.DetectGenLauncherFilesAsync(_tempDir);

        Assert.True(detection.HasGenLauncherFiles);
        Assert.Single(detection.GibFiles);
    }

    /// <summary>
    /// A native game binary has no extension, so GenLauncher's suffix hides it from
    /// <see cref="ExecutableFileClassifier"/>: the name ends in <c>.GOF</c>, which is
    /// neither a library nor a runnable extension, so the magic bytes are never read.
    /// Stripping the suffix is what restores the extensionless shape that classification
    /// by content depends on, which makes the ordering in the import flow load-bearing.
    /// </summary>
    /// <param name="header">Native executable magic bytes to write to the file.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Theory]
    [InlineData(new byte[] { 0xCF, 0xFA, 0xED, 0xFE, 0x0C, 0x00, 0x00, 0x01 })]
    [InlineData(new byte[] { 0x7F, 0x45, 0x4C, 0x46, 0x02, 0x01, 0x01, 0x00 })]
    public async Task NormalizeFilesAsync_UnmasksNativeBinaryForMagicByteClassificationAsync(byte[] header)
    {
        var suffixedPath = Path.Combine(_tempDir, "generals" + GenLauncherConstants.OriginalFileSuffix);
        await File.WriteAllBytesAsync(suffixedPath, header);

        Assert.False(ExecutableFileClassifier.RequiresExecutePermission("generals.GOF", suffixedPath));
        Assert.False(ExecutableFileClassifier.IsLegacyLaunchCandidate("generals.GOF", suffixedPath));

        var result = await _service.NormalizeFilesAsync(_tempDir);

        Assert.True(result.Success);
        var normalizedPath = Path.Combine(_tempDir, "generals");
        Assert.False(File.Exists(suffixedPath));
        Assert.True(File.Exists(normalizedPath));

        Assert.True(ExecutableFileClassifier.RequiresExecutePermission("generals", normalizedPath));
        Assert.True(ExecutableFileClassifier.IsLegacyLaunchCandidate("generals", normalizedPath));
    }

    private bool TryCreateFileSymlink(out string? skipReason)
    {
        skipReason = null;

        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            skipReason = "Unsupported operating system.";
            return false;
        }

        try
        {
            var probeTarget = Path.Combine(_tempDir, "probe-target.txt");
            var probeLink = Path.Combine(_tempDir, "probe-link.txt");
            File.WriteAllText(probeTarget, "probe");
            File.CreateSymbolicLink(probeLink, probeTarget);
            File.Delete(probeLink);
            File.Delete(probeTarget);
            return true;
        }
        catch (IOException ex) when (ex.Message.Contains("privilege", StringComparison.OrdinalIgnoreCase))
        {
            skipReason = ex.Message;
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            skipReason = ex.Message;
            return false;
        }
        catch (PlatformNotSupportedException ex)
        {
            skipReason = ex.Message;
            return false;
        }
    }

    private bool TryCreateDirectorySymlink(out string? skipReason)
    {
        skipReason = null;

        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            skipReason = "Unsupported operating system.";
            return false;
        }

        try
        {
            var probeTarget = Path.Combine(_tempDir, "probe-dir-target");
            var probeLink = Path.Combine(_tempDir, "probe-dir-link");
            Directory.CreateDirectory(probeTarget);
            Directory.CreateSymbolicLink(probeLink, probeTarget);
            Directory.Delete(probeLink);
            Directory.Delete(probeTarget);
            return true;
        }
        catch (IOException ex) when (ex.Message.Contains("privilege", StringComparison.OrdinalIgnoreCase))
        {
            skipReason = ex.Message;
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            skipReason = ex.Message;
            return false;
        }
        catch (PlatformNotSupportedException ex)
        {
            skipReason = ex.Message;
            return false;
        }
    }
}
