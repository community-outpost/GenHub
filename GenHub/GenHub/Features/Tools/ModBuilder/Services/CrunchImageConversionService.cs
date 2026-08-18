using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
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
/// Service for converting images using the external crunch_x64 tool.
/// Provides high-performance DDS conversions matching python and go modbuilder implementations.
/// </summary>
public class CrunchImageConversionService(
    IExternalToolService externalToolService,
    ILogger<CrunchImageConversionService> logger) : IImageConversionService
{
    private static readonly Dictionary<string, ResamplingMode> ResamplingModes = new(StringComparer.OrdinalIgnoreCase)
    {
        { "nearest", ResamplingMode.NearestNeighbor },
        { "box", ResamplingMode.Box },
        { "bilinear", ResamplingMode.Bilinear },
        { "hamming", ResamplingMode.Hamming },
        { "bicubic", ResamplingMode.Bicubic },
        { "lanczos", ResamplingMode.Lanczos },
    };

    /// <inheritdoc />
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

            var targetExt = Path.GetExtension(targetPath).ToLowerInvariant();
            var sourceExt = Path.GetExtension(sourcePath).ToLowerInvariant();

            if (targetExt == ".dds")
            {
                return await ConvertToDdsViaCrunchAsync(sourcePath, targetPath, sourceExt, parameters, cancellationToken).ConfigureAwait(false);
            }

            return await ConvertToStandardImageAsync(sourcePath, targetPath, sourceExt, targetExt, parameters, cancellationToken).ConfigureAwait(false);
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

    /// <inheritdoc />
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
                    using var image = new MagickImage(imagePath);
                    return image.ChannelCount > 3;
                }

                using var loaded = Image.Load(imagePath);
                return DetectAlpha(loaded);
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to detect alpha channel in {ImagePath}", imagePath);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<string> GetRecommendedDxtFormatAsync(string imagePath, CancellationToken cancellationToken = default)
    {
        var hasAlpha = await HasAlphaChannelAsync(imagePath, cancellationToken).ConfigureAwait(false);
        return hasAlpha ? ModBuilderConstants.Dxt5Format : ModBuilderConstants.Dxt1Format;
    }

    /// <summary>
    /// Converts an image to dds using crunch_x64 with temporary tga generation when needed.
    /// </summary>
    private async Task<bool> ConvertToDdsViaCrunchAsync(
        string sourcePath,
        string targetPath,
        string sourceExt,
        IDictionary<string, object>? parameters,
        CancellationToken cancellationToken)
    {
        var hasResize = HasResizeParameters(parameters);
        var requiresIntermediateTga = hasResize || sourceExt is ".psd" or ".tif" or ".tiff";

        string crunchInputFile = sourcePath;
        string? temporaryTgaFile = null;

        try
        {
            if (requiresIntermediateTga)
            {
                temporaryTgaFile = Path.Combine(Path.GetTempPath(), $"crunch_tmp_{Guid.NewGuid():N}.tga");
                var prepSuccess = await PrepareTgaIntermediateAsync(sourcePath, temporaryTgaFile, sourceExt, parameters, cancellationToken).ConfigureAwait(false);
                if (!prepSuccess)
                {
                    logger.LogError("Failed to prepare intermediate tga for crunch: {SourcePath}", sourcePath);
                    return false;
                }

                crunchInputFile = temporaryTgaFile;
            }

            var toolPath = ResolveCrunchExecutable();
            var arguments = await BuildCrunchArgumentsAsync(crunchInputFile, targetPath, parameters, cancellationToken).ConfigureAwait(false);

            var toolResult = await externalToolService.ExecuteToolAsync(
                toolPath,
                arguments,
                workingDirectory: Path.GetDirectoryName(targetPath),
                progress: null,
                cancellationToken).ConfigureAwait(false);

            if (!toolResult.Success && !requiresIntermediateTga)
            {
                // fallback: convert to temporary tga and retry crunch
                logger.LogWarning("Direct crunch conversion failed for {SourcePath}, retrying via temporary tga", sourcePath);
                temporaryTgaFile = Path.Combine(Path.GetTempPath(), $"crunch_tmp_{Guid.NewGuid():N}.tga");
                var prepSuccess = await PrepareTgaIntermediateAsync(sourcePath, temporaryTgaFile, sourceExt, parameters, cancellationToken).ConfigureAwait(false);
                if (prepSuccess)
                {
                    crunchInputFile = temporaryTgaFile;
                    arguments = await BuildCrunchArgumentsAsync(crunchInputFile, targetPath, parameters, cancellationToken).ConfigureAwait(false);
                    toolResult = await externalToolService.ExecuteToolAsync(
                        toolPath,
                        arguments,
                        workingDirectory: Path.GetDirectoryName(targetPath),
                        progress: null,
                        cancellationToken).ConfigureAwait(false);
                }
            }

            return toolResult.Success && File.Exists(targetPath);
        }
        finally
        {
            if (!string.IsNullOrEmpty(temporaryTgaFile) && File.Exists(temporaryTgaFile))
            {
                try
                {
                    File.Delete(temporaryTgaFile);
                }
                catch
                {
                    // ignore temporary file cleanup failure
                }
            }
        }
    }

    /// <summary>
    /// Builds the argument string for crunch_x64.
    /// </summary>
    private async Task<string> BuildCrunchArgumentsAsync(
        string inputFile,
        string outputFile,
        IDictionary<string, object>? parameters,
        CancellationToken cancellationToken)
    {
        var rawArgs = new List<string>
        {
            "-file",
            inputFile,
            "-out",
            outputFile,
            "-fileformat",
            "dds",
            "-noprogress",
            "-quiet"
        };

        var explicitFormat = ExtractExplicitFormat(parameters);

        if (parameters != null)
        {
            foreach (var kvp in parameters)
            {
                if (kvp.Key.StartsWith('-'))
                {
                    if (kvp.Value is bool b)
                    {
                        if (b)
                        {
                            rawArgs.Add(kvp.Key);
                        }
                    }
                    else if (kvp.Value != null)
                    {
                        rawArgs.Add(kvp.Key);
                        var valStr = kvp.Value.ToString();
                        if (!string.IsNullOrEmpty(valStr))
                        {
                            rawArgs.Add(valStr);
                        }
                    }
                }
            }
        }

        if (!string.IsNullOrEmpty(explicitFormat))
        {
            if (!rawArgs.Contains(explicitFormat, StringComparer.OrdinalIgnoreCase))
            {
                rawArgs.Add(explicitFormat);
            }
        }
        else
        {
            // auto detect dxt format based on alpha presence
            var hasAlpha = await HasAlphaChannelAsync(inputFile, cancellationToken).ConfigureAwait(false);
            rawArgs.Add(hasAlpha ? "-DXT5" : "-DXT1");
        }

        return string.Join(" ", rawArgs.Select(EscapeArgument));
    }

    private static string EscapeArgument(string arg)
    {
        if (string.IsNullOrEmpty(arg))
        {
            return "\"\"";
        }

        if (!arg.Contains(' ') && !arg.Contains('\t') && !arg.Contains('"') && !arg.Contains('\\'))
        {
            return arg;
        }

        return "\"" + arg.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }

    /// <summary>
    /// Extracts explicit texture format from parameters if specified.
    /// </summary>
    private static string? ExtractExplicitFormat(IDictionary<string, object>? parameters)
    {
        if (parameters == null)
        {
            return null;
        }

        foreach (var flag in ModBuilderConstants.CrunchTextureFormatFlags)
        {
            if (parameters.ContainsKey(flag))
            {
                return flag;
            }

            var trimmedFlag = flag.TrimStart('-');
            if (parameters.ContainsKey(trimmedFlag))
            {
                return flag;
            }
        }

        if (parameters.TryGetValue("format", out var formatObj) && formatObj is string formatStr)
        {
            var formatted = NormalizeFormatFlag(formatStr);
            if (formatted != null)
            {
                return formatted;
            }
        }

        if (parameters.TryGetValue("compression", out var compObj) && compObj is string compStr)
        {
            var formatted = NormalizeFormatFlag(compStr);
            if (formatted != null)
            {
                return formatted;
            }
        }

        return null;
    }

    /// <summary>
    /// Normalizes format string to crunch flag format.
    /// </summary>
    private static string? NormalizeFormatFlag(string format)
    {
        var upper = format.ToUpperInvariant().Trim();
        if (upper is "DXT1" or "BC1")
        {
            return "-DXT1";
        }

        if (upper is "DXT5" or "BC3")
        {
            return "-DXT5";
        }

        if (upper is "DXT3" or "BC2")
        {
            return "-DXT3";
        }

        if (upper.StartsWith('-') && ModBuilderConstants.CrunchTextureFormatFlags.Contains(upper))
        {
            return upper;
        }

        if (ModBuilderConstants.CrunchTextureFormatFlags.Contains("-" + upper))
        {
            return "-" + upper;
        }

        return null;
    }

    /// <summary>
    /// Prepares a 32-bit tga intermediate file with multi-alpha compositing and channel-split resizing.
    /// </summary>
    private async Task<bool> PrepareTgaIntermediateAsync(
        string sourcePath,
        string targetTgaPath,
        string sourceExt,
        IDictionary<string, object>? parameters,
        CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (sourceExt == ".psd")
            {
                using var magickImage = new MagickImage(sourcePath);

                if (magickImage.ChannelCount <= 3)
                {
                    using var ms = new MemoryStream();
                    magickImage.Format = MagickFormat.Png;
                    magickImage.Write(ms);
                    ms.Position = 0;
                    using var loaded = Image.Load(ms);
                    var resized = ApplyResizeParameters(loaded, parameters);
                    resized.SaveAsTga(targetTgaPath, new TgaEncoder
                    {
                        BitsPerPixel = TgaBitsPerPixel.Pixel32,
                        Compression = TgaCompression.None
                    });
                    return true;
                }

                // multi-alpha compositing for psd files with > 3 channels
                var channels = magickImage.Separate().ToList();
                var r = channels[0];
                var g = channels[1];
                var b = channels[2];

                var alpha = new MagickImage(MagickColors.White, magickImage.Width, magickImage.Height);
                for (int i = 3; i < magickImage.ChannelCount; i++)
                {
                    alpha.Composite(channels[i], CompositeOperator.Multiply);
                }

                var collection = new MagickImageCollection { r, g, b, alpha };
                using var merged = collection.Combine(ColorSpace.sRGB);
                using var msPsd = new MemoryStream();
                merged.Format = MagickFormat.Png;
                merged.Write(msPsd);
                msPsd.Position = 0;

                foreach (var ch in channels)
                {
                    ch.Dispose();
                }

                alpha.Dispose();

                using var psdLoaded = Image.Load(msPsd);
                var resizedPsd = ApplyResizeParameters(psdLoaded, parameters);
                resizedPsd.SaveAsTga(targetTgaPath, new TgaEncoder
                {
                    BitsPerPixel = TgaBitsPerPixel.Pixel32,
                    Compression = TgaCompression.None
                });
                return true;
            }

            if (sourceExt == ".dds")
            {
                using var magickDds = new MagickImage(sourcePath);
                magickDds.Write(targetTgaPath);
                return true;
            }

            using var image = Image.Load(sourcePath);
            var resizedImage = ApplyResizeParameters(image, parameters);

            cancellationToken.ThrowIfCancellationRequested();

            resizedImage.SaveAsTga(targetTgaPath, new TgaEncoder
            {
                BitsPerPixel = TgaBitsPerPixel.Pixel32,
                Compression = TgaCompression.None
            });

            return true;
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Converts an image to non-dds formats like tga or bmp.
    /// </summary>
    private async Task<bool> ConvertToStandardImageAsync(
        string sourcePath,
        string targetPath,
        string sourceExt,
        string targetExt,
        IDictionary<string, object>? parameters,
        CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (sourceExt == ".dds")
            {
                using var magickDds = new MagickImage(sourcePath);
                magickDds.Write(targetPath);
                return true;
            }

            if (sourceExt == ".psd")
            {
                return ConvertPsdToStandardImage(sourcePath, targetPath, targetExt, parameters);
            }

            using var image = Image.Load(sourcePath);
            var resizedImage = ApplyResizeParameters(image, parameters);

            cancellationToken.ThrowIfCancellationRequested();

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
                default:
                    resizedImage.Save(targetPath);
                    break;
            }

            return true;
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Converts psd to standard image formats with multi-alpha compositing.
    /// </summary>
    private static bool ConvertPsdToStandardImage(
        string sourcePath,
        string targetPath,
        string targetExt,
        IDictionary<string, object>? parameters)
    {
        using var magickImage = new MagickImage(sourcePath);

        if (magickImage.ChannelCount <= 3)
        {
            using var ms = new MemoryStream();
            magickImage.Format = MagickFormat.Png;
            magickImage.Write(ms);
            ms.Position = 0;
            using var loaded = Image.Load(ms);
            var resized = ApplyResizeParameters(loaded, parameters);
            SaveImageToTarget(resized, targetPath, targetExt);
            return true;
        }

        var channels = magickImage.Separate().ToList();
        var r = channels[0];
        var g = channels[1];
        var b = channels[2];

        var alpha = new MagickImage(MagickColors.White, magickImage.Width, magickImage.Height);
        for (int i = 3; i < magickImage.ChannelCount; i++)
        {
            alpha.Composite(channels[i], CompositeOperator.Multiply);
        }

        var collection = new MagickImageCollection { r, g, b, alpha };
        using var merged = collection.Combine(ColorSpace.sRGB);
        using var msCombined = new MemoryStream();
        merged.Format = MagickFormat.Png;
        merged.Write(msCombined);
        msCombined.Position = 0;

        foreach (var ch in channels)
        {
            ch.Dispose();
        }

        alpha.Dispose();

        using var psdLoaded = Image.Load(msCombined);
        var resizedPsd = ApplyResizeParameters(psdLoaded, parameters);
        SaveImageToTarget(resizedPsd, targetPath, targetExt);
        return true;
    }

    /// <summary>
    /// Saves an imagesharp image to target path with proper format encoders.
    /// </summary>
    private static void SaveImageToTarget(Image image, string targetPath, string targetExt)
    {
        switch (targetExt)
        {
            case ".bmp":
                image.SaveAsBmp(targetPath, new BmpEncoder());
                break;
            case ".tga":
                image.SaveAsTga(targetPath, new TgaEncoder
                {
                    BitsPerPixel = TgaBitsPerPixel.Pixel32,
                    Compression = TgaCompression.None
                });
                break;
            default:
                image.Save(targetPath);
                break;
        }
    }

    /// <summary>
    /// Resolves the absolute path to crunch_x64 executable.
    /// </summary>
    /// <returns>The resolved executable path or default tool name.</returns>
    public static string ResolveCrunchExecutable()
    {
        foreach (var candidate in ModBuilderConstants.CrunchExecutableCandidates)
        {
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(pathEnv))
        {
            var extensions = OperatingSystem.IsWindows()
                ? new[] { string.Empty, ".exe", ".cmd", ".bat" }
                : new[] { string.Empty };

            var names = new[] { ModBuilderConstants.CrunchExecutable, ModBuilderConstants.CrunchFallbackExecutable, "crunch" };

            foreach (var path in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                foreach (var name in names)
                {
                    foreach (var ext in extensions)
                    {
                        var fullPath = Path.Combine(path, name + ext);
                        if (File.Exists(fullPath))
                        {
                            return Path.GetFullPath(fullPath);
                        }
                    }
                }
            }
        }

        return ModBuilderConstants.CrunchExecutable;
    }

    /// <summary>
    /// Checks if parameters contain resize or rescale instructions.
    /// </summary>
    private static bool HasResizeParameters(IDictionary<string, object>? parameters)
    {
        if (parameters == null || parameters.Count == 0)
        {
            return false;
        }

        return parameters.ContainsKey("resize") || parameters.ContainsKey("rescale");
    }

    /// <summary>
    /// Applies resize and rescale parameters to an image.
    /// </summary>
    private static Image ApplyResizeParameters(Image image, IDictionary<string, object>? parameters)
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
    /// Resizes rgba channels independently to preserve color information where alpha is black.
    /// </summary>
    private static Image ResizeRgbaChannelsSeparately(Image image, Size size, ResamplingMode resamplingMode)
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

        using var rgba32Image = image.CloneAs<Rgba32>();

        using var rChannel = new Image<L8>(rgba32Image.Width, rgba32Image.Height);
        using var gChannel = new Image<L8>(rgba32Image.Width, rgba32Image.Height);
        using var bChannel = new Image<L8>(rgba32Image.Width, rgba32Image.Height);
        using var aChannel = new Image<L8>(rgba32Image.Width, rgba32Image.Height);

        if (rgba32Image.DangerousTryGetSinglePixelMemory(out Memory<Rgba32> rgbaMemory) &&
            rChannel.DangerousTryGetSinglePixelMemory(out Memory<L8> rMemory) &&
            gChannel.DangerousTryGetSinglePixelMemory(out Memory<L8> gMemory) &&
            bChannel.DangerousTryGetSinglePixelMemory(out Memory<L8> bMemory) &&
            aChannel.DangerousTryGetSinglePixelMemory(out Memory<L8> aMemory))
        {
            var rgbaSpan = rgbaMemory.Span;
            var rSpan = rMemory.Span;
            var gSpan = gMemory.Span;
            var bSpan = bMemory.Span;
            var aSpan = aMemory.Span;

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

        rChannel.Mutate(x => x.Resize(new ResizeOptions { Size = size, Mode = ResizeMode.Stretch, Sampler = resampler }));
        gChannel.Mutate(x => x.Resize(new ResizeOptions { Size = size, Mode = ResizeMode.Stretch, Sampler = resampler }));
        bChannel.Mutate(x => x.Resize(new ResizeOptions { Size = size, Mode = ResizeMode.Stretch, Sampler = resampler }));
        aChannel.Mutate(x => x.Resize(new ResizeOptions { Size = size, Mode = ResizeMode.Stretch, Sampler = resampler }));

        var result = new Image<Rgba32>(size.Width, size.Height);
        if (result.DangerousTryGetSinglePixelMemory(out Memory<Rgba32> resultMemory) &&
            rChannel.DangerousTryGetSinglePixelMemory(out Memory<L8> rResizedMemory) &&
            gChannel.DangerousTryGetSinglePixelMemory(out Memory<L8> gResizedMemory) &&
            bChannel.DangerousTryGetSinglePixelMemory(out Memory<L8> bResizedMemory) &&
            aChannel.DangerousTryGetSinglePixelMemory(out Memory<L8> aResizedMemory))
        {
            var resultSpan = resultMemory.Span;
            var rSpan = rResizedMemory.Span;
            var gSpan = gResizedMemory.Span;
            var bSpan = bResizedMemory.Span;
            var aSpan = aResizedMemory.Span;

            for (int i = 0; i < resultSpan.Length; i++)
            {
                resultSpan[i] = new Rgba32(rSpan[i].PackedValue, gSpan[i].PackedValue, bSpan[i].PackedValue, aSpan[i].PackedValue);
            }
        }
        else
        {
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

    /// <summary>
    /// Parses size parameters from diverse input formats.
    /// </summary>
    private static Size ParseSizeParameter(object sizeObj, Size currentSize)
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

    /// <summary>
    /// Parses scale parameters from diverse input formats.
    /// </summary>
    private static (double Width, double Height) ParseScaleParameter(object scaleObj)
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

    /// <summary>
    /// Detects if an imagesharp image has non-opaque alpha pixels.
    /// </summary>
    private static bool DetectAlpha(Image image)
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
