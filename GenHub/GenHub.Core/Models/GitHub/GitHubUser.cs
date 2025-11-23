namespace GenHub.Core.Models.GitHub;

/// <summary>
/// Represents a GitHub user.
/// </summary>
public class GitHubUser
{
    /// <summary>
    /// Gets or sets the user's login name.
    /// </summary>
    public string Login { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the user's ID.
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// Gets or sets the user's name.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the user's email.
    /// </summary>
    public string? Email { get; set; }

    /// <summary>
    /// Gets or sets the user's avatar URL.
    /// </summary>
    public string? AvatarUrl { get; set; }

    /// <summary>
    /// Gets or sets the user's profile URL.
    /// </summary>
    public string? HtmlUrl { get; set; }

    /// <summary>
    /// Gets or sets the user's type (User or Organization).
    /// </summary>
    public string? Type { get; set; }
}
