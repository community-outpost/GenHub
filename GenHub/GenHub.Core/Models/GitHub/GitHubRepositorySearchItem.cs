using System.Text.Json.Serialization;

namespace GenHub.Core.Models.GitHub;

/// <summary>
/// Represents a single repository item from GitHub's Search API response.
/// </summary>
public class GitHubRepositorySearchItem
{
    /// <summary>
    /// Gets or sets the unique repository ID.
    /// </summary>
    [JsonPropertyName("id")]
    public long Id { get; set; }

    /// <summary>
    /// Gets or sets the GraphQL node ID.
    /// </summary>
    [JsonPropertyName("node_id")]
    public string NodeId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the repository name.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the full repository name (owner/repo).
    /// </summary>
    [JsonPropertyName("full_name")]
    public string FullName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the repository owner information.
    /// </summary>
    [JsonPropertyName("owner")]
    public GitHubSearchOwner Owner { get; set; } = new();

    /// <summary>
    /// Gets or sets a value indicating whether the repository is private.
    /// </summary>
    [JsonPropertyName("private")]
    public bool IsPrivate { get; set; }

    /// <summary>
    /// Gets or sets the HTML URL for the repository.
    /// </summary>
    [JsonPropertyName("html_url")]
    public string HtmlUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the repository description.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this is a fork.
    /// </summary>
    [JsonPropertyName("fork")]
    public bool IsFork { get; set; }

    /// <summary>
    /// Gets or sets the API URL for the repository.
    /// </summary>
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the creation timestamp.
    /// </summary>
    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets the last update timestamp.
    /// </summary>
    [JsonPropertyName("updated_at")]
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// Gets or sets the last push timestamp.
    /// </summary>
    [JsonPropertyName("pushed_at")]
    public DateTime? PushedAt { get; set; }

    /// <summary>
    /// Gets or sets the repository homepage URL.
    /// </summary>
    [JsonPropertyName("homepage")]
    public string? Homepage { get; set; }

    /// <summary>
    /// Gets or sets the repository size in KB.
    /// </summary>
    [JsonPropertyName("size")]
    public long Size { get; set; }

    /// <summary>
    /// Gets or sets the star count.
    /// </summary>
    [JsonPropertyName("stargazers_count")]
    public int StargazersCount { get; set; }

    /// <summary>
    /// Gets or sets the primary programming language.
    /// </summary>
    [JsonPropertyName("language")]
    public string? Language { get; set; }

    /// <summary>
    /// Gets or sets the fork count.
    /// </summary>
    [JsonPropertyName("forks_count")]
    public int ForksCount { get; set; }

    /// <summary>
    /// Gets or sets the open issues count.
    /// </summary>
    [JsonPropertyName("open_issues_count")]
    public int OpenIssuesCount { get; set; }

    /// <summary>
    /// Gets or sets the default branch name.
    /// </summary>
    [JsonPropertyName("default_branch")]
    public string DefaultBranch { get; set; } = "main";

    /// <summary>
    /// Gets or sets the search relevance score.
    /// </summary>
    [JsonPropertyName("score")]
    public double Score { get; set; }

    /// <summary>
    /// Gets or sets the repository topics/tags.
    /// </summary>
    [JsonPropertyName("topics")]
    public List<string> Topics { get; set; } = new();

    /// <summary>
    /// Gets or sets the license information.
    /// </summary>
    [JsonPropertyName("license")]
    public GitHubLicense? License { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the repository is archived.
    /// </summary>
    [JsonPropertyName("archived")]
    public bool IsArchived { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the repository is disabled.
    /// </summary>
    [JsonPropertyName("disabled")]
    public bool IsDisabled { get; set; }

    /// <summary>
    /// Gets or sets the repository visibility.
    /// </summary>
    [JsonPropertyName("visibility")]
    public string Visibility { get; set; } = "public";

    /// <summary>
    /// Gets or sets the releases URL template.
    /// </summary>
    [JsonPropertyName("releases_url")]
    public string ReleasesUrl { get; set; } = string.Empty;

    /// <summary>
    /// Converts this search item to a GitHubRepository model.
    /// </summary>
    /// <returns>A GitHubRepository instance.</returns>
    public GitHubRepository ToRepository()
    {
        return new GitHubRepository
        {
            Id = Id,
            RepoOwner = Owner.Login,
            RepoName = Name,
            Description = Description,
            HtmlUrl = HtmlUrl,
            Topics = Topics,
            StarCount = StargazersCount,
            ForkCount = ForksCount,
            DisplayName = Name,
        };
    }
}