namespace GenHub.Core.Models.GitHub;

/// <summary>
/// Represents a paginated result of GitHub workflow runs.
/// </summary>
public class GitHubWorkflowRunsResult
{
    /// <summary>
    /// Gets or sets the workflow runs in this page.
    /// </summary>
    public IEnumerable<GitHubWorkflowRun> WorkflowRuns { get; set; } = Array.Empty<GitHubWorkflowRun>();

    /// <summary>
    /// Gets or sets a value indicating whether more results might be available.
    /// </summary>
    public bool HasMore { get; set; }
}
