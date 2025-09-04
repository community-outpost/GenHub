namespace GenHub.Core.Constants;

/// <summary>
/// API and network related constants.
/// </summary>
public static class ApiConstants
{
    // GitHub API constants

    /// <summary>
    /// GitHub API base URL.
    /// </summary>
    public const string GitHubApiBaseUrl = "https://api.github.com";

    /// <summary>
    /// GitHub raw content base URL.
    /// </summary>
    public const string GitHubRawBaseUrl = "https://raw.githubusercontent.com";

    /// <summary>
    /// GitHub repository API endpoint template.
    /// </summary>
    public const string GitHubRepoApiEndpoint = "/repos/{owner}/{repo}";

    /// <summary>
    /// GitHub releases API endpoint template.
    /// </summary>
    public const string GitHubReleasesApiEndpoint = "/repos/{owner}/{repo}/releases";

    /// <summary>
    /// GitHub latest release API endpoint template.
    /// </summary>
    public const string GitHubLatestReleaseApiEndpoint = "/repos/{owner}/{repo}/releases/latest";

    /// <summary>
    /// GitHub release assets API endpoint template.
    /// </summary>
    public const string GitHubReleaseAssetsApiEndpoint = "/repos/{owner}/{repo}/releases/{releaseId}/assets";

    /// <summary>
    /// GitHub repository contents API endpoint template.
    /// </summary>
    public const string GitHubContentsApiEndpoint = "/repos/{owner}/{repo}/contents/{path}";

    // HTTP status codes

    /// <summary>
    /// HTTP OK status code.
    /// </summary>
    public const int HttpOk = 200;

    /// <summary>
    /// HTTP Created status code.
    /// </summary>
    public const int HttpCreated = 201;

    /// <summary>
    /// HTTP No Content status code.
    /// </summary>
    public const int HttpNoContent = 204;

    /// <summary>
    /// HTTP Bad Request status code.
    /// </summary>
    public const int HttpBadRequest = 400;

    /// <summary>
    /// HTTP Unauthorized status code.
    /// </summary>
    public const int HttpUnauthorized = 401;

    /// <summary>
    /// HTTP Forbidden status code.
    /// </summary>
    public const int HttpForbidden = 403;

    /// <summary>
    /// HTTP Not Found status code.
    /// </summary>
    public const int HttpNotFound = 404;

    /// <summary>
    /// HTTP Internal Server Error status code.
    /// </summary>
    public const int HttpInternalServerError = 500;

    // Network timeouts

    /// <summary>
    /// Default HTTP request timeout in seconds.
    /// </summary>
    public const int DefaultHttpTimeoutSeconds = 30;

    /// <summary>
    /// Long HTTP request timeout in seconds for large downloads.
    /// </summary>
    public const int LongHttpTimeoutSeconds = 300;

    /// <summary>
    /// Short HTTP request timeout in seconds for quick operations.
    /// </summary>
    public const int ShortHttpTimeoutSeconds = 10;

    // User agents

    /// <summary>
    /// Default user agent string for HTTP requests.
    /// </summary>
    public const string DefaultUserAgent = "GenHub/1.0";

    /// <summary>
    /// GitHub API user agent string.
    /// </summary>
    public const string GitHubApiUserAgent = "GenHub-GitHub-API/1.0";

    // Rate limiting

    /// <summary>
    /// GitHub API rate limit per hour for authenticated requests.
    /// </summary>
    public const int GitHubApiRateLimitAuthenticated = 5000;

    /// <summary>
    /// GitHub API rate limit per hour for unauthenticated requests.
    /// </summary>
    public const int GitHubApiRateLimitUnauthenticated = 60;

    /// <summary>
    /// Default delay between API requests in milliseconds.
    /// </summary>
    public const int DefaultApiRequestDelayMs = 1000;

    // Content types

    /// <summary>
    /// JSON content type.
    /// </summary>
    public const string ContentTypeJson = "application/json";

    /// <summary>
    /// Octet stream content type for binary data.
    /// </summary>
    public const string ContentTypeOctetStream = "application/octet-stream";

    /// <summary>
    /// Form URL encoded content type.
    /// </summary>
    public const string ContentTypeFormUrlEncoded = "application/x-www-form-urlencoded";
}
