namespace GenHub.Core.Models.GitHub;

/// <summary>
/// Represents the public profile of the GitHub user currently signed in to GenHub.
/// </summary>
/// <param name="Login">The GitHub username (for example, octocat).</param>
/// <param name="Id">The GitHub user ID.</param>
/// <param name="Name">The display name, or null when the user has not set one.</param>
/// <param name="AvatarUrl">The avatar image URL, or null when unavailable.</param>
/// <param name="HtmlUrl">The profile page URL, or null when unavailable.</param>
public sealed record GitHubUserProfile(
    string Login,
    long Id,
    string? Name,
    string? AvatarUrl,
    string? HtmlUrl);
