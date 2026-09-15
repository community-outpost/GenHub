using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Tools.ReplayManager;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Interfaces.Tools.ReplayManager;

/// <summary>
/// Manages replay directory operations.
/// </summary>
public interface IReplayDirectoryService
{
    /// <summary>
    /// Gets the replay directory path for the specified game version.
    /// </summary>
    /// <param name="version">The game version.</param>
    /// <returns>The path to the replay directory.</returns>
    string GetReplayDirectory(GameType version);

    /// <summary>
    /// Ensures the replay directory exists, creating it if necessary.
    /// </summary>
    /// <param name="version">The game version.</param>
    void EnsureDirectoryExists(GameType version);

    /// <summary>
    /// Gets all replay files for the specified game version.
    /// </summary>
    /// <param name="version">The game version.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A list of replay files.</returns>
    Task<IReadOnlyList<ReplayFile>> GetReplaysAsync(GameType version, CancellationToken ct = default);

    /// <summary>
    /// Deletes the specified replay files (moves to Recycle Bin).
    /// </summary>
    /// <param name="replays">The replays to delete.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if deletion was successful.</returns>
    Task<bool> DeleteReplaysAsync(IEnumerable<ReplayFile> replays, CancellationToken ct = default);

    /// <summary>
    /// Opens the replay directory in Windows Explorer.
    /// </summary>
    /// <param name="version">The game version.</param>
    void OpenInExplorer(GameType version);

    /// <summary>
    /// Reveals a specific file in Windows Explorer.
    /// </summary>
    /// <param name="replay">The replay file to reveal.</param>
    void RevealInExplorer(ReplayFile replay);
}
