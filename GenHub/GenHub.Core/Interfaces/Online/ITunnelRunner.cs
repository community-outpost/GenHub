using GenHub.Core.Models.Results;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Interfaces.Online;

/// <summary>
/// Provides virtual LAN tunneling and packet routing for game sessions.
/// </summary>
public interface ITunnelRunner : IDisposable
{
    /// <summary>
    /// Gets a value indicating whether the tunnel is actively running.
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// Starts the tunnel runner with the specified adapter configuration and overlay IP.
    /// </summary>
    /// <param name="adapterConfig">The base64-encoded or raw JSON adapter config.</param>
    /// <param name="overlayIp">The local member's overlay IP.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result indicating success or failure.</returns>
    Task<OperationResult<bool>> StartAsync(string adapterConfig, string overlayIp, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the tunnel runner.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result indicating success or failure.</returns>
    Task<OperationResult<bool>> StopAsync(CancellationToken cancellationToken = default);
}
