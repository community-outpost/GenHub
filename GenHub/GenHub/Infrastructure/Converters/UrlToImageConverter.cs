using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converts a URL string to a Bitmap for use as an image source.
/// Supports local assets (avares://), web URLs (http/https), and caches downloaded images.
/// </summary>
public class UrlToImageConverter : IValueConverter
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(10),
    };

    private static readonly ConcurrentDictionary<string, Bitmap?> ImageCache = new();

    /// <summary>
    /// Converts a URL string to a Bitmap.
    /// </summary>
    /// <inheritdoc/>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string url || string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        try
        {
            // Handle avares:// URIs (embedded resources)
            if (url.StartsWith("avares://", StringComparison.OrdinalIgnoreCase))
            {
                var uri = new Uri(url);
                var asset = AssetLoader.Open(uri);
                return new Bitmap(asset);
            }

            // Handle local asset paths starting with /Assets
            if (url.StartsWith("/Assets", StringComparison.OrdinalIgnoreCase))
            {
                var uri = new Uri($"avares://GenHub{url}");
                var asset = AssetLoader.Open(uri);
                return new Bitmap(asset);
            }

            // Handle web URLs
            if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                // Check cache first
                if (ImageCache.TryGetValue(url, out var cached))
                {
                    return cached;
                }

                // Start async download and return null for now
                _ = DownloadImageAsync(url);
                return null;
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Not implemented. Converts a Bitmap back to a URL string.
    /// </summary>
    /// <inheritdoc/>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }

    private static async Task DownloadImageAsync(string url)
    {
        try
        {
            var response = await HttpClient.GetAsync(url);
            if (response.IsSuccessStatusCode)
            {
                var bytes = await response.Content.ReadAsByteArrayAsync();
                using var stream = new MemoryStream(bytes);
                var bitmap = new Bitmap(stream);
                ImageCache[url] = bitmap;
            }
            else
            {
                ImageCache[url] = null;
            }
        }
        catch
        {
            ImageCache[url] = null;
        }
    }
}
