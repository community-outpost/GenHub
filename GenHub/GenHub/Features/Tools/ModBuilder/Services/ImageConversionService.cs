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
public class ImageConversionService(ILogger<ImageConversionService> logger) : IImageConversionService
{
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
                logger.LogError("Source file does not exist: {SourcePath}", sourcePath);
                return false;
            }

            var targetDir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            var ext = Path.GetExtension(sourcePath).ToLowerInvariant();

            return ext switch
            {
                ".psd" => await ConvertPsdAsync(sourcePath, targetPath, parameters, cancellationToken),
                ".tga" => await ConvertTgaAsync(sourcePath, targetPath, parameters, cancellationToken),
                ".tif" or ".tiff" => await ConvertTiffAsync(sourcePath, targetPath, parameters, cancellationToken),
                ".dds" => await ConvertDdsAsync(sourcePath, targetPath, parameters, cancellationToken),
                _ => await ConvertGenericAsync(sourcePath, targetPath, parameters, cancellationToken),
            };
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Image conversion cancelled: {SourcePath}", sourcePath);
            return false;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to convert image from {SourcePath} to {TargetPath}", sourcePath, targetPath);
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

                if (ext == ".dds")
                {
                    using var magickImage = new MagickImage(imagePath);
                    return magickImage.HasAlpha;
                }

                if (ext == ".psd")
                {
                    return HasAlphaChannelPsd(imagePath);
                }

                using var image = Image.Load(imagePath);
                return ImageProcessingHelper.DetectAlpha(image);
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to detect alpha channel in {ImagePath}", imagePath);
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
            logger.LogError(ex, "Failed to convert PSD: {SourcePath}", sourcePath);
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
        using var magickImage = new MagickImage(sourcePath)
        {
            Format = MagickFormat.Png,
        };
        using var ms = new MemoryStream();
        magickImage.Write(ms);
        ms.Position = 0;
        return Image.Load<Rgba32>(ms);
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

            using var image = await Image.LoadAsync(sourcePath, cancellationToken).ConfigureAwait(false);
            var resizedImage = ImageProcessingHelper.ApplyResizeParameters(image, parameters);

            cancellationToken.ThrowIfCancellationRequested();

            var targetExt = Path.GetExtension(targetPath).ToLowerInvariant();
            if (targetExt == ".dds")
            {
                var tempPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
                try
                {
                    await ImageProcessingHelper.SaveImageToTargetAsync(resizedImage, tempPath, ".tga", cancellationToken).ConfigureAwait(false);
                    return await ConvertDdsAsync(tempPath, targetPath, parameters, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
                }
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

            using var image = await Image.LoadAsync(sourcePath, cancellationToken).ConfigureAwait(false);

            if (image.PixelType.BitsPerPixel < 24)
            {
                logger.LogError("TIFF image has unsupported color mode: {SourcePath}", sourcePath);
                return false;
            }

            var resizedImage = ImageProcessingHelper.ApplyResizeParameters(image, parameters);

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
            byte[] rawData;
            int width;
            int height;
            bool hasAlpha;

            if (sourcePath.EndsWith(".dds", StringComparison.OrdinalIgnoreCase))
            {
                using var magickImage = new MagickImage(sourcePath);
                width = (int)magickImage.Width;
                height = (int)magickImage.Height;
                hasAlpha = magickImage.HasAlpha;
                var pixelCollection = magickImage.GetPixels();
                rawData = pixelCollection.ToByteArray(PixelMapping.RGBA) ?? Array.Empty<byte>();
            }
            else
            {
                using var image = await Image.LoadAsync<Rgba32>(sourcePath, cancellationToken).ConfigureAwait(false);
                width = image.Width;
                height = image.Height;
                hasAlpha = await HasAlphaChannelAsync(sourcePath, cancellationToken).ConfigureAwait(false);

                rawData = new byte[width * height * 4];
                image.CopyPixelDataTo(rawData);
            }

            var encoder = new BcEncoder();
            encoder.OutputOptions.GenerateMipMaps = true;
            encoder.OutputOptions.Quality = CompressionQuality.Balanced;

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
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to convert to DDS: {SourcePath}", sourcePath);
            return false;
        }
    }

    private async Task<bool> ConvertGenericAsync(
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
                magickImage.Write(targetPath);
                return true;
            }

            using var image = await Image.LoadAsync(sourcePath, cancellationToken).ConfigureAwait(false);
            var resizedImage = ImageProcessingHelper.ApplyResizeParameters(image, parameters);

            cancellationToken.ThrowIfCancellationRequested();
            await ImageProcessingHelper.SaveImageToTargetAsync(resizedImage, targetPath, Path.GetExtension(targetPath).ToLowerInvariant(), cancellationToken).ConfigureAwait(false);
            return true;
        }, cancellationToken).ConfigureAwait(false);
    }
}
