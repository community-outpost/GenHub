using GenHub.Core.Models.GameInstallations;

namespace GenHub.Core.Interfaces.Storage;

/// <summary>
/// Selects and persists an effective installation CAS pool from detected installations.
/// </summary>
public interface IInstallationCasPoolService
{
    /// <summary>
    /// Ensures installation-pool settings reflect the currently detected installations.
    /// </summary>
    /// <param name="installations">The detected game installations.</param>
    /// <param name="cancellationToken">A token that can cancel the settings update.</param>
    /// <returns><c>true</c> when content acquisition may continue; otherwise, <c>false</c>.</returns>
    Task<bool> EnsurePoolPathAsync(
        IReadOnlyList<GameInstallation> installations,
        CancellationToken cancellationToken = default);
}
