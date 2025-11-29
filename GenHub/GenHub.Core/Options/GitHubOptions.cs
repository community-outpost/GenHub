namespace GenHub.Core.Options;

/// <summary>
/// Configuration options for GitHub integration.
/// </summary>
public class GitHubOptions
{
    /// <summary>
    /// Gets or sets the GitHub API base URL.
    /// </summary>
    public string ApiBaseUrl { get; set; } = "https://api.github.com";

    /// <summary>
    /// Gets or sets the product header value for GitHub API requests.
    /// </summary>
    public string ProductHeader { get; set; } = "GenHub";

    /// <summary>
    /// Gets or sets the GitHub token environment variable name.
    /// </summary>
    public string TokenEnvironmentVariable { get; set; } = "GITHUB_TOKEN";

    /// <summary>
    /// Gets or sets the default timeout for GitHub API requests in seconds.
    /// </summary>
    public int DefaultTimeoutSeconds { get; set; } = 30;
}
