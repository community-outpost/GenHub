using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Tools.ReplayManager;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Interfaces.Tools.ReplayManager;

/// <summary>
/// Resolves whether the maps that replays were recorded on are available locally.
/// </summary>
public interface IReplayMapLookupService
{
    /// <summary>
    /// Sets <see cref="ReplayFile.MapAvailability"/> on each replay by matching its map CRC and size
    /// against the user maps and the stock maps of the installed game.
    /// </summary>
    /// <param name="version">The game version the replays belong to.</param>
    /// <param name="replays">The replays to resolve.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task ResolveMapAvailabilityAsync(GameType version, IReadOnlyList<ReplayFile> replays, CancellationToken cancellationToken = default);
}
