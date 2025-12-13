using System.Text.Json.Serialization;

namespace GenHub.Core.Models.GitHub;

/// <summary>
/// Represents the owner information in a search result.
/// </summary>
public class GitHubSearchOwner
{
    /// <summary>
    /// Gets or sets the owner's login/username.
    /// </summary>
    [JsonPropertyName("login")]
    public string Login { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the owner's unique ID.
    /// </summary>
    [JsonPropertyName("id")]
    public long Id { get; set; }

    /// <summary>
    /// Gets or sets the GraphQL node ID.
    /// </summary>
    [JsonPropertyName("node_id")]
    public string NodeId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the avatar URL.
    /// </summary>
    [JsonPropertyName("avatar_url")]
    public string AvatarUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the HTML URL for the owner's profile.
    /// </summary>
    [JsonPropertyName("html_url")]
    public string HtmlUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the owner type (User or Organization).
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "User";

    /// <summary>
    /// Gets or sets a value indicating whether this is a site admin.
    /// </summary>
    [JsonPropertyName("site_admin")]
    public bool IsSiteAdmin { get; set; }
}