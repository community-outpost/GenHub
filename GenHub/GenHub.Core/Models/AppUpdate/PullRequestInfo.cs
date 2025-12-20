namespace GenHub.Core.Models.AppUpdate;

/// <summary>
/// Represents information about an open pull request with CI artifacts.
/// </summary>
public record PullRequestInfo
{
    /// <summary>
    /// Gets the PR number (e.g., 123).
    /// </summary>
    public required int Number { get; init; }

    /// <summary>
    /// Gets the PR title.
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// Gets the branch name (e.g., "feature/dev-ui").
    /// </summary>
    public required string BranchName { get; init; }

    /// <summary>
    /// Gets the PR author's username.
    /// </summary>
    public required string Author { get; init; }

    /// <summary>
    /// Gets the PR state (open, closed, merged).
    /// </summary>
    public required string State { get; init; }

    /// <summary>
    /// Gets the date when the PR was last updated.
    /// </summary>
    public DateTimeOffset? UpdatedAt { get; init; }

    /// <summary>
    /// Gets the most recent artifact for this PR, if available.
    /// </summary>
    public ArtifactUpdateInfo? LatestArtifact { get; init; }

    /// <summary>
    /// Gets a value indicating whether this PR has CI artifacts available.
    /// </summary>
    public bool HasArtifacts => LatestArtifact != null;

    /// <summary>
    /// Gets the display version string for UI (e.g., "0.0.123").
    /// </summary>
    public string DisplayVersion => LatestArtifact?.DisplayVersion ?? $"0.0.{Number}";

    /// <summary>
    /// Gets a value indicating whether this PR is still open.
    /// </summary>
    public bool IsOpen => State.Equals("open", StringComparison.OrdinalIgnoreCase);
}