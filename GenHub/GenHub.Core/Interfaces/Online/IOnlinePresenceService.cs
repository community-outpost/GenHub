using GenHub.Core.Models.Online;
using GenHub.Core.Models.Results;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Interfaces.Online;

/// <summary>
/// Grant-scoped presence channel for a joined network: live roster fan-out
/// with heartbeat and reconnect with backoff.
/// </summary>
public interface IOnlinePresenceService
{
    /// <summary>
    /// Occurs when the live roster changes.
    /// </summary>
    event EventHandler<IReadOnlyList<OnlineMember>>? RosterUpdated;

    /// <summary>
    /// Occurs when presence is lost unrecoverably (evicted, banned, grant refused).
    /// </summary>
    event EventHandler? ConnectionLost;

    /// <summary>
    /// Gets a value indicating whether the presence channel is connected.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Connects the presence channel for a joined network.
    /// </summary>
    /// <param name="networkId">The joined network identifier.</param>
    /// <param name="grant">The short-lived join grant.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True when the channel is connected.</returns>
    Task<OperationResult<bool>> ConnectAsync(
        string networkId,
        string grant,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Disconnects the presence channel. Safe to call when already disconnected.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True when the channel is disconnected.</returns>
    Task<OperationResult<bool>> DisconnectAsync(CancellationToken cancellationToken = default);
}
