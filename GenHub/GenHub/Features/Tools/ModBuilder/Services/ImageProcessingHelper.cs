using GenHub.Infrastructure.Imaging;
using ImageMagick;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.ModBuilder.Services;

/// <summary>
/// Supported resampling modes for image operations.
/// </summary>
internal enum ResamplingMode
{
    /// <summary>
    /// Nearest neighbor resampling.
    /// </summary>
    NearestNeighbor,

    /// <summary>
    /// Box filter resampling.
    /// </summary>
    Box,

    /// <summary>
    /// Bilinear triangle filter resampling.
    /// </summary>
    Bilinear,

    /// <summary>
    /// Hamming hermite filter resampling.
    /// </summary>
    Hamming,

    /// <summary>
    /// Bicubic filter resampling.
    /// </summary>
    Bicubic,

    /// <summary>
    /// Lanczos3 windowed sinc filter resampling.
    /// </summary>
    Lanczos,
}

/// <summary>
/// Shared helper utility for image resizing, channel splitting, parameter parsing, and format persistence.
/// </summary>
internal static class ImageProcessingHelper
{
    public static readonly IReadOnlyDictionary<string, ResamplingMode> ResamplingModes = new Dictionary<string, ResamplingMode>(StringComparer.OrdinalIgnoreCase)
    {
        { "nearest", ResamplingMode.NearestNeighbor },
        { "box", ResamplingMode.Box },
        { "bilinear", ResamplingMode.Bilinear },
        { "hamming", ResamplingMode.Hamming },
        { "bicubic", ResamplingMode.Bicubic },
        { "lanczos", ResamplingMode.Lanczos },
    };

    /// <summary>
    /// Parses size parameters from diverse input formats.
    /// </summary>
    /// <param name="sizeObj">The size parameter object (int, double, array, or list).</param>
    /// <param name="currentSize">The fallback size if parsing fails.</param>
    /// <returns>The parsed width and height.</returns>
    public static (int Width, int Height) ParseSizeParameter(object sizeObj, (int Width, int Height) currentSize)
    {
        return sizeObj switch
        {
            int singleValue => (singleValue, singleValue),
            double singleDouble => ((int)singleDouble, (int)singleDouble),
            int[] array when array.Length == 1 => (array[0], array[0]),
            int[] array when array.Length >= 2 => (array[0], array[1]),
            List<int> list when list.Count == 1 => (list[0], list[0]),
            List<int> list when list.Count >= 2 => (list[0], list[1]),
            _ => currentSize,
        };
    }

    /// <summary>
    /// Parses scale parameters from diverse input formats.
    /// </summary>
    /// <param name="scaleObj">The scale parameter object.</param>
    /// <returns>A tuple containing width and height scale multipliers.</returns>
    public static (double Width, double Height) ParseScaleParameter(object scaleObj)
    {
        return scaleObj switch
        {
            double singleValue => (singleValue, singleValue),
            int singleInt => (singleInt, singleInt),
            double[] array when array.Length == 1 => (array[0], array[0]),
            double[] array when array.Length >= 2 => (array[0], array[1]),
            List<double> list when list.Count == 1 => (list[0], list[0]),
            List<double> list when list.Count >= 2 => (list[0], list[1]),
            _ => (1.0, 1.0),
        };
    }

    /// <summary>
    /// Detects if an image has non-opaque alpha pixels.
    /// </summary>
    /// <param name="image">The image to inspect.</param>
    /// <returns><c>true</c> if the image contains alpha channels with transparency; otherwise, <c>false</c>.</returns>
    public static bool DetectAlpha(IMagickImage<ushort> image)
    {
        if (!image.HasAlpha)
        {
            return false;
        }

        var alpha = image.Statistics().GetChannel(PixelChannel.Alpha);
        return alpha != null && alpha.Minimum < Quantum.Max;
    }

    /// <summary>
    /// Resizes RGBA channels independently to preserve color information where alpha is black.
    /// </summary>
    /// <param name="image">The source image.</param>
    /// <param name="width">The target width.</param>
    /// <param name="height">The target height.</param>
    /// <param name="resamplingMode">The resampling algorithm.</param>
    /// <returns>A new resized image. The caller owns it.</returns>
    public static IMagickImage<ushort> ResizeRgbaChannelsSeparately(IMagickImage<ushort> image, int width, int height, ResamplingMode resamplingMode)
    {
        using var rgba = image.Clone();
        rgba.ColorSpace = ColorSpace.sRGB;
        rgba.ColorType = ColorType.TrueColorAlpha;
        using var channels = new MagickImageCollection(rgba.Separate(Channels.RGBA));
        foreach (var channel in channels)
        {
            Resize(channel, width, height, resamplingMode);
        }

        var result = channels.Combine(ColorSpace.sRGB);
        result.Alpha(AlphaOption.On);
        return result;
    }

    /// <summary>
    /// Gets the ImageMagick filter corresponding to a ResamplingMode.
    /// </summary>
    /// <param name="mode">The resampling mode.</param>
    /// <returns>The corresponding <see cref="FilterType"/>.</returns>
    public static FilterType GetFilter(ResamplingMode mode)
    {
        return mode switch
        {
            ResamplingMode.NearestNeighbor => FilterType.Point,
            ResamplingMode.Box => FilterType.Box,
            ResamplingMode.Bilinear => FilterType.Triangle,
            ResamplingMode.Hamming => FilterType.Hermite,
            ResamplingMode.Bicubic => FilterType.Catrom,
            ResamplingMode.Lanczos => FilterType.Lanczos,
            _ => FilterType.Triangle,
        };
    }

    /// <summary>
    /// Applies resize and rescale parameters to an image.
    /// </summary>
    /// <param name="image">The image to resize.</param>
    /// <param name="parameters">The conversion parameters.</param>
    /// <returns>The resized image, which is either <paramref name="image"/> resized in place or a new image the caller owns.</returns>
    public static IMagickImage<ushort> ApplyResizeParameters(IMagickImage<ushort> image, IDictionary<string, object>? parameters)
    {
        if (parameters == null || parameters.Count == 0)
        {
            return image;
        }

        (int Width, int Height) originalSize = ((int)image.Width, (int)image.Height);
        var size = originalSize;
        var hasResize = false;

        if (parameters.TryGetValue("resize", out var resizeObj))
        {
            size = ParseSizeParameter(resizeObj, size);
            hasResize = true;
        }

        if (parameters.TryGetValue("rescale", out var rescaleObj))
        {
            var scale = ParseScaleParameter(rescaleObj);
            size = ((int)(size.Width * scale.Width), (int)(size.Height * scale.Height));
            hasResize = true;
        }

        if (!hasResize || size == originalSize)
        {
            return image;
        }

        var resamplingMode = ResamplingMode.Bilinear;
        if (parameters.TryGetValue("resampling", out var resamplingObj) &&
            resamplingObj is string resamplingStr &&
            ResamplingModes.TryGetValue(resamplingStr, out var mode))
        {
            resamplingMode = mode;
        }

        if (DetectAlpha(image))
        {
            return ResizeRgbaChannelsSeparately(image, size.Width, size.Height, resamplingMode);
        }

        Resize(image, size.Width, size.Height, resamplingMode);
        return image;
    }

    /// <summary>
    /// Saves an image to target path with the encoder settings the game expects.
    /// TGA is written as uncompressed 32-bit. BMP is written as 24-bit truecolor, except that a source
    /// BMP with an alpha channel stays 32-bit.
    /// </summary>
    /// <param name="image">The image to save. TGA output modifies it in place.</param>
    /// <param name="targetPath">The destination file path.</param>
    /// <param name="targetExt">The file extension.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous save operation.</returns>
    public static async Task SaveImageToTargetAsync(IMagickImage<ushort> image, string targetPath, string targetExt, CancellationToken cancellationToken = default)
    {
        switch (targetExt)
        {
            case ".bmp":
                var keepAlpha = image.HasAlpha && image.Format is MagickFormat.Bmp or MagickFormat.Bmp2 or MagickFormat.Bmp3;
                image.ColorSpace = ColorSpace.sRGB;
                image.ColorType = keepAlpha ? ColorType.TrueColorAlpha : ColorType.TrueColor;
                if (!keepAlpha)
                {
                    image.Alpha(AlphaOption.Off);
                }

                await image.WriteAsync(targetPath, keepAlpha ? MagickFormat.Bmp : MagickFormat.Bmp3, cancellationToken).ConfigureAwait(false);
                break;
            case ".tga":
                TgaWriter.Write32BitUncompressed(image, targetPath);
                break;
            default:
                await image.WriteAsync(targetPath, cancellationToken).ConfigureAwait(false);
                break;
        }
    }

    private static void Resize(IMagickImage<ushort> image, int width, int height, ResamplingMode resamplingMode)
    {
        image.FilterType = GetFilter(resamplingMode);
        image.Resize(new MagickGeometry((uint)width, (uint)height) { IgnoreAspectRatio = true });
    }
}
