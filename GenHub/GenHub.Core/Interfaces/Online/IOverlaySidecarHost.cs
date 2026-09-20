using GenHub.Core.Models.Results;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Interfaces.Online;

/// <summary>
/// Manages an overlay sidecar process: config staging, spawn, supervised
/// logging, health, and teardown. Overlay-specific details (binary, arguments)
/// come from <see cref="IOverlaySidecarLocator"/>.
/// </summary>
public interface IOverlaySidecarHost
{
    /// <summary>
    /// Gets a value indicating whether the sidecar process is running.
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// Starts the sidecar with the given opaque configuration.
    /// </summary>
    /// <param name="configContents">The opaque overlay configuration.</param>
    /// <param name="locator">The platform sidecar locator.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The running sidecar info.</returns>
    Task<OperationResult<SidecarInfo>> StartAsync(
        string configContents,
        IOverlaySidecarLocator locator,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the sidecar and removes staged configuration. Safe to call when idle.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True when the sidecar is stopped.</returns>
    Task<OperationResult<bool>> StopAsync(CancellationToken cancellationToken = default);
}
