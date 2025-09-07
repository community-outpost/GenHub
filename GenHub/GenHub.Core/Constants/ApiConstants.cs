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

    // User agents

    /// <summary>
    /// Default user agent string for HTTP requests.
    /// </summary>
    public const string DefaultUserAgent = AppConstants.AppName + "/" + AppConstants.AppVersion;
}
