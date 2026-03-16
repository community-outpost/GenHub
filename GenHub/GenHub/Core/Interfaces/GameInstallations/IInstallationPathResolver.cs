using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Results;

namespace GenHub.Core.Interfaces.GameInstallations;

/// <summary>
/// Provides services for resolving and validating game installation paths.
/// </summary>
public interface IInstallationPathResolver
{
    /// <summary>
    /// Attempts to resolve the current path of a game installation that may have been moved or renamed.
    /// </summary>
    /// <param name="installation">The installation with a potentially stale path.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>An operation result containing the installation with updated path if found, or failure if not resolved.</returns>
    Task<OperationResult<GameInstallation>> ResolveInstallationPathAsync(
        GameInstallation installation,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates that an installation path exists and contains valid game files.
    /// </summary>
    /// <param name="installation">The installation to validate.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>An operation result indicating whether the path is valid.</returns>
    Task<OperationResult<bool>> ValidateInstallationPathAsync(
        GameInstallation installation,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches common installation locations for a game installation matching the given criteria.
    /// </summary>
    /// <param name="installation">The installation to search for (uses game type, installation type as hints).</param>
    /// <param name="gameDatHash">Optional game.dat hash to match against for precise identification.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>An operation result containing the found installation path, or failure if not found.</returns>
    Task<OperationResult<string>> SearchForInstallationAsync(
        GameInstallation installation,
        string? gameDatHash = null,
        CancellationToken cancellationToken = default);
}
