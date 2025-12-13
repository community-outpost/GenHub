using System.Security;
using GenHub.Core.Models.GitHub;

namespace GenHub.Core.Interfaces.GitHub;

/// <summary>
/// Interface for GitHub API client operations.
/// </summary>
public interface IGitHubApiClient
{
    /// <summary>
    /// Gets a value indicating whether the client is authenticated.
    /// </summary>
    bool IsAuthenticated { get; }

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
    /// Gets a specific release by tag from the specified repository.
    /// </summary>
    /// <param name="owner">The repository owner.</param>
    /// <param name="repositoryName">The repository name.</param>
    /// <param name="tag">The release tag.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The GitHub release with the specified tag.</returns>
    Task<GitHubRelease> GetReleaseByTagAsync(
        string owner,
        string repositoryName,
        string tag,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all releases for the specified repository.
    /// </summary>
    /// <param name="owner">The repository owner.</param>
    /// <param name="repo">The repository name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A collection of GitHub releases.</returns>
    Task<IEnumerable<GitHubRelease>> GetReleasesAsync(
        string owner,
        string repo,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets workflow runs for the specified repository.
    /// </summary>
    /// <param name="owner">The repository owner.</param>
    /// <param name="repo">The repository name.</param>
    /// <param name="perPage">Number of runs per page.</param>
    /// <param name="page">The page number to fetch (1-indexed).</param>
    /// <param name="progress">Progress reporter for streaming workflow results.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A paginated result containing GitHub workflow runs and pagination info.</returns>
    Task<GitHubWorkflowRunsResult> GetWorkflowRunsForRepositoryAsync(
        string owner,
        string repo,
        int perPage = 5,
        int page = 1,
        IProgress<GitHubWorkflowRun>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets artifacts for a workflow run.
    /// </summary>
    /// <param name="owner">The repository owner.</param>
    /// <param name="repo">The repository name.</param>
    /// <param name="runId">The workflow run ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A collection of GitHub artifacts.</returns>
    Task<IEnumerable<GitHubArtifact>> GetArtifactsForWorkflowRunAsync(
        string owner,
        string repo,
        long runId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads a release asset to the specified path.
    /// </summary>
    /// <param name="owner">The repository owner.</param>
    /// <param name="repo">The repository name.</param>
    /// <param name="asset">The release asset to download.</param>
    /// <param name="destinationPath">The destination file path.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the download operation.</returns>
    Task DownloadReleaseAssetAsync(
        string owner,
        string repo,
        GitHubReleaseAsset asset,
        string destinationPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads an artifact to the specified path.
    /// </summary>
    /// <param name="owner">The repository owner.</param>
    /// <param name="repo">The repository name.</param>
    /// <param name="artifact">The artifact to download.</param>
    /// <param name="destinationPath">The destination file path.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the download operation.</returns>
    Task DownloadArtifactAsync(
        string owner,
        string repo,
        GitHubArtifact artifact,
        string destinationPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the authentication token for GitHub API requests.
    /// </summary>
    /// <param name="token">The GitHub token.</param>
    void SetAuthenticationToken(SecureString token);

    /// <summary>
    /// Gets the currently authenticated user.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The authenticated user information, or null if not authenticated.</returns>
    Task<GitHubUser?> GetAuthenticatedUserAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches for repositories by topic using GitHub's Search API.
    /// </summary>
    /// <param name="topic">The topic to search for (e.g., "genhub").</param>
    /// <param name="perPage">Number of results per page (max 100).</param>
    /// <param name="page">The page number to fetch (1-indexed).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A search response containing matching repositories.</returns>
    Task<GitHubRepositorySearchResponse> SearchRepositoriesByTopicAsync(
        string topic,
        int perPage = 30,
        int page = 1,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches for repositories by multiple topics using GitHub's Search API.
    /// Repositories matching ANY of the specified topics will be returned.
    /// </summary>
    /// <param name="topics">The topics to search for.</param>
    /// <param name="perPage">Number of results per page (max 100).</param>
    /// <param name="page">The page number to fetch (1-indexed).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A search response containing matching repositories.</returns>
    Task<GitHubRepositorySearchResponse> SearchRepositoriesByTopicsAsync(
        IEnumerable<string> topics,
        int perPage = 30,
        int page = 1,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets detailed repository information including topics.
    /// </summary>
    /// <param name="owner">The repository owner.</param>
    /// <param name="repo">The repository name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The repository information, or null if not found.</returns>
    Task<GitHubRepository?> GetRepositoryAsync(
        string owner,
        string repo,
        CancellationToken cancellationToken = default);
}