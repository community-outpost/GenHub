using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Models.GitHub;

namespace GenHub.Core.Interfaces.GitHub;

/// <summary>
/// Interface for GitHub API client operations.
/// </summary>
public interface IGitHubApiClient
{
    /// <summary>
    /// Gets the latest release from the specified repository.
    /// </summary>
    /// <param name="owner">The repository owner.</param>
    /// <param name="repositoryName">The repository name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The latest GitHub release.</returns>
    Task<GitHubRelease> GetLatestReleaseAsync(
        string owner,
        string repositoryName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the authentication token for GitHub API requests.
    /// </summary>
    /// <param name="token">The GitHub token.</param>
    void SetAuthenticationToken(string token);
}
