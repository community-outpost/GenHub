using System;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.AppUpdate.Interfaces;

/// <summary>
/// Coordinates background app update checks, periodic check scheduling, fallback discovery, and one-click installation.
/// </summary>
public interface IBackgroundUpdateCoordinator : IDisposable
{
    /// <summary>
    /// Initializes background update checking based on user settings and starts periodic timers if enabled.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the initialization operation.</returns>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs an immediate check for available updates in the background.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the update check operation.</returns>
    Task CheckForUpdatesAsync(CancellationToken cancellationToken = default);
}
