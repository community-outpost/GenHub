using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Models.Tools.GenHotkeys;

namespace GenHub.Core.Interfaces.Tools.GenHotkeys;

/// <summary>
/// Service for stamping hotkey badges onto icon textures and generating in-game TGAs.
/// </summary>
public interface IIconOverlayService
{
    /// <summary>
    /// Renders a hotkey badge onto the given icon image and exports it as a 32-bit TGA.
    /// </summary>
    /// <param name="sourceIconBytes">Raw image bytes (WebP, PNG, etc.).</param>
    /// <param name="hotkey">The character to overlay (e.g. 'A', 'F', 'G').</param>
    /// <param name="corner">The corner position for the badge.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>TGA binary file bytes.</returns>
    Task<byte[]> GenerateOverlayTgaAsync(
        byte[] sourceIconBytes,
        char hotkey,
        OverlayCorner corner,
        CancellationToken cancellationToken = default);
}
