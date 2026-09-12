using System;
using System.Globalization;
using System.IO;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using GenHub.Core.Constants;
using GenHub.Infrastructure.Converters;
using Xunit;

namespace GenHub.Tests.Core.Infrastructure.Converters;

/// <summary>
/// Unit tests for <see cref="StringToImageConverter"/>.
/// </summary>
public class StringToImageConverterTests
{
    private static readonly byte[] ValidPngBytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    private readonly StringToImageConverter _converter = new();
    private readonly CultureInfo _culture = CultureInfo.InvariantCulture;

    /// <summary>
    /// Tests that <see cref="StringToImageConverter.Convert"/> returns null for null input.
    /// </summary>
    [Fact]
    public void Convert_WithNullValue_ReturnsNull()
    {
        var result = _converter.Convert(null, typeof(object), null, _culture);
        Assert.Null(result);
    }

    /// <summary>
    /// Tests that <see cref="StringToImageConverter.Convert"/> returns null for empty string.
    /// </summary>
    [Fact]
    public void Convert_WithEmptyString_ReturnsNull()
    {
        var result = _converter.Convert(string.Empty, typeof(object), null, _culture);
        Assert.Null(result);
    }

    /// <summary>
    /// Tests that <see cref="StringToImageConverter.Convert"/> returns null for whitespace string.
    /// </summary>
    [Fact]
    public void Convert_WithWhitespaceString_ReturnsNull()
    {
        var result = _converter.Convert("   ", typeof(object), null, _culture);
        Assert.Null(result);
    }

    /// <summary>
    /// Tests that <see cref="StringToImageConverter.Convert"/> returns null for non-string values.
    /// </summary>
    [Fact]
    public void Convert_WithNonStringValue_ReturnsNull()
    {
        var result = _converter.Convert(42, typeof(object), null, _culture);
        Assert.Null(result);
    }

    /// <summary>
    /// Tests that <see cref="StringToImageConverter.Convert"/> returns null for HTTP URLs on memory miss.
    /// </summary>
    [Fact]
    public void Convert_WithHttpUrl_ReturnsNull()
    {
        var result = _converter.Convert(UriConstants.HttpUriScheme + "example.com/image.png", typeof(object), null, _culture);
        Assert.Null(result);
    }

    /// <summary>
    /// Tests that <see cref="StringToImageConverter.Convert"/> returns null for HTTPS URLs on memory miss.
    /// </summary>
    [Fact]
    public void Convert_WithHttpsUrl_ReturnsNull()
    {
        var result = _converter.Convert(UriConstants.HttpsUriScheme + "example.com/image.png", typeof(object), null, _culture);
        Assert.Null(result);
    }

    /// <summary>
    /// Tests that <see cref="StringToImageConverter.Convert"/> returns null for non-existing local file.
    /// </summary>
    [Fact]
    public void Convert_WithNonExistingFile_ReturnsNull()
    {
        var result = _converter.Convert("nonexistingfile.png", typeof(object), null, _culture);
        Assert.Null(result);
    }

    /// <summary>
    /// Tests that <see cref="StringToImageConverter.Convert"/> loads an existing local file into a Bitmap without locking.
    /// </summary>
    [AvaloniaFact]
    public void Convert_WithValidLocalFile_ReturnsBitmap()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"string_to_image_{Guid.NewGuid():N}.png");
        File.WriteAllBytes(tempFile, ValidPngBytes);

        try
        {
            var result = _converter.Convert(tempFile, typeof(object), null, _culture);
            Assert.NotNull(result);
            Assert.IsType<Bitmap>(result);

            // Verify file was opened without locking
            File.Delete(tempFile);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                try
                {
                    File.Delete(tempFile);
                }
                catch
                {
                    // ignore cleanup failure
                }
            }
        }
    }

    /// <summary>
    /// Tests that <see cref="StringToImageConverter.ConvertBack"/> throws <see cref="NotSupportedException"/>.
    /// </summary>
    [Fact]
    public void ConvertBack_ThrowsNotSupportedException()
    {
        Assert.Throws<NotSupportedException>(() =>
            _converter.ConvertBack(null, typeof(string), null, _culture));
    }
}
