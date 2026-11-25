using GenHub.Core.Models.Tools.GenHotkeys;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Interfaces.Tools.GenHotkeys;

/// <summary>
/// Service for stamping hotkey badges onto icon textures and generating standard 60x48 in-game TGAs.
/// </summary>
public interface IIconOverlayService
{
    /// <summary>
    /// Renders a hotkey badge onto the given icon image, resizes it to standard 60x48 SAGE cameo dimensions, and exports it as an uncompressed 32-bit BGRA TGA.
    /// </summary>
    /// <param name="sourceIconBytes">Raw image bytes (WebP, PNG, etc.).</param>
    /// <param name="hotkey">The character to overlay (e.g. 'A', 'F', 'G').</param>
    /// <param name="corner">The corner position for the badge.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>TGA binary file bytes formatted as 60x48 32-bit BGRA.</returns>
    /// <exception cref="System.ArgumentNullException">Thrown when <paramref name="sourceIconBytes"/> is null.</exception>
    /// <exception cref="System.IO.InvalidDataException">Thrown when the source bytes cannot be decoded by any supported image codec.</exception>
    /// <exception cref="System.OperationCanceledException">Thrown when the operation is canceled via the cancellation token.</exception>
    Task<byte[]> GenerateOverlayTgaAsync(
        byte[] sourceIconBytes,
        char hotkey,
        OverlayCorner corner,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Converts the source image bytes directly into a standard 60x48 32-bit BGRA TGA format without a badge overlay.
    /// </summary>
    /// <param name="sourceIconBytes">Raw image bytes (PNG, JPEG, BMP, etc.).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>TGA binary file bytes formatted as 60x48 32-bit BGRA.</returns>
    /// <exception cref="System.ArgumentNullException">Thrown when <paramref name="sourceIconBytes"/> is null.</exception>
    /// <exception cref="System.IO.InvalidDataException">Thrown when the source bytes cannot be decoded by any supported image codec.</exception>
    /// <exception cref="System.OperationCanceledException">Thrown when the operation is canceled via the cancellation token.</exception>
    Task<byte[]> ConvertToTgaAsync(
        byte[] sourceIconBytes,
        CancellationToken cancellationToken = default);
}
