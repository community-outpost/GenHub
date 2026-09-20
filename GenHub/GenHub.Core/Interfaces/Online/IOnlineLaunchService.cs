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
    /// Resolves the network expected profile and launches it.
    /// </summary>
    /// <param name="expectedProfileId">The expected profile identifier from the join grant.</param>
    /// <param name="networkName">The joined network display name, for user feedback.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The play outcome.</returns>
    Task<OperationResult<OnlinePlayResult>> PlayAsync(
        string expectedProfileId,
        string networkName,
        CancellationToken cancellationToken = default);
}
