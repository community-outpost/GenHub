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
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The play outcome.</returns>
    Task<OperationResult<OnlinePlayResult>> PlayAsync(
        string profileId,
        string networkName,
        string overlayIp = "",
        CancellationToken cancellationToken = default);
}
