namespace GenHub.Core.Models.AppUpdate;

/// <summary>
/// Information about an available artifact update from CI builds.
/// </summary>
/// <param name="Version">The semantic version of the artifact.</param>
/// <param name="GitHash">The short git commit hash (7 chars).</param>
/// <param name="PullRequestNumber">The PR number if this is a PR build, or null.</param>
/// <param name="WorkflowRunId">The GitHub Actions workflow run ID.</param>
/// <param name="WorkflowRunUrl">The URL to the workflow run.</param>
/// <param name="ArtifactId">The artifact ID for download.</param>
/// <param name="ArtifactName">The artifact name.</param>
/// <param name="CreatedAt">When the artifact was created.</param>
/// <param name="DownloadUrl">The download URL for the artifact.</param>
/// <param name="Size">The size of the artifact in bytes.</param>
public record ArtifactUpdateInfo(
    string Version,
    string GitHash,
    int? PullRequestNumber,
    long WorkflowRunId,
    string WorkflowRunUrl,
    long ArtifactId,
    string ArtifactName,
    DateTime CreatedAt,
    string? DownloadUrl,
    long Size)
{
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
            var displayHash = string.IsNullOrEmpty(GitHash) ? string.Empty : $" ({GitHash})";

            if (PullRequestNumber.HasValue)
            {
                // Strip any build metadata from version (everything after +)
                var baseVersion = Version.Split('+')[0];
                return $"v{baseVersion}{displayHash}";
            }

            return $"v{Version}{displayHash}";
        }
    }
}