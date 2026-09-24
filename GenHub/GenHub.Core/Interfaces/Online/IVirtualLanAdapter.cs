using GenHub.Core.Models.Online;
using GenHub.Core.Models.Results;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Interfaces.Online;

/// <summary>
/// Platform virtual LAN adapter lifecycle. Implemented per OS host; the shared
/// client only programs it through this contract.
/// </summary>
public interface IVirtualLanAdapter
{
    /// <summary>
    /// Gets the current adapter state.
    /// </summary>
    OnlineAdapterState State { get; }

    /// <summary>
    /// Gets the active overlay IP address, or null when the adapter is down.
    /// </summary>
    string? OverlayIp { get; }

    /// <summary>
    /// Gets the last error that occurred during adapter operations, or null if healthy.
    /// </summary>
    string? LastError { get; }

    /// <summary>
    /// Occurs when the adapter state changes.
    /// </summary>
    event EventHandler<OnlineAdapterState>? StateChanged;

    /// <summary>
    /// Brings the adapter up with the given overlay configuration.
    /// </summary>
    /// <param name="adapterConfig">The opaque overlay configuration from the join grant.</param>
    /// <param name="overlayIp">The assigned overlay IP address.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True when the adapter is up.</returns>
    Task<OperationResult<bool>> BringUpAsync(
        string adapterConfig,
        string overlayIp,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Tears the adapter down and removes overlay routes. Safe to call when already down.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True when the adapter is down.</returns>
    Task<OperationResult<bool>> TearDownAsync(CancellationToken cancellationToken = default);
}
