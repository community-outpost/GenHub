namespace GenHub.Core.Models.GitHub;

/// <summary>
/// Represents a GitHub workflow.
/// </summary>
public class GitHubWorkflow
{
    /// <summary>
    /// Gets or sets the workflow ID.
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// Gets or sets the workflow name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the workflow path.
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the workflow state.
    /// </summary>
    public string State { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the creation date.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets the last update date.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
