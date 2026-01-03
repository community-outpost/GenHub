using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Models.GameInstallations;

namespace GenHub.Features.GameInstallations;

/// <summary>
/// Interface for persisting manually registered game installations.
/// </summary>
public interface IManualInstallationStorage
{
    /// <summary>
    /// Gets all persisted manual installations.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A list of manual installations.</returns>
    Task<List<GameInstallation>> LoadManualInstallationsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves a manual installation to persistent storage.
    /// </summary>
    /// <param name="installation">The installation to save.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task SaveManualInstallationAsync(GameInstallation installation, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a manual installation from persistent storage.
    /// </summary>
    /// <param name="installationId">The installation ID to remove.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task RemoveManualInstallationAsync(string installationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears all manual installations from persistent storage.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task ClearAllAsync(CancellationToken cancellationToken = default);
}
