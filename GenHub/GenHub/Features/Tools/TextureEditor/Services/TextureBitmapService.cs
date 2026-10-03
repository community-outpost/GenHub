using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using GenHub.Core.Constants;
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

        if (TryParseArchiveReference(path, out string archivePath, out string entryRelativePath))
        {
            return await LoadDecodedFromArchiveAsync(archivePath, entryRelativePath, started, cancellationToken).ConfigureAwait(false);
        }

        return await LoadDecodedFromLooseFileAsync(path, started, cancellationToken).ConfigureAwait(false);
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

        WriteableBitmap? bitmap = null;
        try
        {
            bitmap = new WriteableBitmap(
                new PixelSize(texture.Width, texture.Height),
                new Vector(96, 96),
                PixelFormat.Rgba8888,
                AlphaFormat.Unpremul);

            using (var frameBuffer = bitmap.Lock())
            {
                int rowBytes = texture.Width * 4;
                for (int y = 0; y < texture.Height; y++)
                {
                    Marshal.Copy(texture.PixelData, y * rowBytes, frameBuffer.Address + (y * frameBuffer.RowBytes), rowBytes);
                }
            }

            var result = bitmap;
            bitmap = null;
            return OperationResult<Bitmap>.CreateSuccess(result, Stopwatch.GetElapsedTime(started));
        }
        catch (OutOfMemoryException ex)
        {
            logger.LogError(ex, "Out of memory allocating {Width}x{Height} bitmap", texture.Width, texture.Height);
            return OperationResult<Bitmap>.CreateFailure($"Insufficient memory for {texture.Width}x{texture.Height} texture.", Stopwatch.GetElapsedTime(started));
        }
        catch (ArgumentException ex)
        {
            logger.LogError(ex, "Invalid bitmap arguments for {Width}x{Height} texture", texture.Width, texture.Height);
            return OperationResult<Bitmap>.CreateFailure($"Invalid bitmap dimensions: {ex.Message}", Stopwatch.GetElapsedTime(started));
        }
        finally
        {
            bitmap?.Dispose();
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

        string? tempPath = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            tempPath = CreateTempPath(path);
            using (var stream = File.Create(tempPath))
            {
                using var image = Image.LoadPixelData<Rgba32>(texture.PixelData, texture.Width, texture.Height);
                await image.SaveAsPngAsync(stream, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            ReplaceDestination(tempPath, path);
            tempPath = null;
            return OperationResult<string>.CreateSuccess(path, Stopwatch.GetElapsedTime(started));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            logger.LogWarning(ex, "Failed to save PNG file: {Path}", path);
            return OperationResult<string>.CreateFailure($"Failed to save PNG file: {path}", Stopwatch.GetElapsedTime(started));
        }
        finally
        {
            DeleteQuietly(tempPath);
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

        cancellationToken.ThrowIfCancellationRequested();
        var encoded = codec.EncodeTga(texture);
        if (encoded.Failed || encoded.Data is null)
        {
            return OperationResult<string>.CreateFailure(encoded, Stopwatch.GetElapsedTime(started));
        }

        string? tempPath = null;
        try
        {
            tempPath = CreateTempPath(path);
            await File.WriteAllBytesAsync(tempPath, encoded.Data, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            ReplaceDestination(tempPath, path);
            tempPath = null;
            return OperationResult<string>.CreateSuccess(path, Stopwatch.GetElapsedTime(started));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            logger.LogWarning(ex, "Failed to save TGA file: {Path}", path);
            return OperationResult<string>.CreateFailure($"Failed to save TGA file: {path}", Stopwatch.GetElapsedTime(started));
        }
        finally
        {
            DeleteQuietly(tempPath);
        }
    }

    private static bool TryParseArchiveReference(string path, out string archivePath, out string entryRelativePath) =>
        TextureEditorConstants.TryParseArchiveReference(path, out archivePath, out entryRelativePath);

    private static string CreateTempPath(string path)
    {
        string fileName = Path.GetFileName(path) + "." + Path.GetRandomFileName() + ".tmp";
        string? directory = Path.GetDirectoryName(path);
        return string.IsNullOrEmpty(directory) ? fileName : Path.Combine(directory, fileName);
    }

    private static void ReplaceDestination(string tempPath, string path) =>
        File.Move(tempPath, path, overwrite: true);

    private static void DeleteQuietly(string? tempPath)
    {
        if (tempPath is null)
        {
            return;
        }

        try
        {
            File.Delete(tempPath);
        }
        catch (IOException)
        {
            // Best effort cleanup of the temp file; the export result is already decided.
        }
        catch (UnauthorizedAccessException)
        {
            // Best effort cleanup of the temp file; the export result is already decided.
        }
    }

    private async Task<OperationResult<DecodedTexture>> LoadDecodedFromArchiveAsync(
        string archivePath,
        string entryRelativePath,
        long started,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(archivePath))
        {
            return OperationResult<DecodedTexture>.CreateFailure($"Archive not found: {archivePath}", Stopwatch.GetElapsedTime(started));
        }

        if (!BigArchiveReader.TryReadIndex(archivePath, out var index))
        {
            return OperationResult<DecodedTexture>.CreateFailure($"Failed to read archive index: {archivePath}", Stopwatch.GetElapsedTime(started));
        }

        string normKey = entryRelativePath.Replace('/', '\\').ToLowerInvariant();
        if (!index.TryGetValue(normKey, out var entry))
        {
            entry = index.Values.FirstOrDefault(e => string.Equals(e.Path.Replace('/', '\\'), entryRelativePath.Replace('/', '\\'), StringComparison.OrdinalIgnoreCase));
        }

        if (entry is null)
        {
            return OperationResult<DecodedTexture>.CreateFailure($"Archive entry not found: {archivePath}#{entryRelativePath}", Stopwatch.GetElapsedTime(started));
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            byte[] bytes = BigArchiveReader.ReadEntryData(entry);
            string ext = Path.GetExtension(entryRelativePath);
            if (codec.SupportsExtension(ext))
            {
                return codec.Decode(bytes, ext, entryRelativePath);
            }

            using var ms = new MemoryStream(bytes);
            using var image = await Image.LoadAsync<Rgba32>(ms, cancellationToken).ConfigureAwait(false);
            if ((long)image.Width * image.Height * 4 > int.MaxValue)
            {
                return OperationResult<DecodedTexture>.CreateFailure("Image dimensions exceed maximum supported pixel buffer size.", Stopwatch.GetElapsedTime(started));
            }

            var pixels = new byte[image.Width * image.Height * 4];
            image.CopyPixelDataTo(pixels);
            var texture = new DecodedTexture(image.Width, image.Height, pixels);
            return OperationResult<DecodedTexture>.CreateSuccess(texture, Stopwatch.GetElapsedTime(started));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is UnknownImageFormatException or InvalidImageContentException or IOException or EndOfStreamException or NotSupportedException or ArgumentException or InvalidOperationException)
        {
            logger.LogWarning(ex, "Failed to decode texture {Entry} from archive {Archive}", entryRelativePath, archivePath);
            return OperationResult<DecodedTexture>.CreateFailure($"Failed to decode texture from archive: {ex.Message}", Stopwatch.GetElapsedTime(started));
        }
    }

    private async Task<OperationResult<DecodedTexture>> LoadDecodedFromLooseFileAsync(
        string path,
        long started,
        CancellationToken cancellationToken)
    {
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
            if ((long)image.Width * image.Height * 4 > int.MaxValue)
            {
                return OperationResult<DecodedTexture>.CreateFailure("Image dimensions exceed maximum supported pixel buffer size.", Stopwatch.GetElapsedTime(started));
            }

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
}
