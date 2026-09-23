using GenHub.Core.Models.Online;
using GenHub.Core.Models.Results;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Interfaces.Online;

/// <summary>
/// Launches the expected game profile for a joined network: profile lookup,
/// workspace reconciliation, and launch through the existing launcher facade.
/// </summary>
public interface IOnlineLaunchService
{
    /// <summary>
    /// Resolves the selected local profile, preselects the overlay IP for it,
    /// and launches it.
    /// </summary>
    /// <param name="profileId">The local profile identifier to launch.</param>
    /// <param name="networkName">The joined network display name, for user feedback.</param>
    /// <param name="overlayIp">The member overlay IP to preselect in the game network settings.</param>
    /// <param name="nickname">The LAN nickname to sync into the launched game's Network.ini. Blank leaves the file untouched.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The play outcome.</returns>
    Task<OperationResult<OnlinePlayResult>> PlayAsync(
        string profileId,
        string networkName,
        string overlayIp = "",
        string nickname = "",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the game previously launched through <see cref="PlayAsync"/>.
    /// Stopping an already-stopped profile succeeds.
    /// </summary>
    /// <param name="profileId">The local profile identifier to stop.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The stop outcome.</returns>
    Task<OperationResult<bool>> StopAsync(string profileId, CancellationToken cancellationToken = default);
}
