using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;

namespace GenHub.Infrastructure.Services;

/// <summary>
/// Service interface for downloading and caching images in memory and on disk.
/// </summary>
public interface IImageCacheService
{
    /// <summary>
    /// Synchronously checks if a bitmap is already cached in memory.
    /// </summary>
    /// <param name="url">The image URL or local file path.</param>
    /// <returns>The cached <see cref="Bitmap"/> if present; otherwise, <see langword="null"/>.</returns>
    Bitmap? GetBitmapFromMemory(string? url);

    /// <summary>
    /// Asynchronously gets a bitmap from memory, disk cache, local file, or web.
    /// </summary>
    /// <param name="url">The image URL or local file path.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The loaded <see cref="Bitmap"/>, or <see langword="null"/> if loading failed.</returns>
    Task<Bitmap?> GetBitmapAsync(string? url, CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears the in-memory cache without disposing bitmaps that may still be in use by UI controls.
    /// </summary>
    void ClearMemoryCache();
}
