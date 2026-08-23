using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.Formats.Tga;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Processing.Processors.Transforms;

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
    public static readonly Dictionary<string, ResamplingMode> ResamplingModes = new(StringComparer.OrdinalIgnoreCase)
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
    /// <returns>The parsed <see cref="Size"/>.</returns>
    public static Size ParseSizeParameter(object sizeObj, Size currentSize)
    {
        return sizeObj switch
        {
            int singleValue => new Size(singleValue, singleValue),
            double singleDouble => new Size((int)singleDouble, (int)singleDouble),
            int[] array when array.Length == 1 => new Size(array[0], array[0]),
            int[] array when array.Length >= 2 => new Size(array[0], array[1]),
            List<int> list when list.Count == 1 => new Size(list[0], list[0]),
            List<int> list when list.Count >= 2 => new Size(list[0], list[1]),
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
    /// Detects if an ImageSharp image has non-opaque alpha pixels.
    /// </summary>
    /// <param name="image">The image to inspect.</param>
    /// <returns><c>true</c> if the image contains alpha channels with transparency; otherwise, <c>false</c>.</returns>
    public static bool DetectAlpha(Image image)
    {
        if (image.PixelType.AlphaRepresentation == PixelAlphaRepresentation.None ||
            image.PixelType.BitsPerPixel == 24 ||
            image.PixelType.BitsPerPixel == 48)
        {
            return false;
        }

        if (image is Image<Rgba32> rgbaImage)
        {
            if (rgbaImage.DangerousTryGetSinglePixelMemory(out Memory<Rgba32> memory))
            {
                var span = memory.Span;
                for (var i = 0; i < span.Length; i++)
                {
                    if (span[i].A < 255)
                    {
                        return true;
                    }
                }

                return false;
            }

            var hasAlpha = false;
            rgbaImage.ProcessPixelRows(accessor =>
            {
                for (var y = 0; y < accessor.Height; y++)
                {
                    var pixelRow = accessor.GetRowSpan(y);
                    for (var x = 0; x < pixelRow.Length; x++)
                    {
                        if (pixelRow[x].A < 255)
                        {
                            hasAlpha = true;
                            return;
                        }
                    }
                }
            });

            return hasAlpha;
        }

        return true;
    }

    /// <summary>
    /// Resizes RGBA channels independently to preserve color information where alpha is black.
    /// </summary>
    /// <param name="image">The source image.</param>
    /// <param name="size">The target size.</param>
    /// <param name="resamplingMode">The resampling algorithm.</param>
    /// <returns>A new resized <see cref="Image"/>.</returns>
    public static Image ResizeRgbaChannelsSeparately(Image image, Size size, ResamplingMode resamplingMode)
    {
        var resampler = GetResampler(resamplingMode);

        using var rgba32Image = image.CloneAs<Rgba32>();
        using var rChannel = new Image<L8>(rgba32Image.Width, rgba32Image.Height);
        using var gChannel = new Image<L8>(rgba32Image.Width, rgba32Image.Height);
        using var bChannel = new Image<L8>(rgba32Image.Width, rgba32Image.Height);
        using var aChannel = new Image<L8>(rgba32Image.Width, rgba32Image.Height);

        rgba32Image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var pixelRow = accessor.GetRowSpan(y);
                for (var x = 0; x < pixelRow.Length; x++)
                {
                    var p = pixelRow[x];
                    rChannel[x, y] = new L8(p.R);
                    gChannel[x, y] = new L8(p.G);
                    bChannel[x, y] = new L8(p.B);
                    aChannel[x, y] = new L8(p.A);
                }
            }
        });

        var resizeOptions = new ResizeOptions
        {
            Size = size,
            Mode = ResizeMode.Stretch,
            Sampler = resampler,
        };

        rChannel.Mutate(x => x.Resize(resizeOptions));
        gChannel.Mutate(x => x.Resize(resizeOptions));
        bChannel.Mutate(x => x.Resize(resizeOptions));
        aChannel.Mutate(x => x.Resize(resizeOptions));

        var result = new Image<Rgba32>(size.Width, size.Height);
        result.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var pixelRow = accessor.GetRowSpan(y);
                for (var x = 0; x < pixelRow.Length; x++)
                {
                    pixelRow[x] = new Rgba32(rChannel[x, y].PackedValue, gChannel[x, y].PackedValue, bChannel[x, y].PackedValue, aChannel[x, y].PackedValue);
                }
            }
        });

        return result;
    }

    /// <summary>
    /// Gets the ImageSharp IResampler corresponding to a ResamplingMode.
    /// </summary>
    /// <param name="mode">The resampling mode.</param>
    /// <returns>The corresponding <see cref="IResampler"/>.</returns>
    public static IResampler GetResampler(ResamplingMode mode)
    {
        return mode switch
        {
            ResamplingMode.NearestNeighbor => KnownResamplers.NearestNeighbor,
            ResamplingMode.Box => KnownResamplers.Box,
            ResamplingMode.Bilinear => KnownResamplers.Triangle,
            ResamplingMode.Hamming => KnownResamplers.Hermite,
            ResamplingMode.Bicubic => KnownResamplers.Bicubic,
            ResamplingMode.Lanczos => KnownResamplers.Lanczos3,
            _ => KnownResamplers.Triangle,
        };
    }

    /// <summary>
    /// Applies resize and rescale parameters to an Image.
    /// </summary>
    /// <param name="image">The image to resize.</param>
    /// <param name="parameters">The conversion parameters.</param>
    /// <returns>The resized or original <see cref="Image"/>.</returns>
    public static Image ApplyResizeParameters(Image image, IDictionary<string, object>? parameters)
    {
        if (parameters == null || parameters.Count == 0)
        {
            return image;
        }

        var size = image.Size;
        var hasResize = false;

        if (parameters.TryGetValue("resize", out var resizeObj))
        {
            size = ParseSizeParameter(resizeObj, size);
            hasResize = true;
        }

        if (parameters.TryGetValue("rescale", out var rescaleObj))
        {
            var scale = ParseScaleParameter(rescaleObj);
            size = new Size((int)(size.Width * scale.Width), (int)(size.Height * scale.Height));
            hasResize = true;
        }

        if (!hasResize || size == image.Size)
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
            return ResizeRgbaChannelsSeparately(image, size, resamplingMode);
        }

        var resampler = GetResampler(resamplingMode);
        image.Mutate(x => x.Resize(new ResizeOptions
        {
            Size = size,
            Mode = ResizeMode.Stretch,
            Sampler = resampler,
        }));

        return image;
    }

    /// <summary>
    /// Saves an ImageSharp image to target path with proper format encoders.
    /// </summary>
    /// <param name="image">The image to save.</param>
    /// <param name="targetPath">The destination file path.</param>
    /// <param name="targetExt">The file extension.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous save operation.</returns>
    public static async Task SaveImageToTargetAsync(Image image, string targetPath, string targetExt, CancellationToken cancellationToken = default)
    {
        switch (targetExt)
        {
            case ".bmp":
                await image.SaveAsBmpAsync(targetPath, new BmpEncoder(), cancellationToken).ConfigureAwait(false);
                break;
            case ".tga":
                await image.SaveAsTgaAsync(
                    targetPath,
                    new TgaEncoder
                    {
                        BitsPerPixel = TgaBitsPerPixel.Pixel32,
                        Compression = TgaCompression.None,
                    },
                    cancellationToken).ConfigureAwait(false);
                break;
            default:
                await image.SaveAsync(targetPath, cancellationToken).ConfigureAwait(false);
                break;
        }
    }
}
