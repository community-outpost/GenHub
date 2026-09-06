using System;
using System.Globalization;
using System.IO;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using GenHub.Core.Constants;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converts a string file path or asset URI to a <see cref="Bitmap"/> for use as an image source.
/// Note: For asynchronous loading and caching of remote HTTP/HTTPS images, use
/// <see cref="Controls.ImageLoader.SourceProperty"/> rather than this synchronous converter.
/// </summary>
public class StringToImageConverter : IValueConverter
{
    /// <summary>
    /// Converts a string file path to a Bitmap for use as an image source.
    /// </summary>
    /// <inheritdoc/>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path))
            return null;

        try
        {
            // Handle avares:// URIs (embedded resources)
            if (path.StartsWith("avares://", StringComparison.OrdinalIgnoreCase))
            {
                // Ensure URI is well-formed for Avalonia
                var uri = new Uri(path);
                var asset = AssetLoader.Open(uri);
                return new Bitmap(asset);
            }

            // Handle relative asset paths (e.g., "/Assets/Logos/logo.png" or "Assets/Logos/logo.png")
            if (path.StartsWith("/Assets/", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                var cleanPath = path.TrimStart('/');
                var uri = new Uri($"avares://GenHub/{cleanPath}");
                var asset = AssetLoader.Open(uri);
                return new Bitmap(asset);
            }

            // Handle web URLs: return cached bitmap if available in memory.
            // Full asynchronous loading and caching should be done via ImageLoader.Source.
            if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return Services.ImageCacheService.Instance.GetBitmapFromMemory(path);
            }

            // Handle local file paths (reject UNC shares). Decode from a memory stream to avoid locking the file.
            if (Path.IsPathRooted(path) && !path.StartsWith(@"\\", StringComparison.Ordinal) && !path.StartsWith("//", StringComparison.Ordinal) && File.Exists(path))
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var ms = new MemoryStream();
                fs.CopyTo(ms);
                ms.Position = 0;
                return new Bitmap(ms);
            }

            return null;
        }
        catch
        {
            // Fallback for relative paths that might be intended for Avalonia's built-in converter
            return path;
        }
    }

    /// <summary>
    /// Not supported. Converts a Bitmap back to a string file path.
    /// </summary>
    /// <inheritdoc/>
    /// <returns>This method does not return a value; it always throws <see cref="NotSupportedException"/>.</returns>
    /// <exception cref="NotSupportedException">Always thrown as this converter only supports one-way conversion.</exception>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
