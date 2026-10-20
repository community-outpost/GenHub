using BCnEncoder.Encoder;
using BCnEncoder.Shared;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Tools.ModBuilder;
using GenHub.Infrastructure.Imaging;
using ImageMagick;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.ModBuilder.Services;

/// <summary>
/// Implementation of image conversion service for ModBuilder.
/// Handles PSD, TGA, TIFF, DDS, and BMP conversions with advanced features.
/// </summary>
public class ImageConversionService(ILogger<ImageConversionService> logger)
    : ImageConversionServiceBase(logger)
{
    /// <inheritdoc />
    protected override async Task<bool> ConvertCoreAsync(
        string sourcePath,
        string targetPath,
        string sourceExt,
        string targetExt,
        IDictionary<string, object>? parameters,
        CancellationToken cancellationToken)
    {
        return sourceExt switch
        {
            ".psd" => await ConvertPsdAsync(sourcePath, targetPath, cancellationToken),
            ".tga" => await ConvertTgaAsync(sourcePath, targetPath, parameters, cancellationToken),
            ".tif" or ".tiff" => await ConvertTiffAsync(sourcePath, targetPath, parameters, cancellationToken),
            ".dds" => await ConvertDdsAsync(sourcePath, targetPath, parameters, cancellationToken),
            _ => await ConvertGenericAsync(sourcePath, targetPath, parameters, cancellationToken),
        };
    }

    /// <inheritdoc />
    public override async Task<bool> HasAlphaChannelAsync(string imagePath, CancellationToken cancellationToken = default)
    {
        try
        {
            var ext = Path.GetExtension(imagePath).ToLowerInvariant();

            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (ext == ".dds")
                {
                    using var magickImage = new MagickImage(imagePath);
                    return magickImage.HasAlpha;
                }

                if (ext == ".psd")
                {
                    return HasAlphaChannelPsd(imagePath);
                }

                using var image = new MagickImage(imagePath);
                return ImageProcessingHelper.DetectAlpha(image);
            }, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to check alpha channel for: {ImagePath}", imagePath);
            return false;
        }
    }

    /// <summary>
    /// Converts PSD files with support for RGB and RGBA modes, including multi-alpha compositing.
    /// This is the most complex conversion due to PSD's multi-channel alpha support.
    /// </summary>
    private async Task<bool> ConvertPsdAsync(
        string sourcePath,
        string targetPath,
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
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to convert PSD: {SourcePath}", sourcePath);
            return false;
        }
    }

    private bool HasAlphaChannelPsd(string sourcePath)
    {
        try
        {
            using var image = new MagickImage(sourcePath);

            // PSD has alpha if it has more than 3 channels (R, G, B)
            return image.ChannelCount > 3;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to detect alpha channel in PSD: {SourcePath}", sourcePath);
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

            using var image = new MagickImage();
            await image.ReadAsync(sourcePath, cancellationToken).ConfigureAwait(false);
            var resizedImage = ImageProcessingHelper.ApplyResizeParameters(image, parameters);
            using var resizedScope = ReferenceEquals(resizedImage, image) ? null : resizedImage;

            cancellationToken.ThrowIfCancellationRequested();

            var targetExt = Path.GetExtension(targetPath).ToLowerInvariant();
            if (targetExt == ".dds")
            {
                return await EncodeImageToDdsAsync(resizedImage, targetPath, sourcePath, cancellationToken).ConfigureAwait(false);
            }

            await ImageProcessingHelper.SaveImageToTargetAsync(resizedImage, targetPath, targetExt, cancellationToken).ConfigureAwait(false);
            return true;
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> ConvertTiffAsync(
        string sourcePath,
        string targetPath,
        IDictionary<string, object>? parameters,
        CancellationToken cancellationToken)
    {
        var targetExt = Path.GetExtension(targetPath).ToLowerInvariant();
        if (targetExt == ".dds")
        {
            return await ConvertDdsAsync(sourcePath, targetPath, parameters, cancellationToken).ConfigureAwait(false);
        }

        return await Task.Run(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var image = new MagickImage();
            await image.ReadAsync(sourcePath, cancellationToken).ConfigureAwait(false);

            // Matches the old rule of rejecting fewer than 24 bits per pixel: gray and bilevel always,
            // gray with alpha only at 8 bits per sample.
            if (image.ColorType is ColorType.Bilevel or ColorType.Grayscale
                || (image.ColorType == ColorType.GrayscaleAlpha && image.Depth <= 8))
            {
                logger.LogError("TIFF image has unsupported color mode: {SourcePath}", sourcePath);
                return false;
            }

            var resizedImage = ImageProcessingHelper.ApplyResizeParameters(image, parameters);
            using var resizedScope = ReferenceEquals(resizedImage, image) ? null : resizedImage;

            cancellationToken.ThrowIfCancellationRequested();
            await ImageProcessingHelper.SaveImageToTargetAsync(resizedImage, targetPath, targetExt, cancellationToken).ConfigureAwait(false);
            return true;
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> ConvertDdsAsync(
        string sourcePath,
        string targetPath,
        IDictionary<string, object>? parameters,
        CancellationToken cancellationToken)
    {
        try
        {
            var (rawData, width, height, hasAlpha) = sourcePath.EndsWith(".dds", StringComparison.OrdinalIgnoreCase)
                ? LoadDdsPixels(sourcePath)
                : await LoadStandardPixelsAsync(sourcePath, parameters, cancellationToken).ConfigureAwait(false);

            return await WriteDdsDataAsync(rawData, width, height, hasAlpha, targetPath, sourcePath, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to convert to DDS: {SourcePath}", sourcePath);
            return false;
        }
    }

    private async Task<bool> EncodeImageToDdsAsync(
        IMagickImage<ushort> image,
        string targetPath,
        string sourcePath,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var width = (int)image.Width;
            var height = (int)image.Height;
            var hasAlpha = ImageProcessingHelper.DetectAlpha(image);
            var rawData = RgbaPixels.ToBytes(image);

            return await WriteDdsDataAsync(rawData, width, height, hasAlpha, targetPath, sourcePath, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to encode DDS from image: {SourcePath}", sourcePath);
            return false;
        }
    }

    private async Task<bool> WriteDdsDataAsync(
        byte[] rawData,
        int width,
        int height,
        bool hasAlpha,
        string targetPath,
        string sourcePath,
        CancellationToken cancellationToken)
    {
        var encoder = new BcEncoder();
        encoder.OutputOptions.GenerateMipMaps = true;
        encoder.OutputOptions.Quality = CompressionQuality.Balanced;
        encoder.OutputOptions.FileFormat = OutputFileFormat.Dds;

        // The build engine already converts files in parallel; keep each encode
        // single-threaded so parallel encodes do not oversubscribe the CPU.
        encoder.Options.IsParallel = false;

        // Auto-detect format based on alpha
        encoder.OutputOptions.Format = hasAlpha
            ? CompressionFormat.Bc3 // DXT5 with alpha
            : CompressionFormat.Bc1; // DXT1 no alpha

        await using var output = File.Create(targetPath);

        await encoder.EncodeToStreamAsync(
            rawData,
            width,
            height,
            BCnEncoder.Encoder.PixelFormat.Rgba32,
            output,
            cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Converted {Source} to DDS format {Format}", sourcePath, encoder.OutputOptions.Format);
        return true;
    }

    private static (byte[] RawData, int Width, int Height, bool HasAlpha) LoadDdsPixels(string sourcePath)
    {
        using var magickImage = new MagickImage(sourcePath);
        var width = (int)magickImage.Width;
        var height = (int)magickImage.Height;
        var hasAlpha = magickImage.HasAlpha;
        var pixelCollection = magickImage.GetPixels();
        var rawData = pixelCollection.ToByteArray(PixelMapping.RGBA) ?? [];
        return (rawData, width, height, hasAlpha);
    }

    private static async Task<(byte[] RawData, int Width, int Height, bool HasAlpha)> LoadStandardPixelsAsync(
        string sourcePath,
        IDictionary<string, object>? parameters,
        CancellationToken cancellationToken)
    {
        using var image = new MagickImage();
        await image.ReadAsync(sourcePath, cancellationToken).ConfigureAwait(false);
        var resizedImage = ImageProcessingHelper.ApplyResizeParameters(image, parameters);
        using var resizedScope = ReferenceEquals(resizedImage, image) ? null : resizedImage;
        var hasAlpha = ImageProcessingHelper.DetectAlpha(resizedImage);
        var rawData = RgbaPixels.ToBytes(resizedImage);
        return (rawData, (int)resizedImage.Width, (int)resizedImage.Height, hasAlpha);
    }

    private static async Task<bool> ConvertGenericAsync(
        string sourcePath,
        string targetPath,
        IDictionary<string, object>? parameters,
        CancellationToken cancellationToken)
    {
        return await Task.Run(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (sourcePath.EndsWith(".dds", StringComparison.OrdinalIgnoreCase))
            {
                using var magickImage = new MagickImage(sourcePath);
                await magickImage.WriteAsync(targetPath, cancellationToken).ConfigureAwait(false);
                return true;
            }

            using var image = new MagickImage();
            await image.ReadAsync(sourcePath, cancellationToken).ConfigureAwait(false);
            var resizedImage = ImageProcessingHelper.ApplyResizeParameters(image, parameters);
            using var resizedScope = ReferenceEquals(resizedImage, image) ? null : resizedImage;

            cancellationToken.ThrowIfCancellationRequested();
            await ImageProcessingHelper.SaveImageToTargetAsync(resizedImage, targetPath, Path.GetExtension(targetPath).ToLowerInvariant(), cancellationToken).ConfigureAwait(false);
            return true;
        }, cancellationToken).ConfigureAwait(false);
    }
}
