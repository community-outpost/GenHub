using GenHub.Core.Helpers;
using System;
using System.IO;
using Xunit;

namespace GenHub.Tests.Core.Helpers;

/// <summary>
/// Unit tests for <see cref="MediaFileHelper"/> image content inspection.
/// </summary>
public sealed class MediaFileHelperTests : IDisposable
{
    private readonly string _tempDirectory;

    /// <summary>
    /// Initializes a new instance of the <see cref="MediaFileHelperTests"/> class.
    /// </summary>
    public MediaFileHelperTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "GenHub_MediaFileHelperTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            try
            {
                Directory.Delete(_tempDirectory, true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best-effort cleanup
            }
        }
    }

    /// <summary>
    /// Verifies that files with matching image headers are accepted.
    /// </summary>
    /// <param name="extension">The file extension to test.</param>
    /// <param name="hexHeader">The hexadecimal file header bytes.</param>
    [Theory]
    [InlineData(".png", "89504E470D0A1A0A")]
    [InlineData(".jpg", "FFD8FFE000104A464946")]
    [InlineData(".jpeg", "FFD8FF")]
    [InlineData(".gif", "474946383961")]
    [InlineData(".bmp", "424D46000000000000003600000028000000")]
    [InlineData(".ico", "00000100")]
    [InlineData(".webp", "524946460000000057454250")]
    public void HasImageContent_MatchingHeader_ReturnsTrue(string extension, string hexHeader)
    {
        var path = WriteTempFile("image" + extension, Convert.FromHexString(hexHeader));

        Assert.True(MediaFileHelper.HasImageContent(path));
    }

    /// <summary>
    /// Verifies that renamed text files are rejected even with an image extension.
    /// </summary>
    [Fact]
    public void HasImageContent_TextContentWithImageExtension_ReturnsFalse()
    {
        var path = WriteTempFile("cover.png", "This is plain text, not an image."u8.ToArray());

        Assert.False(MediaFileHelper.HasImageContent(path));
    }

    /// <summary>
    /// Verifies that image headers with a non-image extension are rejected.
    /// </summary>
    [Fact]
    public void HasImageContent_ImageHeaderWithTextExtension_ReturnsFalse()
    {
        var path = WriteTempFile("notes.txt", Convert.FromHexString("89504E470D0A1A0A"));

        Assert.False(MediaFileHelper.HasImageContent(path));
    }

    /// <summary>
    /// Verifies that missing and empty paths are rejected.
    /// </summary>
    /// <param name="path">The path to inspect.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void HasImageContent_MissingOrEmptyPath_ReturnsFalse(string? path)
    {
        Assert.False(MediaFileHelper.HasImageContent(path));
        Assert.False(MediaFileHelper.HasImageContent(Path.Combine(_tempDirectory, "missing.png")));
    }

    /// <summary>
    /// Verifies that truncated headers are rejected instead of accepted on a short read.
    /// </summary>
    /// <param name="extension">The file extension to test.</param>
    /// <param name="hexHeader">The truncated hexadecimal file header bytes.</param>
    [Theory]
    [InlineData(".png", "")]
    [InlineData(".png", "89504E47")]
    [InlineData(".webp", "52494646")]
    [InlineData(".bmp", "424D")]
    public void HasImageContent_TruncatedHeader_ReturnsFalse(string extension, string hexHeader)
    {
        var path = WriteTempFile("partial" + extension, Convert.FromHexString(hexHeader));

        Assert.False(MediaFileHelper.HasImageContent(path));
    }

    /// <summary>
    /// Verifies that text files starting with BM are rejected as BMP images.
    /// </summary>
    [Fact]
    public void HasImageContent_BmPrefixedText_ReturnsFalse()
    {
        var path = WriteTempFile("notes.bmp", "BMW is not a bitmap image."u8.ToArray());

        Assert.False(MediaFileHelper.HasImageContent(path));
    }

    private string WriteTempFile(string fileName, byte[] content)
    {
        var path = Path.Combine(_tempDirectory, fileName);
        File.WriteAllBytes(path, content);
        return path;
    }
}
