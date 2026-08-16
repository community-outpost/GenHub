using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BCnEncoder.Encoder;
using BCnEncoder.Shared;
using GenHub.Core.Interfaces.Tools.ModBuilder;
using ImageMagick;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.Formats.Tga;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace GenHub.Features.Tools.ModBuilder.Services;

/// <summary>
/// Implementation of image conversion service for ModBuilder.
/// Handles PSD, TGA, TIFF, DDS, and BMP conversions with advanced features.
/// </summary>
public class ImageConversionService : IImageConversionService
{
    // Supported resampling algorithms
    private static readonly Dictionary<string, ResamplingMode> ResamplingModes = new(StringComparer.OrdinalIgnoreCase)
    {
        { "nearest", ResamplingMode.NearestNeighbor },
        { "box", ResamplingMode.Box },
        { "bilinear", ResamplingMode.Bilinear },
        { "hamming", ResamplingMode.Hamming },
        { "bicubic", ResamplingMode.Bicubic },
        { "lanczos", ResamplingMode.Lanczos },
    };

    private readonly ILogger<ImageConversionService> _logger;

    public ImageConversionService(ILogger<ImageConversionService> logger)
    {
        _logger = logger;
    }

    public async Task<bool> ConvertImageAsync(
        string sourcePath,
        string targetPath,
        IDictionary<string, object>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(sourcePath))
            {
                _logger.LogError("Source file does not exist: {SourcePath}", sourcePath);
                return false;
            }

            var sourceExt = Path.GetExtension(sourcePath).ToLowerInvariant();
            var targetExt = Path.GetExtension(targetPath).ToLowerInvariant();

            // Create target directory if needed
            var targetDir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            return await Task.Run(async () =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Route to appropriate conversion method
                return (sourceExt, targetExt) switch
                {
                    (".psd", ".bmp") or (".psd", ".tga") or (".psd", ".dds") =>
                        await ConvertPsdAsync(sourcePath, targetPath, parameters, cancellationToken),

                    (".tga", ".bmp") or (".tga", ".dds") =>
                        await ConvertTgaAsync(sourcePath, targetPath, parameters, cancellationToken),

                    (".tiff", ".bmp") or (".tiff", ".tga") or (".tiff", ".dds") or
                    (".tif", ".bmp") or (".tif", ".tga") or (".tif", ".dds") =>
                        await ConvertTiffAsync(sourcePath, targetPath, parameters, cancellationToken),

                    (".dds", ".dds") =>
                        await ConvertDdsAsync(sourcePath, targetPath, parameters, cancellationToken),

                    _ => await ConvertGenericAsync(sourcePath, targetPath, parameters, cancellationToken)
                };
            }, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Image conversion cancelled: {SourcePath}", sourcePath);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to convert image from {SourcePath} to {TargetPath}", sourcePath, targetPath);
            return false;
        }
    }

    public async Task<bool> HasAlphaChannelAsync(string imagePath, CancellationToken cancellationToken = default)
    {
        try
        {
            var ext = Path.GetExtension(imagePath).ToLowerInvariant();

            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (ext == ".psd")
                {
                    return HasAlphaChannelPsd(imagePath);
                }

                using var image = Image.Load(imagePath);
                return HasAlphaChannel(image);
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to detect alpha channel in {ImagePath}", imagePath);
            return false;
        }
    }

    public async Task<string> GetRecommendedDxtFormatAsync(string imagePath, CancellationToken cancellationToken = default)
    {
        var hasAlpha = await HasAlphaChannelAsync(imagePath, cancellationToken);
        return hasAlpha ? "DXT5" : "DXT1";
    }

    /// <summary>
    /// Converts PSD files with support for RGB and RGBA modes, including multi-alpha compositing.
    /// This is the most complex conversion due to PSD's multi-channel alpha support.
    /// </summary>
    private async Task<bool> ConvertPsdAsync(
        string sourcePath,
        string targetPath,
        IDictionary<string, object>? parameters,
        CancellationToken cancellationToken)
    {
        try
        {
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                using var image = new MagickImage(sourcePath);

                // Simple RGB case (3 channels or less)
                if (image.ChannelCount <= 3)
                {
                    image.Write(targetPath);
                    return true;
                }

                // Multi-alpha compositing for images with more than 3 channels
                // Extract RGB channels
                var channels = image.Separate().ToList();
                var r = channels[0];
                var g = channels[1];
                var b = channels[2];

                // Composite all alpha channels
                var alpha = new MagickImage(MagickColors.White, image.Width, image.Height);
                for (int i = 3; i < image.ChannelCount; i++)
                {
                    var alphaChannel = channels[i];
                    alpha.Composite(alphaChannel, CompositeOperator.Multiply);
                }

                // Merge RGBA
                var result = new MagickImageCollection { r, g, b, alpha };
                var merged = result.Combine(ColorSpace.sRGB);
                merged.Write(targetPath);

                // Dispose resources
                foreach (var channel in channels)
                {
                    channel.Dispose();
                }

                alpha.Dispose();
                merged.Dispose();

                return true;
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to convert PSD: {SourcePath}", sourcePath);
            return false;
        }
    }

    /// <summary>
    /// Builds an image from PSD with multi-alpha compositing.
    ///
    /// CRITICAL ALGORITHM (from Python implementation):
    /// For RGBA PSD (>3 channels):
    /// 1. Composite with psd.composite(color=0.0, alpha=1.0)
    /// 2. Extract R, G, B channels separately
    /// 3. Multi-Alpha Compositing: Merge ALL alpha channels (channels 3+)
    ///    - Create white and black base images
    ///    - Iterate through each alpha channel
    ///    - Use Image.composite(an, black, a) to blend alphas
    /// 4. Final output: RGBA image with merged alpha
    /// </summary>
    private Image<Rgba32> BuildImageFromPsd(string sourcePath)
    {
        // This method requires a PSD parsing library that supports:
        // - Channel extraction
        // - Multi-alpha compositing
        // - RGB color mode verification
        throw new NotImplementedException(
            "PSD multi-alpha compositing requires a specialized PSD library. " +
            "Consider using Magick.NET or implementing a custom PSD parser.");
    }

    private bool HasAlphaChannelPsd(string sourcePath)
    {
        try
        {
            using var image = new MagickImage(sourcePath);

            // PSD has alpha if it has more than 3 channels (R, G, B)
            return image.ChannelCount > 3;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to detect alpha channel in PSD: {SourcePath}", sourcePath);
            return false;
        }
    }

    private async Task<bool> ConvertTgaAsync(
        string sourcePath,
        string targetPath,
        IDictionary<string, object>? parameters,
        CancellationToken cancellationToken)
    {
        return await Task.Run(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var image = Image.Load(sourcePath);
            var resizedImage = ApplyResizeParameters(image, parameters);

            cancellationToken.ThrowIfCancellationRequested();

            var targetExt = Path.GetExtension(targetPath).ToLowerInvariant();
            switch (targetExt)
            {
                case ".bmp":
                    resizedImage.SaveAsBmp(targetPath, new BmpEncoder());
                    break;
                case ".tga":
                    resizedImage.SaveAsTga(targetPath, new TgaEncoder
                    {
                        BitsPerPixel = TgaBitsPerPixel.Pixel32,
                        Compression = TgaCompression.None
                    });
                    break;
                case ".dds":
                    // Save to temp file first, then convert to DDS
                    var tempPath = Path.GetTempFileName();
                    try
                    {
                        resizedImage.SaveAsTga(tempPath);
                        return await ConvertToDdsAsync(tempPath, targetPath, parameters, cancellationToken).ConfigureAwait(false);
                    }
                    finally
                    {
                        if (File.Exists(tempPath))
                            File.Delete(tempPath);
                    }

                default:
                    resizedImage.Save(targetPath);
                    break;
            }

            return true;
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> ConvertTiffAsync(
        string sourcePath,
        string targetPath,
        IDictionary<string, object>? parameters,
        CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var image = Image.Load(sourcePath);

            // TIFF supports RGB, RGBA, RGBX modes
            // Note: No composite support, single alpha channel only, no transparent background
            if (image.PixelType.BitsPerPixel < 24)
            {
                _logger.LogError("TIFF image has unsupported color mode: {SourcePath}", sourcePath);
                return false;
            }

            var resizedImage = ApplyResizeParameters(image, parameters);

            cancellationToken.ThrowIfCancellationRequested();

            var targetExt = Path.GetExtension(targetPath).ToLowerInvariant();
            switch (targetExt)
            {
                case ".bmp":
                    resizedImage.SaveAsBmp(targetPath, new BmpEncoder());
                    break;
                case ".tga":
                    resizedImage.SaveAsTga(targetPath, new TgaEncoder
                    {
                        BitsPerPixel = TgaBitsPerPixel.Pixel32,
                        Compression = TgaCompression.None
                    });
                    break;
                case ".dds":
                    _logger.LogWarning("DDS encoding not yet implemented. Use external tool.");
                    return false;
                default:
                    resizedImage.Save(targetPath);
                    break;
            }

            return true;
        }, cancellationToken);
    }

    private async Task<bool> ConvertDdsAsync(
        string sourcePath,
        string targetPath,
        IDictionary<string, object>? parameters,
        CancellationToken cancellationToken)
    {
        // DDS to DDS re-export (format conversion, e.g., DXT5 to DXT1)
        return await ConvertToDdsAsync(sourcePath, targetPath, parameters, cancellationToken);
    }

    /// <summary>
    /// Converts any image format to DDS using BCnEncoder.NET.
    /// Auto-detects DXT1 (no alpha) or DXT5 (with alpha) format.
    /// </summary>
    private async Task<bool> ConvertToDdsAsync(
        string sourcePath,
        string targetPath,
        IDictionary<string, object>? parameters,
        CancellationToken cancellationToken)
    {
        try
        {
            using var image = await Image.LoadAsync<Rgba32>(sourcePath, cancellationToken);

            // Convert ImageSharp image to BCnEncoder format
            var pixels = new BCnEncoder.Shared.ColorRgba32[image.Width * image.Height];
            int index = 0;

            image.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < accessor.Height; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (int x = 0; x < row.Length; x++)
                    {
                        var pixel = row[x];
                        pixels[index++] = new BCnEncoder.Shared.ColorRgba32(pixel.R, pixel.G, pixel.B, pixel.A);
                    }
                }
            });

            var encoder = new BcEncoder();
            encoder.OutputOptions.GenerateMipMaps = true;
            encoder.OutputOptions.Quality = CompressionQuality.Balanced;

            // Auto-detect format based on alpha
            encoder.OutputOptions.Format = await HasAlphaChannelAsync(sourcePath, cancellationToken)
                ? CompressionFormat.Bc3 // DXT5 with alpha
                : CompressionFormat.Bc1; // DXT1 no alpha

            await using var output = File.Create(targetPath);

            // BCnEncoder expects raw RGBA data
            var rawData = new byte[pixels.Length * 4];
            for (int i = 0; i < pixels.Length; i++)
            {
                rawData[i * 4] = pixels[i].r;
                rawData[i * 4 + 1] = pixels[i].g;
                rawData[i * 4 + 2] = pixels[i].b;
                rawData[i * 4 + 3] = pixels[i].a;
            }

            await encoder.EncodeToStreamAsync(
                rawData,
                image.Width,
                image.Height,
                BCnEncoder.Encoder.PixelFormat.Rgba32,
                output).ConfigureAwait(false);

            _logger.LogInformation("Converted {Source} to DDS format {Format}", sourcePath, encoder.OutputOptions.Format);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to convert to DDS: {SourcePath}", sourcePath);
            return false;
        }
    }

    private async Task<bool> ConvertGenericAsync(
        string sourcePath,
        string targetPath,
        IDictionary<string, object>? parameters,
        CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var image = Image.Load(sourcePath);
            var resizedImage = ApplyResizeParameters(image, parameters);

            cancellationToken.ThrowIfCancellationRequested();

            resizedImage.Save(targetPath);
            return true;
        }, cancellationToken);
    }

    /// <summary>
    /// Applies resize/rescale parameters to an image.
    /// For RGBA images, splits channels and resizes independently to prevent color loss.
    /// </summary>
    private Image ApplyResizeParameters(Image image, IDictionary<string, object>? parameters)
    {
        if (parameters == null || parameters.Count == 0)
        {
            return image;
        }

        var size = image.Size;
        var hasResize = false;

        // Parse resize parameter (absolute size)
        if (parameters.TryGetValue("resize", out var resizeObj))
        {
            size = ParseSizeParameter(resizeObj, size);
            hasResize = true;
        }

        // Parse rescale parameter (scale factor)
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

        // Parse resampling mode
        var resamplingMode = ResamplingMode.Bilinear; // Default
        if (parameters.TryGetValue("resampling", out var resamplingObj) && resamplingObj is string resamplingStr)
        {
            if (ResamplingModes.TryGetValue(resamplingStr, out var mode))
            {
                resamplingMode = mode;
            }
        }

        // For RGBA images, resize channels separately to prevent color loss where alpha is black
        if (HasAlphaChannel(image))
        {
            return ResizeRgbaChannelsSeparately(image, size, resamplingMode);
        }

        // Standard resize for non-RGBA images
        var resampler = resamplingMode switch
        {
            ResamplingMode.NearestNeighbor => KnownResamplers.NearestNeighbor,
            ResamplingMode.Box => KnownResamplers.Box,
            ResamplingMode.Bilinear => KnownResamplers.Triangle,
            ResamplingMode.Hamming => KnownResamplers.Hermite,
            ResamplingMode.Bicubic => KnownResamplers.Bicubic,
            ResamplingMode.Lanczos => KnownResamplers.Lanczos3,
            _ => KnownResamplers.Triangle,
        };

        image.Mutate(x => x.Resize(new ResizeOptions
        {
            Size = size,
            Mode = ResizeMode.Stretch,
            Sampler = resampler,
        }));

        return image;
    }

    /// <summary>
    /// Resizes RGBA image by splitting channels and resizing independently.
    /// This prevents color information loss where alpha is black.
    /// </summary>
    private Image ResizeRgbaChannelsSeparately(Image image, Size size, ResamplingMode resamplingMode)
    {
        var resampler = resamplingMode switch
        {
            ResamplingMode.NearestNeighbor => KnownResamplers.NearestNeighbor,
            ResamplingMode.Box => KnownResamplers.Box,
            ResamplingMode.Bilinear => KnownResamplers.Triangle,
            ResamplingMode.Hamming => KnownResamplers.Hermite,
            ResamplingMode.Bicubic => KnownResamplers.Bicubic,
            ResamplingMode.Lanczos => KnownResamplers.Lanczos3,
            _ => KnownResamplers.Triangle,
        };

        // Convert to Rgba32 for channel manipulation
        var rgba32Image = image.CloneAs<Rgba32>();

        // Extract channels
        using var rChannel = new Image<L8>(rgba32Image.Width, rgba32Image.Height);
        using var gChannel = new Image<L8>(rgba32Image.Width, rgba32Image.Height);
        using var bChannel = new Image<L8>(rgba32Image.Width, rgba32Image.Height);
        using var aChannel = new Image<L8>(rgba32Image.Width, rgba32Image.Height);

        // Split RGBA into separate channels using DangerousTryGetSinglePixelMemory (50x faster than direct pixel access)
        if (rgba32Image.DangerousTryGetSinglePixelMemory(out Memory<Rgba32> rgbaMemory) &&
            rChannel.DangerousTryGetSinglePixelMemory(out Memory<L8> rMemory) &&
            gChannel.DangerousTryGetSinglePixelMemory(out Memory<L8> gMemory) &&
            bChannel.DangerousTryGetSinglePixelMemory(out Memory<L8> bMemory) &&
            aChannel.DangerousTryGetSinglePixelMemory(out Memory<L8> aMemory))
        {
            Span<Rgba32> rgbaSpan = rgbaMemory.Span;
            Span<L8> rSpan = rMemory.Span;
            Span<L8> gSpan = gMemory.Span;
            Span<L8> bSpan = bMemory.Span;
            Span<L8> aSpan = aMemory.Span;

            for (int i = 0; i < rgbaSpan.Length; i++)
            {
                var pixel = rgbaSpan[i];
                rSpan[i] = new L8(pixel.R);
                gSpan[i] = new L8(pixel.G);
                bSpan[i] = new L8(pixel.B);
                aSpan[i] = new L8(pixel.A);
            }
        }
        else
        {
            // Fallback to row-by-row processing
            for (int y = 0; y < rgba32Image.Height; y++)
            {
                for (int x = 0; x < rgba32Image.Width; x++)
                {
                    var pixel = rgba32Image[x, y];
                    rChannel[x, y] = new L8(pixel.R);
                    gChannel[x, y] = new L8(pixel.G);
                    bChannel[x, y] = new L8(pixel.B);
                    aChannel[x, y] = new L8(pixel.A);
                }
            }
        }

        // Resize each channel independently
        rChannel.Mutate(x => x.Resize(new ResizeOptions { Size = size, Mode = ResizeMode.Stretch, Sampler = resampler }));
        gChannel.Mutate(x => x.Resize(new ResizeOptions { Size = size, Mode = ResizeMode.Stretch, Sampler = resampler }));
        bChannel.Mutate(x => x.Resize(new ResizeOptions { Size = size, Mode = ResizeMode.Stretch, Sampler = resampler }));
        aChannel.Mutate(x => x.Resize(new ResizeOptions { Size = size, Mode = ResizeMode.Stretch, Sampler = resampler }));

        // Merge channels back using DangerousTryGetSinglePixelMemory (50x faster than direct pixel access)
        var result = new Image<Rgba32>(size.Width, size.Height);
        if (result.DangerousTryGetSinglePixelMemory(out Memory<Rgba32> resultMemory) &&
            rChannel.DangerousTryGetSinglePixelMemory(out Memory<L8> rResizedMemory) &&
            gChannel.DangerousTryGetSinglePixelMemory(out Memory<L8> gResizedMemory) &&
            bChannel.DangerousTryGetSinglePixelMemory(out Memory<L8> bResizedMemory) &&
            aChannel.DangerousTryGetSinglePixelMemory(out Memory<L8> aResizedMemory))
        {
            Span<Rgba32> resultSpan = resultMemory.Span;
            Span<L8> rSpan = rResizedMemory.Span;
            Span<L8> gSpan = gResizedMemory.Span;
            Span<L8> bSpan = bResizedMemory.Span;
            Span<L8> aSpan = aResizedMemory.Span;

            for (int i = 0; i < resultSpan.Length; i++)
            {
                resultSpan[i] = new Rgba32(rSpan[i].PackedValue, gSpan[i].PackedValue, bSpan[i].PackedValue, aSpan[i].PackedValue);
            }
        }
        else
        {
            // Fallback to row-by-row processing
            for (int y = 0; y < result.Height; y++)
            {
                for (int x = 0; x < result.Width; x++)
                {
                    result[x, y] = new Rgba32(
                        rChannel[x, y].PackedValue,
                        gChannel[x, y].PackedValue,
                        bChannel[x, y].PackedValue,
                        aChannel[x, y].PackedValue);
                }
            }
        }

        return result;
    }

    private Size ParseSizeParameter(object sizeObj, Size currentSize)
    {
        return sizeObj switch
        {
            int singleValue => new Size(singleValue, singleValue),
            double singleDouble => new Size((int)singleDouble, (int)singleDouble),
            int[] array when array.Length == 1 => new Size(array[0], array[0]),
            int[] array when array.Length >= 2 => new Size(array[0], array[1]),
            List<int> list when list.Count == 1 => new Size(list[0], list[0]),
            List<int> list when list.Count >= 2 => new Size(list[0], list[1]),
            _ => currentSize
        };
    }

    private (double Width, double Height) ParseScaleParameter(object scaleObj)
    {
        return scaleObj switch
        {
            double singleValue => (singleValue, singleValue),
            int singleInt => (singleInt, singleInt),
            double[] array when array.Length == 1 => (array[0], array[0]),
            double[] array when array.Length >= 2 => (array[0], array[1]),
            List<double> list when list.Count == 1 => (list[0], list[0]),
            List<double> list when list.Count >= 2 => (list[0], list[1]),
            _ => (1.0, 1.0)
        };
    }

    private static bool HasAlphaChannel(Image image)
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
                for (int i = 0; i < span.Length; i++)
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
                for (int y = 0; y < accessor.Height; y++)
                {
                    var pixelRow = accessor.GetRowSpan(y);
                    for (int x = 0; x < pixelRow.Length; x++)
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

    private enum ResamplingMode
    {
        NearestNeighbor,
        Box,
        Bilinear,
        Hamming,
        Bicubic,
        Lanczos,
    }
}
