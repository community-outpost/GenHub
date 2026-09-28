using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Tools.TextureEditor;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.TextureEditor;
using GenHub.Core.Services.Tools.Checksum;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.TextureEditor.Services;

/// <summary>
/// Bridges portable decoded textures and Avalonia bitmaps for the Texture Editor.
/// </summary>
public sealed class TextureBitmapService(ISageTextureCodec codec, ILogger<TextureBitmapService> logger)
{
    /// <summary>
    /// Crops a bitmap region, clamping the rectangle to the source bounds.
    /// </summary>
    /// <param name="source">The source bitmap.</param>
    /// <param name="x">The left pixel.</param>
    /// <param name="y">The top pixel.</param>
    /// <param name="width">The region width.</param>
    /// <param name="height">The region height.</param>
    /// <returns>The cropped image, or null when the rectangle falls outside the source.</returns>
    public static IImage? Crop(Bitmap source, int x, int y, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(source);

        int clampedX = Math.Clamp(x, 0, source.PixelSize.Width);
        int clampedY = Math.Clamp(y, 0, source.PixelSize.Height);
        int clampedWidth = (int)Math.Clamp((long)x + width - clampedX, 0L, source.PixelSize.Width - clampedX);
        int clampedHeight = (int)Math.Clamp((long)y + height - clampedY, 0L, source.PixelSize.Height - clampedY);
        if (clampedWidth <= 0 || clampedHeight <= 0)
        {
            return null;
        }

        return new CroppedBitmap(source, new PixelRect(clampedX, clampedY, clampedWidth, clampedHeight));
    }

    /// <summary>
    /// Creates a placeholder decoded texture of the specified dimensions with a dark checkerboard pattern.
    /// Used when a mapped image definition or INI references a texture not found on disk.
    /// </summary>
    /// <param name="width">The texture width in pixels.</param>
    /// <param name="height">The texture height in pixels.</param>
    /// <returns>A decoded RGBA texture with a neutral dark checker pattern.</returns>
    public static DecodedTexture CreatePlaceholder(int width, int height)
    {
        int safeWidth = Math.Clamp(width, 1, 4096);
        int safeHeight = Math.Clamp(height, 1, 4096);
        byte[] pixels = new byte[safeWidth * safeHeight * 4];
        const int cellSize = 32;
        for (int y = 0; y < safeHeight; y++)
        {
            int cellY = (y / cellSize) % 2;
            int rowOffset = y * safeWidth * 4;
            for (int x = 0; x < safeWidth; x++)
            {
                int cellX = (x / cellSize) % 2;
                bool isLight = (cellX ^ cellY) == 0;
                byte color = isLight ? (byte)45 : (byte)32;
                int pixelOffset = rowOffset + (x * 4);
                pixels[pixelOffset] = color;
                pixels[pixelOffset + 1] = color;
                pixels[pixelOffset + 2] = color;
                pixels[pixelOffset + 3] = 255;
            }
        }

        return new DecodedTexture(safeWidth, safeHeight, pixels);
    }

    /// <summary>
    /// Loads an image file into portable RGBA pixels.
    /// Supports loose files on disk and entries archived inside .BIG packages (via archive#entry syntax).
    /// TGA and DDS files use the SAGE codec, other formats use ImageSharp.
    /// </summary>
    /// <param name="path">The image file path or archive#entry reference.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The decoded texture, or a failure describing the problem.</returns>
    public async Task<OperationResult<DecodedTexture>> LoadDecodedAsync(string path, CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.GetTimestamp();
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        int hashIndex = path.IndexOf('#');
        if (hashIndex > 0
            && path[..hashIndex].EndsWith(".big", StringComparison.OrdinalIgnoreCase)
            && File.Exists(path[..hashIndex]))
        {
            string[] parts = path.Split('#', 2);
            string archivePath = parts[0];
            string entryRelativePath = parts[1];

            if (File.Exists(archivePath) && BigArchiveReader.TryReadIndex(archivePath, out var index))
            {
                string normKey = entryRelativePath.Replace('/', '\\').ToLowerInvariant();
                if (!index.TryGetValue(normKey, out var entry))
                {
                    entry = index.Values.FirstOrDefault(e => string.Equals(e.Path.Replace('/', '\\'), entryRelativePath.Replace('/', '\\'), StringComparison.OrdinalIgnoreCase));
                }

                if (entry is not null)
                {
                    try
                    {
                        byte[] bytes = BigArchiveReader.ReadEntryData(entry);
                        string ext = Path.GetExtension(entryRelativePath);
                        if (codec.SupportsExtension(ext))
                        {
                            return codec.Decode(bytes, ext, entryRelativePath);
                        }

                        using var ms = new MemoryStream(bytes);
                        using var image = await Image.LoadAsync<Rgba32>(ms, cancellationToken).ConfigureAwait(false);
                        var pixels = new byte[image.Width * image.Height * 4];
                        image.CopyPixelDataTo(pixels);
                        var texture = new DecodedTexture(image.Width, image.Height, pixels);
                        return OperationResult<DecodedTexture>.CreateSuccess(texture, Stopwatch.GetElapsedTime(started));
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Failed to decode texture {Entry} from archive {Archive}", entryRelativePath, archivePath);
                        return OperationResult<DecodedTexture>.CreateFailure($"Failed to decode texture from archive: {ex.Message}", Stopwatch.GetElapsedTime(started));
                    }
                }
            }

            return OperationResult<DecodedTexture>.CreateFailure($"Archive entry not found: {path}", Stopwatch.GetElapsedTime(started));
        }

        string extension = Path.GetExtension(path);
        if (codec.SupportsExtension(extension))
        {
            return await codec.DecodeFileAsync(path, cancellationToken).ConfigureAwait(false);
        }

        if (!File.Exists(path))
        {
            return OperationResult<DecodedTexture>.CreateFailure($"Image file not found: {path}", Stopwatch.GetElapsedTime(started));
        }

        try
        {
            using var image = await Image.LoadAsync<Rgba32>(path, cancellationToken).ConfigureAwait(false);
            var pixels = new byte[image.Width * image.Height * 4];
            image.CopyPixelDataTo(pixels);
            var texture = new DecodedTexture(image.Width, image.Height, pixels);
            return OperationResult<DecodedTexture>.CreateSuccess(texture, Stopwatch.GetElapsedTime(started));
        }
        catch (UnknownImageFormatException ex)
        {
            logger.LogWarning(ex, "Unsupported image format: {Path}", path);
            return OperationResult<DecodedTexture>.CreateFailure($"Unsupported image format: {path}", Stopwatch.GetElapsedTime(started));
        }
        catch (InvalidImageContentException ex)
        {
            logger.LogWarning(ex, "Invalid image content: {Path}", path);
            return OperationResult<DecodedTexture>.CreateFailure($"Invalid image content: {path}", Stopwatch.GetElapsedTime(started));
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Failed to read image file: {Path}", path);
            return OperationResult<DecodedTexture>.CreateFailure($"Failed to read image file: {path}", Stopwatch.GetElapsedTime(started));
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Access denied reading image file: {Path}", path);
            return OperationResult<DecodedTexture>.CreateFailure($"Access denied reading image file: {path}", Stopwatch.GetElapsedTime(started));
        }
    }

    /// <summary>
    /// Converts portable RGBA pixels into an Avalonia bitmap.
    /// </summary>
    /// <param name="texture">The decoded texture.</param>
    /// <returns>The bitmap, or a failure describing the problem.</returns>
    public OperationResult<Bitmap> ToBitmap(DecodedTexture texture)
    {
        var started = Stopwatch.GetTimestamp();
        ArgumentNullException.ThrowIfNull(texture);

        if (texture.Width <= 0 || texture.Height <= 0)
        {
            return OperationResult<Bitmap>.CreateFailure("Texture dimensions must be positive.", Stopwatch.GetElapsedTime(started));
        }

        if ((long)texture.Width * texture.Height * 4 != texture.PixelData.Length)
        {
            return OperationResult<Bitmap>.CreateFailure("Pixel data length does not match texture dimensions.", Stopwatch.GetElapsedTime(started));
        }

        try
        {
            var bitmap = new WriteableBitmap(
                new PixelSize(texture.Width, texture.Height),
                new Vector(96, 96),
                PixelFormat.Rgba8888,
                AlphaFormat.Unpremul);
            using (var locked = bitmap.Lock())
            {
                int rowBytes = texture.Width * 4;
                for (int y = 0; y < texture.Height; y++)
                {
                    Marshal.Copy(texture.PixelData, y * rowBytes, locked.Address + (y * locked.RowBytes), rowBytes);
                }
            }

            return OperationResult<Bitmap>.CreateSuccess(bitmap, Stopwatch.GetElapsedTime(started));
        }
        catch (ArgumentException ex)
        {
            logger.LogWarning(ex, "Failed to create bitmap for {Width}x{Height} texture", texture.Width, texture.Height);
            return OperationResult<Bitmap>.CreateFailure("Failed to create bitmap from texture pixels.", Stopwatch.GetElapsedTime(started));
        }
    }

    /// <summary>
    /// Encodes portable pixels as PNG and saves them to a file.
    /// </summary>
    /// <param name="texture">The texture to save.</param>
    /// <param name="path">The destination path.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The saved path, or a failure describing the problem.</returns>
    public async Task<OperationResult<string>> SavePngAsync(DecodedTexture texture, string path, CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.GetTimestamp();
        ArgumentNullException.ThrowIfNull(texture);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (texture.Width <= 0 || texture.Height <= 0)
        {
            return OperationResult<string>.CreateFailure("Texture dimensions must be positive.", Stopwatch.GetElapsedTime(started));
        }

        if ((long)texture.Width * texture.Height * 4 != texture.PixelData.Length)
        {
            return OperationResult<string>.CreateFailure("Pixel data length does not match texture dimensions.", Stopwatch.GetElapsedTime(started));
        }

        try
        {
            byte[] encoded;
            using (var buffer = new MemoryStream())
            {
                using var image = Image.LoadPixelData<Rgba32>(texture.PixelData, texture.Width, texture.Height);
                await image.SaveAsPngAsync(buffer, cancellationToken).ConfigureAwait(false);
                encoded = buffer.ToArray();
            }

            await AtomicFile.WriteAllBytesAsync(path, encoded, cancellationToken).ConfigureAwait(false);
            return OperationResult<string>.CreateSuccess(path, Stopwatch.GetElapsedTime(started));
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Failed to save PNG file: {Path}", path);
            return OperationResult<string>.CreateFailure($"Failed to save PNG file: {path}", Stopwatch.GetElapsedTime(started));
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Access denied saving PNG file: {Path}", path);
            return OperationResult<string>.CreateFailure($"Access denied saving PNG file: {path}", Stopwatch.GetElapsedTime(started));
        }
    }

    /// <summary>
    /// Encodes portable pixels as TGA and saves them to a file.
    /// </summary>
    /// <param name="texture">The texture to save.</param>
    /// <param name="path">The destination path.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The saved path, or a failure describing the problem.</returns>
    public async Task<OperationResult<string>> SaveTgaAsync(DecodedTexture texture, string path, CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.GetTimestamp();
        ArgumentNullException.ThrowIfNull(texture);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var encoded = codec.EncodeTga(texture);
        if (encoded.Failed || encoded.Data is null)
        {
            return OperationResult<string>.CreateFailure(encoded, Stopwatch.GetElapsedTime(started));
        }

        try
        {
            await AtomicFile.WriteAllBytesAsync(path, encoded.Data, cancellationToken).ConfigureAwait(false);
            return OperationResult<string>.CreateSuccess(path, Stopwatch.GetElapsedTime(started));
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Failed to save TGA file: {Path}", path);
            return OperationResult<string>.CreateFailure($"Failed to save TGA file: {path}", Stopwatch.GetElapsedTime(started));
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Access denied saving TGA file: {Path}", path);
            return OperationResult<string>.CreateFailure($"Access denied saving TGA file: {path}", Stopwatch.GetElapsedTime(started));
        }
    }
}
