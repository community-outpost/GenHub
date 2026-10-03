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

    /// <summary>
    /// Verifies that only absolute http/https URLs count as subscriber-openable remote media.
    /// </summary>
    /// <param name="value">The media reference to classify.</param>
    /// <param name="expected">The expected classification.</param>
    [Theory]
    [InlineData("https://cdn.example.com/trailer.mp4", true)]
    [InlineData("http://cdn.example.com/shot.png", true)]
    [InlineData("https://www.youtube.com/watch?v=abc123", true)]
    [InlineData("file:///E:/Downloaded/clip.mp4", false)]
    [InlineData("E:\\Downloaded\\clip.mp4", false)]
    [InlineData("/home/user/clip.mp4", false)]
    [InlineData("media/shot.png", false)]
    [InlineData("avares://GenHub/Assets/icon.png", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    public void IsRemoteHttpUrl_ClassifiesReferences(string? value, bool expected)
    {
        Assert.Equal(expected, MediaFileHelper.IsRemoteHttpUrl(value));
    }

    /// <summary>
    /// Verifies that project-relative, file://, and absolute media references resolve to existing files.
    /// </summary>
    [Fact]
    public void TryResolveLocalMediaPath_ResolvesAllLocalForms()
    {
        var shot = WriteTempFile("shot.png", [1, 2, 3, 4]);

        Assert.Equal(shot, MediaFileHelper.TryResolveLocalMediaPath(_tempDirectory, "shot.png"));
        Assert.Equal(shot, MediaFileHelper.TryResolveLocalMediaPath(_tempDirectory, new Uri(shot).AbsoluteUri));
        Assert.Equal(shot, MediaFileHelper.TryResolveLocalMediaPath(null, shot));
    }

    /// <summary>
    /// Verifies that remote URLs, missing files, and escaping relatives never resolve.
    /// </summary>
    [Fact]
    public void TryResolveLocalMediaPath_UnresolvableReferences_ReturnsNull()
    {
        Assert.Null(MediaFileHelper.TryResolveLocalMediaPath(_tempDirectory, "https://cdn.example.com/trailer.mp4"));
        Assert.Null(MediaFileHelper.TryResolveLocalMediaPath(_tempDirectory, "missing.png"));
        Assert.Null(MediaFileHelper.TryResolveLocalMediaPath(_tempDirectory, "../outside.png"));
        Assert.Null(MediaFileHelper.TryResolveLocalMediaPath(null, "shot.png"));
        Assert.Null(MediaFileHelper.TryResolveLocalMediaPath(_tempDirectory, null));
    }

    /// <summary>
    /// Verifies that an existing file outside the project directory is never resolved via a relative reference.
    /// </summary>
    [Fact]
    public void TryResolveLocalMediaPath_ExistingFileOutsideProject_ReturnsNull()
    {
        var parentDirectory = Path.GetDirectoryName(_tempDirectory)!;
        var outsidePath = Path.Combine(parentDirectory, $"outside-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(outsidePath, [9, 9, 9, 9]);
        try
        {
            Assert.Null(MediaFileHelper.TryResolveLocalMediaPath(_tempDirectory, "../" + Path.GetFileName(outsidePath)));
        }
        finally
        {
            File.Delete(outsidePath);
        }
    }

    /// <summary>
    /// Verifies that video platform pages are detected for external browser routing.
    /// </summary>
    /// <param name="value">The URL under test.</param>
    /// <param name="expected">The expected classification.</param>
    [Theory]
    [InlineData("https://www.youtube.com/watch?v=abc123", true)]
    [InlineData("https://www.youtube.com/embed/abc123", true)]
    [InlineData("https://m.youtube.com/watch?v=abc123", true)]
    [InlineData("https://www.youtube-nocookie.com/embed/abc123", true)]
    [InlineData("https://youtu.be/abc123", true)]
    [InlineData("https://vimeo.com/123456", true)]
    [InlineData("https://player.vimeo.com/video/123456", true)]
    [InlineData("https://fakeyoutube.com/watch?v=abc123", false)]
    [InlineData("https://fakeyoutube-nocookie.com/embed/abc123", false)]
    [InlineData("https://notvimeo.com/123456", false)]
    [InlineData("https://subdomain.fakevimeo.com/123456", false)]
    [InlineData("https://cdn.example.com/trailer.mp4", false)]
    [InlineData("https://drive.google.com/uc?export=download&id=abc123", false)]
    [InlineData("https://utfs.io/f/abc123", false)]
    [InlineData("https://cdn.example.com/shot.jpg", false)]
    [InlineData("file:///C:/media/trailer.mp4", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void IsEmbedVideoPageUrl_ClassifiesPlatforms(string? value, bool expected)
    {
        Assert.Equal(expected, MediaFileHelper.IsEmbedVideoPageUrl(value));
    }

    /// <summary>
    /// Verifies that remote video files are detected for in-app playback.
    /// </summary>
    /// <param name="value">The URL under test.</param>
    /// <param name="expected">The expected classification.</param>
    [Theory]
    [InlineData("https://cdn.example.com/trailer.mp4", true)]
    [InlineData("https://cdn.example.com/trailer.webm", true)]
    [InlineData("https://cdn.example.com/trailer.mov", true)]
    [InlineData("https://cdn.example.com/trailer.mkv", true)]
    [InlineData("https://dl.dropboxusercontent.com/s/abc/trailer.mp4", true)]
    [InlineData("https://cdn.example.com/trailer.mp4?token=abc", true)]
    [InlineData("https://cdn.example.com/trailer.webm?expires=123&signature=xyz", true)]
    [InlineData("https://cdn.example.com/trailer.mp4#t=10", true)]
    [InlineData("https://www.youtube.com/watch?v=abc123", false)]
    [InlineData("https://drive.google.com/uc?export=download&id=abc123", false)]
    [InlineData("https://cdn.example.com/shot.jpg", false)]
    [InlineData("C:\\media\\trailer.mp4", false)]
    [InlineData(null, false)]
    public void IsDirectVideoFileUrl_ClassifiesVideoFiles(string? value, bool expected)
    {
        Assert.Equal(expected, MediaFileHelper.IsDirectVideoFileUrl(value));
    }

    /// <summary>
    /// Verifies that extensionless hosted file URLs from GenHub media hosting are detected.
    /// </summary>
    /// <param name="value">The URL under test.</param>
    /// <param name="expected">The expected classification.</param>
    [Theory]
    [InlineData("https://drive.google.com/uc?export=download&id=abc123", true)]
    [InlineData("https://utfs.io/f/abc123", true)]
    [InlineData("https://dl.dropboxusercontent.com/s/abc/trailer.mp4", false)]
    [InlineData("https://cdn.example.com/trailer.mp4", false)]
    [InlineData("https://www.youtube.com/watch?v=abc123", false)]
    [InlineData("https://drive.google.com/drive/folders/abc123", false)]
    [InlineData(null, false)]
    public void IsExtensionlessHostedFileUrl_ClassifiesHostingUrls(string? value, bool expected)
    {
        Assert.Equal(expected, MediaFileHelper.IsExtensionlessHostedFileUrl(value));
    }

    /// <summary>
    /// Verifies that YouTube video IDs are extracted from watch, embed, shorts, live, and shortened URLs.
    /// </summary>
    /// <param name="value">The URL under test.</param>
    /// <param name="expected">The expected video ID.</param>
    [Theory]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ&t=42s", "dQw4w9WgXcQ")]
    [InlineData("https://m.youtube.com/watch?v=dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/embed/dQw4w9WgXcQ?rel=0", "dQw4w9WgXcQ")]
    [InlineData("https://www.youtube-nocookie.com/embed/dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/shorts/dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/live/dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("https://youtu.be/dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("https://youtu.be/dQw4w9WgXcQ?t=42", "dQw4w9WgXcQ")]
    public void TryGetYouTubeVideoId_YouTubeUrls_ReturnsVideoId(string? value, string expected)
    {
        Assert.Equal(expected, MediaFileHelper.TryGetYouTubeVideoId(value));
    }

    /// <summary>
    /// Verifies that non-YouTube URLs and malformed IDs resolve to no video ID.
    /// </summary>
    /// <param name="value">The URL under test.</param>
    [Theory]
    [InlineData("https://vimeo.com/123456")]
    [InlineData("https://cdn.example.com/trailer.mp4")]
    [InlineData("https://www.youtube.com/watch?v=")]
    [InlineData("https://www.youtube.com/watch")]
    [InlineData("https://www.youtube.com/playlist?list=abc123")]
    [InlineData("https://fakeyoutube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://youtu.be/")]
    [InlineData("file:///C:/media/trailer.mp4")]
    [InlineData(null)]
    [InlineData("")]
    public void TryGetYouTubeVideoId_NonYouTubeUrls_ReturnsNull(string? value)
    {
        Assert.Null(MediaFileHelper.TryGetYouTubeVideoId(value));
    }

    /// <summary>
    /// Verifies that YouTube URLs resolve to the official thumbnail image URL.
    /// </summary>
    [Fact]
    public void TryGetYouTubeThumbnailUrl_YouTubeUrl_ReturnsThumbnailUrl()
    {
        var thumbnail = MediaFileHelper.TryGetYouTubeThumbnailUrl("https://www.youtube.com/watch?v=dQw4w9WgXcQ");

        Assert.Equal("https://img.youtube.com/vi/dQw4w9WgXcQ/hqdefault.jpg", thumbnail);
    }

    /// <summary>
    /// Verifies that direct uploads resolve to no thumbnail so callers render a placeholder.
    /// </summary>
    /// <param name="value">The URL under test.</param>
    [Theory]
    [InlineData("https://cdn.example.com/trailer.mp4")]
    [InlineData("https://drive.google.com/uc?export=download&id=abc123")]
    [InlineData("https://vimeo.com/123456")]
    [InlineData(null)]
    public void TryGetYouTubeThumbnailUrl_NonYouTubeUrl_ReturnsNull(string? value)
    {
        Assert.Null(MediaFileHelper.TryGetYouTubeThumbnailUrl(value));
    }

    private string WriteTempFile(string fileName, byte[] content)
    {
        var path = Path.Combine(_tempDirectory, fileName);
        File.WriteAllBytes(path, content);
        return path;
    }
}
