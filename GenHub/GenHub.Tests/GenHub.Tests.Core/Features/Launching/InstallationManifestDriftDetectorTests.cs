using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Features.Launching;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace GenHub.Tests.Core.Features.Launching;

/// <summary>
/// Tests for <see cref="InstallationManifestDriftDetector"/>.
/// </summary>
public sealed class InstallationManifestDriftDetectorTests : IDisposable
{
    private readonly string _gameDir;

    /// <summary>
    /// Initializes a new instance of the <see cref="InstallationManifestDriftDetectorTests"/> class.
    /// </summary>
    public InstallationManifestDriftDetectorTests()
    {
        _gameDir = Path.Combine(Path.GetTempPath(), "GenHub_DriftTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_gameDir);
    }

    /// <summary>
    /// Cleans up the temporary game directory.
    /// </summary>
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_gameDir))
            {
                Directory.Delete(_gameDir, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup failures
        }
    }

    /// <summary>
    /// Matching disk contents report no drift.
    /// </summary>
    [Fact]
    public void DetectDrift_WhenDiskMatchesManifest_ReportsNoDrift()
    {
        var bigPath = WriteFile("0_MyMod.big", 128);
        var manifestFiles = new List<ManifestFile>
        {
            new() { RelativePath = "0_MyMod.big", Size = new FileInfo(bigPath).Length },
        };

        var drift = InstallationManifestDriftDetector.DetectDrift(
            _gameDir, GameType.ZeroHour, "1.05", manifestFiles);

        Assert.False(drift.HasDrift);
        Assert.Empty(drift.AddedFiles);
        Assert.Empty(drift.RemovedFiles);
        Assert.Empty(drift.ChangedFiles);
    }

    /// <summary>
    /// A loose archive added to a scan-based install reports added drift.
    /// </summary>
    [Fact]
    public void DetectDrift_WhenLooseArchiveAddedToScanBasedInstall_ReportsAdded()
    {
        WriteFile("0_MyMod.big", 128);

        var drift = InstallationManifestDriftDetector.DetectDrift(
            _gameDir, GameType.ZeroHour, "1.05", []);

        Assert.True(drift.HasDrift);
        Assert.Contains("0_MyMod.big", drift.AddedFiles);
    }

    /// <summary>
    /// Loose extras are ignored for catalog-based installs whose generation would not capture them.
    /// </summary>
    [Fact]
    public void DetectDrift_WhenLooseArchiveAddedToCatalogBasedInstall_IgnoresAdded()
    {
        // Generals 1.08 generation is catalog-driven and ignores loose extras, so added
        // files must not trigger a refresh that could never converge.
        WriteFile("0_MyMod.big", 128);

        var drift = InstallationManifestDriftDetector.DetectDrift(
            _gameDir, GameType.Generals, "1.08", []);

        Assert.False(drift.HasDrift);
    }

    /// <summary>
    /// A manifest file missing from disk reports removed drift.
    /// </summary>
    [Fact]
    public void DetectDrift_WhenManifestFileMissing_ReportsRemoved()
    {
        var manifestFiles = new List<ManifestFile>
        {
            new() { RelativePath = "0_MyMod.big", Size = 128 },
        };

        var drift = InstallationManifestDriftDetector.DetectDrift(
            _gameDir, GameType.ZeroHour, "1.05", manifestFiles);

        Assert.True(drift.HasDrift);
        Assert.Contains("0_MyMod.big", drift.RemovedFiles);
    }

    /// <summary>
    /// A manifest file with a differing size reports changed drift.
    /// </summary>
    [Fact]
    public void DetectDrift_WhenManifestFileSizeDiffers_ReportsChanged()
    {
        WriteFile("generals.exe", 64);
        var manifestFiles = new List<ManifestFile>
        {
            new() { RelativePath = "generals.exe", Size = 128 },
        };

        var drift = InstallationManifestDriftDetector.DetectDrift(
            _gameDir, GameType.Generals, "1.08", manifestFiles);

        Assert.True(drift.HasDrift);
        Assert.Contains("generals.exe", drift.ChangedFiles);
    }

    /// <summary>
    /// Zero-size manifest entries skip the size comparison.
    /// </summary>
    [Fact]
    public void DetectDrift_WhenManifestSizeIsUnknown_SkipsChangedCheck()
    {
        // Fresh manifests may carry zero sizes until measured; those must not drift.
        WriteFile("generals.exe", 64);
        var manifestFiles = new List<ManifestFile>
        {
            new() { RelativePath = "generals.exe", Size = 0 },
        };

        var drift = InstallationManifestDriftDetector.DetectDrift(
            _gameDir, GameType.Generals, "1.08", manifestFiles);

        Assert.False(drift.HasDrift);
    }

    /// <summary>
    /// Skipped paths and untracked extensions never drift.
    /// </summary>
    [Fact]
    public void DetectDrift_IgnoresSkippedAndUntrackedFiles()
    {
        WriteFile(Path.Combine(".git", "config"), 32);
        WriteFile("notes.xyz", 32);

        var drift = InstallationManifestDriftDetector.DetectDrift(
            _gameDir, GameType.ZeroHour, "1.05", []);

        Assert.False(drift.HasDrift);
    }

    /// <summary>
    /// A sibling backup provides the manifest size, matching generation's proxy handling.
    /// </summary>
    [Fact]
    public void DetectDrift_WhenBackupHoldsManifestSize_ReportsNoDrift()
    {
        WriteFile("generals.exe", 64);
        WriteFile("generals.exe.bak", 128);
        var manifestFiles = new List<ManifestFile>
        {
            new() { RelativePath = "generals.exe", Size = 128 },
        };

        var drift = InstallationManifestDriftDetector.DetectDrift(
            _gameDir, GameType.Generals, "1.08", manifestFiles);

        Assert.False(drift.HasDrift);
    }

    /// <summary>
    /// A changed backup size reports changed drift.
    /// </summary>
    [Fact]
    public void DetectDrift_WhenBackupSizeDiffers_ReportsChanged()
    {
        WriteFile("generals.exe", 64);
        WriteFile("generals.exe.bak", 96);
        var manifestFiles = new List<ManifestFile>
        {
            new() { RelativePath = "generals.exe", Size = 128 },
        };

        var drift = InstallationManifestDriftDetector.DetectDrift(
            _gameDir, GameType.Generals, "1.08", manifestFiles);

        Assert.True(drift.HasDrift);
        Assert.Contains("generals.exe", drift.ChangedFiles);
    }

    /// <summary>
    /// A missing directory fails open with no drift.
    /// </summary>
    [Fact]
    public void DetectDrift_WhenDirectoryMissing_ReportsNoDrift()
    {
        var missing = Path.Combine(_gameDir, "Missing_" + Guid.NewGuid().ToString("N"));

        var drift = InstallationManifestDriftDetector.DetectDrift(
            missing, GameType.ZeroHour, "1.05", []);

        Assert.False(drift.HasDrift);
    }

    private string WriteFile(string relativePath, int size)
    {
        var fullPath = Path.Combine(_gameDir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllBytes(fullPath, Enumerable.Repeat((byte)0xAB, size).ToArray());
        return fullPath;
    }
}
