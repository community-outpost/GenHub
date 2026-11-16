using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Interfaces.Content;

/// <summary>
/// Supplies manifest IDs that must be pinned and protected from automated deletion during updates and cleanup.
/// </summary>
public interface IPinnedManifestProvider
{
    /// <summary>
    /// Gets manifest IDs that are actively required and should not be removed during automatic cleanup.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A set of pinned manifest IDs.</returns>
    Task<IReadOnlySet<string>> GetPinnedManifestIdsAsync(CancellationToken cancellationToken = default);
}
