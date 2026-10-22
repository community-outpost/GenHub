using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Interfaces.Storage;

/// <summary>
/// Performs automated storage maintenance and legacy directory cleanup across GenHub data roots.
/// </summary>
public interface IStorageMaintenanceService
{
    /// <summary>
    /// Executes maintenance tasks to consolidate cache folders, migrate legacy files, and clean up empty legacy directories.
    /// </summary>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task RunMaintenanceAsync(CancellationToken cancellationToken = default);
}
