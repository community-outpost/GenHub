namespace GenHub.Core.Models.GitHub;

/// <summary>
/// Represents a GitHub workflow run.
/// </summary>
public class GitHubWorkflowRun
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GitHubWorkflowRun"/> class.
    /// </summary>
    public GitHubWorkflowRun()
    {
        Workflow = new GitHubWorkflow();
    }

    /// <summary>
    /// Gets or sets the run ID.
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// Gets or sets the workflow ID.
    /// </summary>
    public long WorkflowId { get; set; }

    /// <summary>
    /// Gets or sets the run number.
    /// </summary>
    public int RunNumber { get; set; }

    /// <summary>
    /// Gets or sets the run attempt.
    /// </summary>
    public int RunAttempt { get; set; }

    /// <summary>
    /// Gets or sets the status.
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the conclusion.
    /// </summary>
    public string Conclusion { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the head branch.
    /// </summary>
    public string HeadBranch { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the head SHA.
    /// </summary>
    public string HeadSha { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the event type.
    /// </summary>
    public string Event { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the creation date.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets the update date.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// Gets or sets the HTML URL.
    /// </summary>
    public string HtmlUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the name of the workflow run.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the associated workflow.
    /// </summary>
    public GitHubWorkflow Workflow { get; set; }

    /// <summary>
    /// Gets or sets the associated pull request numbers.
    /// </summary>
    public List<int> PullRequestNumbers { get; set; } = new();

    /// <summary>
    /// Gets or sets the display title for the run.
    /// </summary>
    public string DisplayTitle { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the actor who triggered the run.
    /// </summary>
    public string Actor { get; set; } = string.Empty;
}
