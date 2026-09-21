using GenHub.Core.Models.Online;
using GenHub.Core.Models.Results;
using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Interfaces.Online;

/// <summary>
/// Client-side P2P connection service with NAT traversal.
/// Covers endpoint discovery (STUN), listening, and peer dialing.
/// </summary>
public interface IP2PConnectionService
{
    /// <summary>
    /// Gets the current connection quality.
    /// </summary>
    OnlineConnectionQuality CurrentQuality { get; }

    /// <summary>
    /// Occurs when the P2P connection status changes.
    /// </summary>
    event EventHandler<OnlineConnectionQuality>? ConnectionStatusChanged;

    /// <summary>
    /// Starts listening on the given port for inbound peer traffic.
    /// </summary>
    /// <param name="port">The local port to listen on, or 0 for an ephemeral port.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The bound local endpoint.</returns>
    Task<OperationResult<IPEndPoint>> StartListeningAsync(
        int port,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Connects to a peer at the given address and port.
    /// </summary>
    /// <param name="ipAddress">The peer IP address.</param>
    /// <param name="port">The peer port.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True when the peer connection was established.</returns>
    Task<OperationResult<bool>> ConnectToPeerAsync(
        string ipAddress,
        int port,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the local and STUN-resolved public endpoints.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The local and public endpoints.</returns>
    Task<OperationResult<P2PEndpoints>> GetLocalAndPublicEndpointsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops listening and releases all peer connections.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result of the stop operation.</returns>
    Task<OperationResult<bool>> StopListeningAsync(CancellationToken cancellationToken = default);
}
