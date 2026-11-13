using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Interfaces.Storage;

/// <summary>
/// Service responsible for detecting and resolving installation collisions
/// (e.g. when GenHub runs from the default location while an existing custom installation exists).
/// </summary>
public interface IInstallationConflictService
{
    /// <summary>
    /// Checks for duplicate installation conflicts, adopts user data from custom installations if applicable,
    /// and notifies the user in the UI.
    /// </summary>
    /// <param name="cancellationToken">Optional token to cancel the asynchronous operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task CheckAndResolveConflictsAsync(CancellationToken cancellationToken = default);
}
