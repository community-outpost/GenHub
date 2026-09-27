using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace GenHub.Common.Helpers;

/// <summary>
/// Reusable helper for unified clipboard extraction of files, images, URLs, and data URIs across all views and input fields.
/// </summary>
public static class ClipboardInputHelper
{
    private static readonly string[] ImageFormats =
    [
        "image/png",
        "PNG",
        "image/jpeg",
        "image/jpg",
        "image/webp",
        "image/x-png",
        "image/bmp",
        "Bitmap",
        "DeviceIndependentBitmap",
        "CF_DIB",
    ];

    private static readonly string[] FileFormats =
    [
        DataFormats.Files,
        "FileDrop",
        "FileName",
        "FileNameW",
    ];

    /// <summary>
    /// Extracts a single file path, image path, or URL from the clipboard.
    /// Handles clipboard images (converting DIB/raw bitmaps to disk files), file drops, data URIs, and URL/path strings.
    /// </summary>
    /// <param name="clipboard">The system clipboard.</param>
    /// <returns>A local file path or URL string, or null if no valid input is present in clipboard.</returns>
    public static async Task<string?> ExtractPastedFileOrImageAsync(IClipboard? clipboard)
    {
        if (clipboard == null)
        {
            return null;
        }

        try
        {
            // 1. Check plain text first for URLs, local paths, or base64 data URIs
            var text = await clipboard.GetTextAsync();
            var textResult = TryExtractFromText(text);
            if (textResult != null)
            {
                return textResult;
            }

            // 2. Check files in clipboard
            var files = await ExtractFilesFromClipboardAsync(clipboard);
            if (files.Count > 0)
            {
                return files[0];
            }

            // 3. Check image data in clipboard
            var imageFile = await TryExtractImageBytesFromClipboardAsync(clipboard);
            if (!string.IsNullOrWhiteSpace(imageFile) && File.Exists(imageFile))
            {
                return imageFile;
            }
        }
        catch
        {
            // Suppress clipboard reading errors gracefully
        }

        return null;
    }

    /// <summary>
    /// Extracts all file paths present in the clipboard.
    /// </summary>
    /// <param name="clipboard">The system clipboard.</param>
    /// <returns>List of local file paths.</returns>
    public static async Task<IReadOnlyList<string>> ExtractFilesFromClipboardAsync(IClipboard? clipboard)
    {
        if (clipboard == null)
        {
            return [];
        }

        try
        {
            var formats = (await clipboard.GetFormatsAsync()) ?? [];

            foreach (var fileFormat in FileFormats)
            {
                if (!formats.Contains(fileFormat, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                var data = await clipboard.GetDataAsync(fileFormat);
                if (data == null)
                {
                    continue;
                }

                var paths = ExtractPathsFromObject(data);
                if (paths.Count > 0)
                {
                    return paths;
                }
            }
        }
        catch
        {
            // Suppress clipboard errors
        }

        return [];
    }

    /// <summary>
    /// Extracts all file paths, pasted image paths, or URLs present in the clipboard.
    /// </summary>
    /// <param name="clipboard">The system clipboard.</param>
    /// <param name="destinationDirectory">Optional destination directory for extracted images.</param>
    /// <returns>List of local file paths or URLs.</returns>
    public static async Task<IReadOnlyList<string>> ExtractClipboardPathsAsync(
        IClipboard? clipboard,
        string? destinationDirectory = null)
    {
        if (clipboard == null)
        {
            return [];
        }

        var files = await ExtractFilesFromClipboardAsync(clipboard);
        if (files.Count > 0)
        {
            return files;
        }

        var single = await ExtractPastedFileOrImageAsync(clipboard);
        if (!string.IsNullOrWhiteSpace(single))
        {
            return [single];
        }

        return [];
    }

    private static string? TryExtractFromText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var trimmed = text.Trim().Trim('"', '\'');

        if (trimmed.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
        {
            return TrySaveDataUriImage(trimmed);
        }

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return trimmed;
        }

        if (File.Exists(trimmed) || Directory.Exists(trimmed))
        {
            return trimmed;
        }

        return null;
    }

    private static List<string> ExtractPathsFromObject(object data)
    {
        var paths = new List<string>();
        switch (data)
        {
            case IEnumerable<IStorageItem> storageItems:
                ExtractStorageItemPaths(storageItems, paths);
                break;

            case IEnumerable<string> stringPaths:
                ExtractStringPaths(stringPaths, paths);
                break;

            case string singlePath:
                ExtractSinglePath(singlePath, paths);
                break;
        }

        return paths;
    }

    private static void ExtractStorageItemPaths(IEnumerable<IStorageItem> storageItems, List<string> results)
    {
        foreach (var item in storageItems)
        {
            var localPath = item.Path.LocalPath;
            if (!string.IsNullOrWhiteSpace(localPath) && (File.Exists(localPath) || Directory.Exists(localPath)))
            {
                results.Add(localPath);
            }
        }
    }

    private static void ExtractStringPaths(IEnumerable<string> paths, List<string> results)
    {
        foreach (var path in paths)
        {
            var trimmed = path?.Trim().Trim('"', '\'');
            if (!string.IsNullOrWhiteSpace(trimmed) && (File.Exists(trimmed) || Directory.Exists(trimmed)))
            {
                results.Add(trimmed);
            }
        }
    }

    private static void ExtractSinglePath(string singlePath, List<string> results)
    {
        var cleanSingle = singlePath.Trim().Trim('"', '\'');
        if (File.Exists(cleanSingle) || Directory.Exists(cleanSingle))
        {
            results.Add(cleanSingle);
        }
    }

    private static async Task<string?> TryExtractImageBytesFromClipboardAsync(
        IClipboard clipboard,
        string? destinationDirectory = null)
    {
        var formats = (await clipboard.GetFormatsAsync()) ?? [];

        foreach (var format in ImageFormats)
        {
            var matchedFormat = formats.FirstOrDefault(f => string.Equals(f, format, StringComparison.OrdinalIgnoreCase));
            if (matchedFormat == null)
            {
                continue;
            }

            var data = await clipboard.GetDataAsync(matchedFormat);
            if (data == null)
            {
                continue;
            }

            var path = await TrySaveImageDataAsync(format, data, destinationDirectory);
            if (!string.IsNullOrWhiteSpace(path))
            {
                return path;
            }
        }

        return null;
    }

    private static async Task<string?> TrySaveImageDataAsync(
        string format,
        object data,
        string? destinationDirectory)
    {
        if (data is byte[] bytes && bytes.Length > 0)
        {
            return await SaveRawImageBytesAsync(format, bytes, destinationDirectory);
        }

        if (data is Stream stream && stream.Length > 0)
        {
            return await SaveImageStreamAsync(stream, destinationDirectory);
        }

        return null;
    }

    private static async Task<string?> SaveRawImageBytesAsync(
        string format,
        byte[] bytes,
        string? destinationDirectory)
    {
        if (string.Equals(format, "DeviceIndependentBitmap", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(format, "CF_DIB", StringComparison.OrdinalIgnoreCase))
        {
            return SaveDibBytesAsBmp(bytes, destinationDirectory);
        }

        var ext = GetImageExtension(format);
        var dir = destinationDirectory ?? Path.GetTempPath();
        Directory.CreateDirectory(dir);
        var targetPath = Path.Combine(dir, $"pasted_img_{Guid.NewGuid():N}{ext}");
        await File.WriteAllBytesAsync(targetPath, bytes);
        return targetPath;
    }

    private static async Task<string?> SaveImageStreamAsync(Stream stream, string? destinationDirectory)
    {
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        var streamBytes = ms.ToArray();
        if (streamBytes.Length > 0)
        {
            var dir = destinationDirectory ?? Path.GetTempPath();
            Directory.CreateDirectory(dir);
            var targetPath = Path.Combine(dir, $"pasted_img_{Guid.NewGuid():N}.png");
            await File.WriteAllBytesAsync(targetPath, streamBytes);
            return targetPath;
        }

        return null;
    }

    private static string GetImageExtension(string format)
    {
        if (format.Contains("jpeg", StringComparison.OrdinalIgnoreCase) ||
            format.Contains("jpg", StringComparison.OrdinalIgnoreCase))
        {
            return ".jpg";
        }

        if (format.Contains("webp", StringComparison.OrdinalIgnoreCase))
        {
            return ".webp";
        }

        return ".png";
    }

    private static string? SaveDibBytesAsBmp(byte[] dibBytes, string? destinationDirectory = null)
    {
        try
        {
            if (dibBytes.Length < 40)
            {
                return null;
            }

            // DIB structure begins with BITMAPINFOHEADER (biSize = 40 bytes)
            var biSize = BitConverter.ToUInt32(dibBytes, 0);
            var biBitCount = BitConverter.ToUInt16(dibBytes, 14);
            var biCompression = BitConverter.ToUInt32(dibBytes, 16);
            var biClrUsed = BitConverter.ToUInt32(dibBytes, 32);

            var colorTableSize = 0u;
            if (biBitCount <= 8)
            {
                var colors = biClrUsed == 0 ? (1u << biBitCount) : biClrUsed;
                colorTableSize = colors * 4;
            }
            else if (biCompression == 3 && biSize == 40)
            {
                // BI_BITFIELDS: 3 masks * 4 bytes
                colorTableSize = 12;
            }

            var bfOffBits = 14 + biSize + colorTableSize;
            var bfSize = 14 + (uint)dibBytes.Length;

            var bmpFileBytes = new byte[14 + dibBytes.Length];

            // BITMAPFILEHEADER
            bmpFileBytes[0] = 0x42; // 'B'
            bmpFileBytes[1] = 0x4D; // 'M'
            BitConverter.GetBytes(bfSize).CopyTo(bmpFileBytes, 2);
            bmpFileBytes[6] = 0;
            bmpFileBytes[7] = 0;
            bmpFileBytes[8] = 0;
            bmpFileBytes[9] = 0;
            BitConverter.GetBytes(bfOffBits).CopyTo(bmpFileBytes, 10);

            // Copy DIB bytes following the file header
            Buffer.BlockCopy(dibBytes, 0, bmpFileBytes, 14, dibBytes.Length);

            var dir = destinationDirectory ?? Path.GetTempPath();
            Directory.CreateDirectory(dir);
            var targetBmp = Path.Combine(dir, $"pasted_img_{Guid.NewGuid():N}.bmp");
            File.WriteAllBytes(targetBmp, bmpFileBytes);
            return targetBmp;
        }
        catch
        {
            return null;
        }
    }

    private static string? TrySaveDataUriImage(string dataUri, string? destinationDirectory = null)
    {
        try
        {
            var commaIndex = dataUri.IndexOf(',');
            if (commaIndex <= 0)
            {
                return null;
            }

            var header = dataUri[..commaIndex];
            var base64Data = dataUri[(commaIndex + 1)..];

            var ext = ".png";
            if (header.Contains("jpeg", StringComparison.OrdinalIgnoreCase) || header.Contains("jpg", StringComparison.OrdinalIgnoreCase))
            {
                ext = ".jpg";
            }
            else if (header.Contains("webp", StringComparison.OrdinalIgnoreCase))
            {
                ext = ".webp";
            }
            else if (header.Contains("bmp", StringComparison.OrdinalIgnoreCase))
            {
                ext = ".bmp";
            }

            var bytes = Convert.FromBase64String(base64Data);
            var dir = destinationDirectory ?? Path.GetTempPath();
            Directory.CreateDirectory(dir);
            var targetPath = Path.Combine(dir, $"pasted_img_{Guid.NewGuid():N}{ext}");
            File.WriteAllBytes(targetPath, bytes);
            return targetPath;
        }
        catch
        {
            return null;
        }
    }
}
