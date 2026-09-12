using System;
using System.IO;
using GenHub.Core.Utilities;
using Xunit;

namespace GenHub.Tests.Core.Utilities;

/// <summary>
/// Unit tests for <see cref="BigArchiveClassifier"/>.
/// </summary>
public class BigArchiveClassifierTests : IDisposable
{
    private readonly string _tempDirectory = Directory.CreateTempSubdirectory("big-classifier-tests").FullName;

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
    /// Tests that valid BIG magic byte headers return true.
    /// </summary>
    /// <param name="fourthByte">The fourth magic byte variant.</param>
    [Theory]
    [InlineData((byte)'4')]
    [InlineData((byte)'F')]
    [InlineData((byte)'E')]
    [InlineData((byte)0)]
    public void HasBigArchiveMagicBytes_ValidSignatures_ReturnsTrue(byte fourthByte)
    {
        ReadOnlySpan<byte> header = [(byte)'B', (byte)'I', (byte)'G', fourthByte];
        Assert.True(BigArchiveClassifier.HasBigArchiveMagicBytes(header));
    }

    /// <summary>
    /// Tests that headers shorter than 4 bytes return false.
    /// </summary>
    [Fact]
    public void HasBigArchiveMagicBytes_TooShort_ReturnsFalse()
    {
        ReadOnlySpan<byte> header = [(byte)'B', (byte)'I', (byte)'G'];
        Assert.False(BigArchiveClassifier.HasBigArchiveMagicBytes(header));
    }

    /// <summary>
    /// Tests that headers with non-BIG signatures return false.
    /// </summary>
    [Fact]
    public void HasBigArchiveMagicBytes_InvalidSignature_ReturnsFalse()
    {
        ReadOnlySpan<byte> header = [(byte)'M', (byte)'Z', 0x90, 0x00];
        Assert.False(BigArchiveClassifier.HasBigArchiveMagicBytes(header));
    }

    /// <summary>
    /// Tests that valid BIG archive files on disk are detected as true.
    /// </summary>
    /// <param name="fourthByte">The fourth magic byte variant.</param>
    [Theory]
    [InlineData((byte)'4')]
    [InlineData((byte)'F')]
    [InlineData((byte)'E')]
    [InlineData((byte)0)]
    public void IsBigArchiveFile_ValidBigFile_ReturnsTrue(byte fourthByte)
    {
        var filePath = Path.Combine(_tempDirectory, $"test_{fourthByte}.big");
        var bytes = new byte[16];
        bytes[0] = (byte)'B';
        bytes[1] = (byte)'I';
        bytes[2] = (byte)'G';
        bytes[3] = fourthByte;
        File.WriteAllBytes(filePath, bytes);

        Assert.True(BigArchiveClassifier.IsBigArchiveFile(filePath));
    }

    /// <summary>
    /// Tests that files smaller than 16 bytes return false.
    /// </summary>
    [Fact]
    public void IsBigArchiveFile_FileTooShort_ReturnsFalse()
    {
        var filePath = Path.Combine(_tempDirectory, "short.big");
        File.WriteAllBytes(filePath, [(byte)'B', (byte)'I', (byte)'G', (byte)'4']);

        Assert.False(BigArchiveClassifier.IsBigArchiveFile(filePath));
    }

    /// <summary>
    /// Tests that non-existent files return false.
    /// </summary>
    [Fact]
    public void IsBigArchiveFile_NonExistentFile_ReturnsFalse()
    {
        var filePath = Path.Combine(_tempDirectory, "nonexistent.big");
        Assert.False(BigArchiveClassifier.IsBigArchiveFile(filePath));
    }

    /// <summary>
    /// Tests that null, empty, or whitespace paths return false.
    /// </summary>
    /// <param name="path">The file path to test.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsBigArchiveFile_NullOrWhiteSpace_ReturnsFalse(string? path)
    {
        Assert.False(BigArchiveClassifier.IsBigArchiveFile(path!));
    }
}
