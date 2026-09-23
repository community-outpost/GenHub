using FluentAssertions;
using GenHub.Core.Services.Tools.Checksum;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.IO;

namespace GenHub.Tests.Core.Features.Tools.WndEditor.Services;

/// <summary>
/// Unit tests for <see cref="SageVirtualFileSystem"/> verifying case-insensitive resolution.
/// </summary>
public sealed class SageVirtualFileSystemTests : IDisposable
{
    private readonly string _tempRoot;

    /// <summary>
    /// Initializes a new instance of the <see cref="SageVirtualFileSystemTests"/> class.
    /// </summary>
    public SageVirtualFileSystemTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "GenHub_VfsTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    /// <summary>
    /// Cleans up the temporary directory.
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    /// <summary>
    /// Tests that FilesUnder discovers files even when the directory casing on disk differs from the query casing.
    /// </summary>
    [Fact]
    public void FilesUnder_WithDifferentDirectoryCasing_DiscoversLooseIniFiles()
    {
        // Arrange: create directory structure with different casing on disk
        var diskDir = Path.Combine(_tempRoot, "data", "ini", "mappedimages");
        Directory.CreateDirectory(diskDir);
        var testIniPath = Path.Combine(diskDir, "custom_images.ini");
        File.WriteAllText(testIniPath, "MappedImage Sample\nEnd\n");

        var vfs = new SageVirtualFileSystem(_tempRoot, isZeroHour: true, logger: Mock.Of<ILogger>());

        // Act: query with SAGE standard casing "Data\\INI\\MappedImages"
        var files = vfs.FilesUnder("Data\\INI\\MappedImages");

        // Assert
        files.Should().NotBeEmpty();
        files.Should().Contain(f => f.EndsWith("custom_images.ini", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Tests that Read reads loose files case-insensitively.
    /// </summary>
    [Fact]
    public void Read_WithCaseMismatch_ReturnsFileBytes()
    {
        // Arrange
        var diskDir = Path.Combine(_tempRoot, "art", "textures");
        Directory.CreateDirectory(diskDir);
        var diskFile = Path.Combine(diskDir, "sampletexture.tga");
        byte[] expectedBytes = [0x01, 0x02, 0x03, 0x04];
        File.WriteAllBytes(diskFile, expectedBytes);

        var vfs = new SageVirtualFileSystem(_tempRoot, isZeroHour: true, logger: Mock.Of<ILogger>());

        // Act: query with mixed case
        var readBytes = vfs.Read("Art\\Textures\\SampleTexture.tga");

        // Assert
        readBytes.Should().NotBeNull();
        readBytes.Should().Equal(expectedBytes);
    }
}
