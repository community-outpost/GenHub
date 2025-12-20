namespace GenHub.Core.Constants;

/// <summary>
/// API and network related constants.
/// </summary>
public static class ApiConstants
{
    // GitHub

    /// <summary>
    /// GitHub domain name.
    /// </summary>
    public const string GitHubDomain = "github.com";

    /// <summary>
    /// GitHub URL regex pattern for parsing repository URLs.
    /// </summary>
    public const string GitHubUrlRegexPattern = @"^https://github\.com/(?<owner>[^/]+)/(?<repo>[^/]+)(?:/releases/tag/(?<tag>[^/]+))?";

    // GitHub API

    /// <summary>
    /// GitHub API base URL.
    /// </summary>
    public const string GitHubApiBaseUrl = "https://api.github.com";

    /// <summary>
    /// GitHub API Accept header value.
    /// </summary>
    public const string GitHubApiHeaderAccept = "application/vnd.github+json";

    /// <summary>
    /// Format string for GitHub API Pull Requests endpoint (owner, repo).
    /// </summary>
    public const string GitHubApiPrsFormat = "https://api.github.com/repos/{0}/{1}/pulls?state=open&per_page=30";

    /// <summary>
    /// Format string for GitHub API Pull Request Status endpoint (owner, repo, number).
    /// </summary>
    public const string GitHubApiPrDetailFormat = "https://api.github.com/repos/{0}/{1}/pulls/{2}";

    /// <summary>
    /// Format string for GitHub API Artifact download URL (owner, repo, artifactId).
    /// </summary>
    public const string GitHubApiArtifactDownloadFormat = "https://api.github.com/repos/{0}/{1}/actions/artifacts/{2}/zip";

    /// <summary>
    /// Format string for GitHub API Workflow Runs endpoint (owner, repo, branch).
    /// </summary>
    public const string GitHubApiWorkflowRunsFormat = "https://api.github.com/repos/{0}/{1}/actions/runs?status=success&branch={2}&per_page=10";

    /// <summary>
    /// Format string for GitHub API Latest Workflow Runs endpoint (owner, repo).
    /// </summary>
    public const string GitHubApiLatestWorkflowRunsFormat = "https://api.github.com/repos/{0}/{1}/actions/runs?status=success&per_page=1";

    /// <summary>
    /// Format string for GitHub API Run Artifacts endpoint (owner, repo, runId).
    /// </summary>
    public const string GitHubApiRunArtifactsFormat = "https://api.github.com/repos/{0}/{1}/actions/runs/{2}/artifacts";

    // User agents

    /// <summary>
    /// Gets the default user agent string for HTTP requests.
    /// </summary>
    public static string DefaultUserAgent => $"{AppConstants.AppName}/{AppConstants.AppVersion}";
}
