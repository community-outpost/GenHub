using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Models.Results;

namespace GenHub.Core.Interfaces.AppUpdate;

/// <summary>
/// Interface for application update services.
/// </summary>
public interface IAppUpdateService
{
    /// <summary>
    /// Gets the current application version.
    /// </summary>
    /// <returns>The current version string.</returns>
    string GetCurrentVersion();

    /// <summary>
    /// Checks for available updates from the specified repository with a cancellation token.
    /// </summary>
    /// <param name="owner">The repository owner.</param>
    /// <param name="repositoryName">The repository name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The update check result.</returns>
    Task<UpdateCheckResult> CheckForUpdatesAsync(string owner, string repositoryName, CancellationToken cancellationToken = default);
}
