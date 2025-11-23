namespace GenHub.Core.Models.GitHub;

using System.Collections.Generic;

/// <summary>
/// Represents a GitHub repository.
/// </summary>
/// <remarks>
/// This model provides essential repository information for UI display and content resolution.
/// Supports integration with the content pipeline by including metadata like description and topics
/// for generating ContentManifest.Metadata. FullName is computed for consistency
/// with GitHub's standard format (owner/repo). Used in GitHubDiscoverer and
/// GitHubResolver for repository-based content discovery.
/// </remarks>
public class GitHubRepository
{
    /// <summary>
    /// Gets or sets the unique repository ID from GitHub.
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// Gets or sets the repository owner (GitHub username/organization).
    /// Maps to Repository.Owner.Login.
    /// </summary>
    public string RepoOwner { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the repository name.
    /// Maps to Repository.Name.
    /// </summary>
    public string RepoName { get; set; } = string.Empty;

    /// <summary>
    /// Gets the full repository name in GitHub format (owner/repo).
    /// </summary>
    public string FullName => $"{RepoOwner}/{RepoName}";

    /// <summary>
    /// Gets or sets the human-readable repository description.
    /// Maps to Repository.Description; used for UI display and
    /// ContentMetadata.Description in manifests.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the HTML URL for the repository.
    /// Maps to Repository.HtmlUrl; used for external links in UI.
    /// </summary>
    public string HtmlUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the repository topics/tags.
    /// Maps to Repository.Topics; used for search/filtering in
    /// ContentSearchQuery.Tags.
    /// </summary>
    public List<string> Topics { get; set; } = new();

    /// <summary>
    /// Gets or sets the number of stars (watchers) for the repository.
    /// Maps to Repository.StargazersCount; displayed in UI for popularity.
    /// </summary>
    public int StarCount { get; set; }

    /// <summary>
    /// Gets or sets the number of forks for the repository.
    /// Maps to Repository.ForksCount; displayed in UI.
    /// </summary>
    public int ForkCount { get; set; }

    /// <summary>
    /// Gets or sets the display name for UI (falls back to RepoName if empty).
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Gets the computed display name for UI binding.
    /// </summary>
    public string ComputedDisplayName => string.IsNullOrEmpty(DisplayName) ? RepoName : DisplayName;
}
