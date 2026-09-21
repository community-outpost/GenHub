using GenHub.Core.Utilities;
using System;
using System.IO;
using Xunit;

namespace GenHub.Tests.Core.Utilities;

/// <summary>
/// Unit tests for <see cref="WindowsBinaryClassifier"/>.
/// </summary>
public class WindowsBinaryClassifierTests : IDisposable
{
    private readonly string _tempDirectory = Directory.CreateTempSubdirectory("windows-binary-tests").FullName;

    /// <inheritdoc/>
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
        }
        catch
        {
            // Allowed to fail during cleanup
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Tests that the MZ signature header returns true.
    /// </summary>
    [Fact]
    public void HasWindowsBinaryMagicBytes_ValidSignature_ReturnsTrue()
    {
        ReadOnlySpan<byte> header = [(byte)'M', (byte)'Z'];
        Assert.True(WindowsBinaryClassifier.HasWindowsBinaryMagicBytes(header));
    }

    /// <summary>
    /// Tests that headers shorter than 2 bytes return false.
    /// </summary>
    [Fact]
    public void HasWindowsBinaryMagicBytes_TooShort_ReturnsFalse()
    {
        ReadOnlySpan<byte> header = [(byte)'M'];
        Assert.False(WindowsBinaryClassifier.HasWindowsBinaryMagicBytes(header));
    }

    /// <summary>
    /// Tests that a native ELF header returns false.
    /// </summary>
    [Fact]
    public void HasWindowsBinaryMagicBytes_ElfSignature_ReturnsFalse()
    {
        ReadOnlySpan<byte> header = [0x7F, (byte)'E', (byte)'L', (byte)'F'];
        Assert.False(WindowsBinaryClassifier.HasWindowsBinaryMagicBytes(header));
    }

    /// <summary>
    /// Tests that a Windows binary file on disk is detected as true.
    /// </summary>
    [Fact]
    public void IsWindowsBinary_MzFile_ReturnsTrue()
    {
        var filePath = Path.Combine(_tempDirectory, "game.dat");
        File.WriteAllBytes(filePath, [(byte)'M', (byte)'Z', 0x90, 0x00]);

        Assert.True(WindowsBinaryClassifier.IsWindowsBinary(filePath));
    }

    /// <summary>
    /// Tests that a native binary file on disk returns false.
    /// </summary>
    [Fact]
    public void IsWindowsBinary_ElfFile_ReturnsFalse()
    {
        var filePath = Path.Combine(_tempDirectory, "generalszh");
        File.WriteAllBytes(filePath, [0x7F, (byte)'E', (byte)'L', (byte)'F']);

        Assert.False(WindowsBinaryClassifier.IsWindowsBinary(filePath));
    }

    /// <summary>
    /// Tests that files shorter than 2 bytes return false.
    /// </summary>
    [Fact]
    public void IsWindowsBinary_FileTooShort_ReturnsFalse()
    {
        var filePath = Path.Combine(_tempDirectory, "short.dat");
        File.WriteAllBytes(filePath, [(byte)'M']);

        Assert.False(WindowsBinaryClassifier.IsWindowsBinary(filePath));
    }

    /// <summary>
    /// Tests that non-existent files return false.
    /// </summary>
    [Fact]
    public void IsWindowsBinary_NonExistentFile_ReturnsFalse()
    {
        var filePath = Path.Combine(_tempDirectory, "missing.dat");

        Assert.False(WindowsBinaryClassifier.IsWindowsBinary(filePath));
    }
}
