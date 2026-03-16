using System;
using System.Globalization;
using System.IO;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using GenHub.Core.Constants;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converts a string file path to a Bitmap for use as an image source.
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

            // Handle relative asset paths (e.g., "/Assets/Logos/logo.png")
            if (path.StartsWith("/", StringComparison.Ordinal))
            {
                var uri = new Uri($"avares://GenHub{path}");
                var asset = AssetLoader.Open(uri);
                return new Bitmap(asset);
            }

            // Handle asset paths starting with 'Assets/'
            if (path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                var uri = new Uri($"avares://GenHub/{path}");
                var asset = AssetLoader.Open(uri);
                return new Bitmap(asset);
            }

            // Handle web URLs
            if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                // TODO: For web URLs, implement caching/downloading if needed
                return null;
            }

            // Handle local file paths
            if (Path.IsPathRooted(path) && File.Exists(path))
            {
                return new Bitmap(path);
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
    /// Not implemented. Converts a Bitmap back to a string file path.
    /// </summary>
    /// <inheritdoc/>
    /// <returns>This method does not return a value; it always throws <see cref="NotImplementedException"/>.</returns>
    /// <exception cref="NotImplementedException">Always thrown as this converter only supports one-way conversion.</exception>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
