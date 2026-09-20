using GenHub.Core.Models.Online;
using GenHub.Core.Models.Results;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Interfaces.Online;

/// <summary>
/// Client transport for the Online network directory, join flow, and presence.
/// Directory calls are anonymous and metadata-only; member data requires a join grant.
/// </summary>
public interface IOnlineNetworkService
{
    /// <summary>
    /// Occurs when the joined roster changes.
    /// </summary>
    event EventHandler<IReadOnlyList<OnlineMember>>? RosterChanged;

    /// <summary>
    /// Occurs when presence is lost unrecoverably and the client auto-left.
    /// </summary>
    event EventHandler? ConnectionLost;

    /// <summary>
    /// Occurs when the host switches the lobby's expected profile.
    /// </summary>
    event EventHandler<OnlineExpectedProfile>? ExpectedProfileChanged;

    /// <summary>
    /// Gets the currently joined network, or null when not joined.
    /// </summary>
    OnlineJoinResult? CurrentJoin { get; }

    /// <summary>
    /// Gets the local reflexive endpoint published to members, or empty when hidden.
    /// </summary>
    string LocalEndpoint { get; }

    /// <summary>
    /// Gets the current virtual LAN adapter state. The lobby stays usable
    /// (roster, presence) while the adapter is down; only tunneling waits.
    /// </summary>
    OnlineAdapterState AdapterState { get; }

    /// <summary>
    /// Gets the public network directory (metadata only, no endpoints).
    /// </summary>
    /// <param name="search">Optional server-side metadata search text.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The directory entries.</returns>
    Task<OperationResult<IReadOnlyList<OnlineNetworkSummary>>> GetNetworksAsync(
        string? search = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets pre-join detail for a network (still no endpoints).
    /// </summary>
    /// <param name="networkId">The network identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The network detail.</returns>
    Task<OperationResult<OnlineNetworkDetail>> GetNetworkDetailAsync(
        string networkId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a network and joins it as host.
    /// </summary>
    /// <param name="request">The creation request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The join outcome.</returns>
    Task<OperationResult<OnlineJoinResult>> CreateNetworkAsync(
        OnlineCreateNetworkRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Joins a network: password check, grant, overlay config, roster.
    /// </summary>
    /// <param name="networkId">The network identifier.</param>
    /// <param name="password">The network password.</param>
    /// <param name="preferRelay">When true, offer only relay candidates (hide direct endpoint). Defaults to relay for privacy.</param>
    /// <param name="profileFingerprint">The local profile fingerprint advertised to the roster.</param>
    /// <param name="profileName">The local profile display name advertised to the roster.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The join outcome.</returns>
    Task<OperationResult<OnlineJoinResult>> JoinNetworkAsync(
        string networkId,
        string password,
        bool preferRelay = true,
        string profileFingerprint = "",
        string profileName = "",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Leaves the current network and tears down overlay state.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result of the leave operation.</returns>
    Task<OperationResult<bool>> LeaveNetworkAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the joined network description and expected profile (host only).
    /// </summary>
    /// <param name="description">The new description, or null to keep it.</param>
    /// <param name="expectedProfile">The new expected profile, or null to keep it.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result of the update operation.</returns>
    Task<OperationResult<bool>> UpdateNetworkAsync(
        string? description,
        OnlineExpectedProfile? expectedProfile,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the local profile advertisement attached to presence heartbeats.
    /// </summary>
    /// <param name="fingerprint">The local profile fingerprint.</param>
    /// <param name="profileName">The local profile display name.</param>
    void SetLocalProfileAdvertisement(string fingerprint, string profileName);

    /// <summary>
    /// Reports a joined member for abuse.
    /// </summary>
    /// <param name="overlayIp">The member overlay IP.</param>
    /// <param name="reason">The abuse reason.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result of the report operation.</returns>
    Task<OperationResult<bool>> ReportMemberAsync(
        string overlayIp,
        string reason,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Bans a joined member (host only).
    /// </summary>
    /// <param name="overlayIp">The member overlay IP.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result of the ban operation.</returns>
    Task<OperationResult<bool>> BanMemberAsync(
        string overlayIp,
        CancellationToken cancellationToken = default);
}
