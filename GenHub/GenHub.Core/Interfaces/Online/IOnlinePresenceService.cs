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
    /// Gets a value indicating whether the presence channel is connected.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Occurs when the live roster changes.
    /// </summary>
    event EventHandler<IReadOnlyList<OnlineMember>>? RosterUpdated;

    /// <summary>
    /// Occurs when presence is lost unrecoverably (evicted, banned, grant refused).
    /// </summary>
    event EventHandler? ConnectionLost;

    /// <summary>
    /// Occurs when the join grant is proactively refreshed before expiry.
    /// Carries the full cert, including the fresh overlay adapter config.
    /// </summary>
    event EventHandler<OnlineCertResult>? GrantRefreshed;

    /// <summary>
    /// Occurs when the host switches the lobby's expected profile.
    /// </summary>
    event EventHandler<OnlineExpectedProfile>? ExpectedProfileChanged;

    /// <summary>
    /// Sets the local profile advertisement attached to heartbeats so the
    /// roster shows per-member setup match state.
    /// </summary>
    /// <param name="fingerprint">The local profile fingerprint.</param>
    /// <param name="profileName">The local profile display name.</param>
    /// <param name="displayName">The player name shown in the lobby roster. Blank keeps the edge default.</param>
    void UpdateAdvertisedProfile(string fingerprint, string profileName, string displayName = "");

    /// <summary>
    /// Connects the presence channel for a joined network.
    /// </summary>
    /// <param name="networkId">The joined network identifier.</param>
    /// <param name="grant">The short-lived join grant.</param>
    /// <param name="grantExpiresUtc">The grant expiry, used to schedule proactive refresh.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True when the channel is connected.</returns>
    Task<OperationResult<bool>> ConnectAsync(
        string networkId,
        string grant,
        DateTime grantExpiresUtc = default,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Disconnects the presence channel. Safe to call when already disconnected.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True when the channel is disconnected.</returns>
    Task<OperationResult<bool>> DisconnectAsync(CancellationToken cancellationToken = default);
}
