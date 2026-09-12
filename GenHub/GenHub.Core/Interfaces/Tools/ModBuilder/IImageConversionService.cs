using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Interfaces.Tools.ModBuilder;

/// <summary>
/// Service for converting image files between various formats used in C&amp;C Generals Zero Hour modding.
/// Supports PSD, TGA, TIFF, DDS, and BMP formats with advanced features like multi-alpha compositing,
/// resizing, and automatic DXT format selection.
/// </summary>
public interface IImageConversionService
{
    /// <summary>
    /// Converts an image from one format to another with optional processing parameters.
    /// </summary>
    /// <param name="sourcePath">Path to the source image file.</param>
    /// <param name="targetPath">Path to the target image file.</param>
    /// <param name="parameters">Optional conversion parameters (resize, rescale, resampling, etc.).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if conversion succeeded, false otherwise.</returns>
    Task<bool> ConvertImageAsync(
        string sourcePath,
        string targetPath,
        IDictionary<string, object>? parameters = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Detects if an image has an alpha channel.
    /// </summary>
    /// <param name="imagePath">Path to the image file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the image has an alpha channel, false otherwise.</returns>
    Task<bool> HasAlphaChannelAsync(string imagePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the recommended DDS compression format (DXT1 or DXT5) based on alpha channel presence.
    /// </summary>
    /// <param name="imagePath">Path to the image file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Recommended DXT format string ("DXT1" or "DXT5").</returns>
    Task<string> GetRecommendedDxtFormatAsync(string imagePath, CancellationToken cancellationToken = default);
}
