namespace GenHub.Core.Models.AppUpdate;

/// <summary>
/// Information about an available artifact update from CI builds.
/// </summary>
/// <param name="version">The semantic version of the artifact.</param>
/// <param name="gitHash">The short git commit hash (7 chars).</param>
/// <param name="pullRequestNumber">The PR number if this is a PR build, or null.</param>
/// <param name="workflowRunId">The GitHub Actions workflow run ID.</param>
/// <param name="workflowRunUrl">The URL to the workflow run.</param>
/// <param name="artifactId">The artifact ID for download.</param>
/// <param name="artifactName">The artifact name.</param>
/// <param name="createdAt">When the artifact was created.</param>
public record ArtifactUpdateInfo(
    string version,
    string gitHash,
    int? pullRequestNumber,
    long workflowRunId,
    string workflowRunUrl,
    long artifactId,
    string artifactName,
    DateTime createdAt)
{
    /// <summary>
    /// Gets the semantic version of the artifact.
    /// </summary>
    public string Version { get; init; } = version;

    /// <summary>
    /// Gets the short git commit hash (7 chars).
    /// </summary>
    public string GitHash { get; init; } = gitHash;

    /// <summary>
    /// Gets the PR number if this is a PR build, or null.
    /// </summary>
    public int? PullRequestNumber { get; init; } = pullRequestNumber;

    /// <summary>
    /// Gets the GitHub Actions workflow run ID.
    /// </summary>
    public long WorkflowRunId { get; init; } = workflowRunId;

    /// <summary>
    /// Gets the URL to the workflow run.
    /// </summary>
    public string WorkflowRunUrl { get; init; } = workflowRunUrl;

    /// <summary>
    /// Gets the artifact ID for download.
    /// </summary>
    public long ArtifactId { get; init; } = artifactId;

    /// <summary>
    /// Gets the artifact name.
    /// </summary>
    public string ArtifactName { get; init; } = artifactName;

    /// <summary>
    /// Gets when the artifact was created.
    /// </summary>
    public DateTime CreatedAt { get; init; } = createdAt;

    /// <summary>
    /// Gets a value indicating whether this is a PR build artifact.
    /// </summary>
    public bool IsPrBuild => PullRequestNumber.HasValue;

    /// <summary>
    /// Gets the display version with hash.
    /// </summary>
    public string DisplayVersion
    {
        get
        {
            if (PullRequestNumber.HasValue)
            {
                // Strip any build metadata from version (everything after +)
                var baseVersion = Version.Split('+')[0];
                return $"v{baseVersion} ({GitHash})";
            }

            return $"v{Version} ({GitHash})";
        }
    }
}