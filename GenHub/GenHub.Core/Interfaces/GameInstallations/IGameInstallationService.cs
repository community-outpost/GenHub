using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Results;

namespace GenHub.Core.Interfaces.GameInstallations;

/// <summary>
/// Provides services for managing and retrieving game installations.
/// </summary>
public interface IGameInstallationService
{
    /// <summary>
    /// Gets a game installation by its unique identifier.
    /// </summary>
    /// <param name="installationId">The unique identifier of the installation (a GUID string, e.g., "550e8400-e29b-41d4-a716-446655440000").</param>
    /// <param name="cancellationToken">A cancellation token to observe while waiting for the task to complete.</param>
    /// <returns>An operation result containing the game installation if found.</returns>
    Task<OperationResult<GameInstallation>> GetInstallationAsync(string installationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all available game installations.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token to observe while waiting for the task to complete.</param>
    /// <returns>An operation result containing all game installations.</returns>
    Task<OperationResult<IReadOnlyList<GameInstallation>>> GetAllInstallationsAsync(CancellationToken cancellationToken = default);
}
