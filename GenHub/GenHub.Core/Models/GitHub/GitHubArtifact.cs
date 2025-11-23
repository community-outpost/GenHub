using System.Text.Json.Serialization;

namespace GenHub.Core.Models.GitHub;

/// <summary>
/// Represents a GitHub workflow artifact.
/// </summary>
public class GitHubArtifact
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GitHubArtifact"/> class.
    /// </summary>
    public GitHubArtifact()
    {
        BuildInfo = new GitHubBuild();
        RepositoryInfo = new GitHubRepository();
    }

    /// <summary>
    /// Gets or sets the artifact ID.
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// Gets or sets the workflow ID.
    /// </summary>
    public long WorkflowId { get; set; }

    /// <summary>
    /// Gets or sets the run ID.
    /// </summary>
    public long RunId { get; set; }

    /// <summary>
    /// Gets or sets the workflow number.
    /// </summary>
    public int WorkflowNumber { get; set; }

    /// <summary>
    /// Gets or sets the artifact name.
    /// </summary>
    public string Name
    {
        get => _name;
        set => _name = string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Name cannot be empty", nameof(value))
            : value;
    }

    private string _name = string.Empty;

    /// <summary>
    /// Gets or sets the artifact size in bytes.
    /// </summary>
    [JsonPropertyName("size_in_bytes")]
    public long SizeInBytes
    {
        get => _sizeInBytes;
        set => _sizeInBytes = value < 0
            ? throw new ArgumentException("Size cannot be negative", nameof(value))
            : value;
    }

    private long _sizeInBytes;

    /// <summary>
    /// Gets or sets a value indicating whether this is a release.
    /// </summary>
    public bool IsRelease { get; set; }

    /// <summary>
    /// Gets or sets the download URL.
    /// </summary>
    public string DownloadUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the archive download URL.
    /// </summary>
    public string ArchiveDownloadUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the artifact is expired.
    /// </summary>
    public bool Expired { get; set; }

    /// <summary>
    /// Gets or sets the creation date.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets the expiration date.
    /// </summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// Gets or sets the pull request number.
    /// </summary>
    public int? PullRequestNumber { get; set; }

    /// <summary>
    /// Gets or sets the pull request title.
    /// </summary>
    public string PullRequestTitle { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the commit SHA.
    /// </summary>
    public string CommitSha { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the commit message.
    /// </summary>
    public string CommitMessage { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the event type.
    /// </summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the build preset.
    /// </summary>
    public string BuildPreset { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the build info.
    /// </summary>
    public GitHubBuild BuildInfo { get; set; }

    /// <summary>
    /// Gets or sets the repository info.
    /// </summary>
    public GitHubRepository RepositoryInfo { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this artifact is active.
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this artifact is installed.
    /// </summary>
    public bool IsInstalled { get; set; }

    /// <summary>
    /// Gets or sets the workflow run info.
    /// </summary>
    public GitHubWorkflowRun? WorkflowRun { get; set; }
}
