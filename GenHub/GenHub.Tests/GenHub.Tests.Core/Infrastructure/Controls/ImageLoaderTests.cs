using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using GenHub.Infrastructure.Controls;
using GenHub.Infrastructure.Services;
using Xunit;

namespace GenHub.Tests.Core.Infrastructure.Controls;

/// <summary>
/// Headless UI tests for <see cref="ImageLoader"/> attached properties and lifecycle behavior.
/// </summary>
public class ImageLoaderTests
{
    private static readonly byte[] ValidPngBytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    /// <summary>
    /// Verifies that Source and Placeholder attached properties can be set and retrieved.
    /// </summary>
    [AvaloniaFact]
    public void ImageLoader_SourceAndPlaceholderProperties_CanBeSetAndRetrieved()
    {
        var image = new Image();
        using var ms = new MemoryStream(ValidPngBytes);
        var placeholder = new Bitmap(ms);

        ImageLoader.SetPlaceholder(image, placeholder);
        ImageLoader.SetSource(image, "http://127.0.0.1/test.png");

        Assert.Equal(placeholder, ImageLoader.GetPlaceholder(image));
        Assert.Equal("http://127.0.0.1/test.png", ImageLoader.GetSource(image));
    }

    /// <summary>
    /// Verifies that changing the URL immediately clears the previous image and sets the placeholder.
    /// </summary>
    [AvaloniaFact]
    public void ImageLoader_UrlChange_ImmediatelySetsPlaceholderAndClearsStaleImage()
    {
        var image = new Image();
        using var ms1 = new MemoryStream(ValidPngBytes);
        using var ms2 = new MemoryStream(ValidPngBytes);
        var initialBitmap = new Bitmap(ms1);
        var placeholderBitmap = new Bitmap(ms2);

        image.Source = initialBitmap;
        ImageLoader.SetPlaceholder(image, placeholderBitmap);

        // Changing the URL should immediately replace initialBitmap with the placeholder
        ImageLoader.SetSource(image, "http://127.0.0.1/new_image.png");

        Assert.Equal(placeholderBitmap, image.Source);
    }

    /// <summary>
    /// Verifies that setting a null or empty URL clears the image source.
    /// </summary>
    [AvaloniaFact]
    public void ImageLoader_NullOrEmptyUrl_ClearsSourceToPlaceholderOrNull()
    {
        var image = new Image();
        using var ms = new MemoryStream(ValidPngBytes);
        var initialBitmap = new Bitmap(ms);

        image.Source = initialBitmap;
        ImageLoader.SetSource(image, string.Empty);

        Assert.Null(image.Source);
    }

    /// <summary>
    /// Verifies that a local file path can be loaded into an Image control asynchronously.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [AvaloniaFact]
    public async Task ImageLoader_LocalFile_LoadsBitmapSuccessfullyAsync()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"imageloader_test_{Guid.NewGuid():N}.png");
        await File.WriteAllBytesAsync(tempFile, ValidPngBytes);

        try
        {
            var image = new Image();
            ImageLoader.SetSource(image, tempFile);

            // Allow async load to complete
            for (int i = 0; i < 40 && image.Source == null; i++)
            {
                await Task.Delay(50);
            }

            Assert.NotNull(image.Source);
            Assert.IsAssignableFrom<Bitmap>(image.Source);
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
    /// Verifies that when a load fails, the image source resets to the placeholder.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [AvaloniaFact]
    public async Task ImageLoader_FailedLoad_ResetsToPlaceholderAsync()
    {
        var image = new Image();
        using var ms = new MemoryStream(ValidPngBytes);
        var placeholder = new Bitmap(ms);
        ImageLoader.SetPlaceholder(image, placeholder);

        // Attempt to load a non-existent local file or invalid remote url
        ImageLoader.SetSource(image, "https://127.0.0.1/will_fail.png");

        await Task.Delay(100);

        // Failed load should retain or reset to placeholder, not stale image
        Assert.Equal(placeholder, image.Source);
    }
}
