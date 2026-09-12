using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Tools.GenHotkeys;

namespace GenHub.Core.Interfaces.Tools.GenHotkeys;

/// <summary>
/// Service for parsing and navigating the Generals and Zero Hour tech trees.
/// </summary>
public interface ITechTreeService
{
    /// <summary>
    /// Loads all factions, game objects, and keyboard layouts for the specified game.
    /// </summary>
    /// <param name="gameType">The target game.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of loaded factions.</returns>
    Task<IReadOnlyList<HotkeyFaction>> LoadTechTreeAsync(GameType gameType, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets raw WebP image bytes for the requested icon.
    /// </summary>
    /// <param name="iconName">The icon name without extension.</param>
    /// <param name="gameType">The target game.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The raw image bytes, or null if not found.</returns>
    Task<byte[]?> GetIconBytesAsync(string iconName, GameType gameType, CancellationToken cancellationToken = default);
}
