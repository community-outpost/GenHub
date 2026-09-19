using GenHub.Core.Helpers;
using System;
using System.IO;
using System.IO.Compression;
using Xunit;

namespace GenHub.Tests.Core.Helpers;

/// <summary>
/// Unit tests for <see cref="ZipArchiveGuard"/>.
/// </summary>
public class ZipArchiveGuardTests : IDisposable
{
    private readonly string _tempRoot;

    /// <summary>
    /// Initializes a new instance of the <see cref="ZipArchiveGuardTests"/> class.
    /// </summary>
    public ZipArchiveGuardTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "GenHubTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempRoot);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Tests that a valid archive extracts to the destination directory.
    /// </summary>
    [Fact]
    public void ExtractToDirectory_WithValidArchive_ExtractsFiles()
    {
        var zipPath = Path.Combine(_tempRoot, "valid.zip");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("data/file.txt");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("payload");
        }

        var destination = Path.Combine(_tempRoot, "out");
        ZipArchiveGuard.ExtractToDirectory(zipPath, destination);

        Assert.Equal("payload", File.ReadAllText(Path.Combine(destination, "data", "file.txt")));
    }

    /// <summary>
    /// Tests that an entry escaping the destination directory is rejected.
    /// </summary>
    [Fact]
    public void ExtractToDirectory_WithTraversalEntry_ThrowsIOException()
    {
        var zipPath = Path.Combine(_tempRoot, "evil.zip");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("../evil.txt");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("payload");
        }

        Assert.Throws<IOException>(() => ZipArchiveGuard.ExtractToDirectory(zipPath, Path.Combine(_tempRoot, "out")));
    }
}
