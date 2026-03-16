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

    // UploadThing

    /// <summary>
    /// UploadThing API version.
    /// </summary>
    public const string UploadThingApiVersion = "7.7.4";

    /// <summary>
    /// UploadThing prepare upload URL.
    /// </summary>
    public const string UploadThingPrepareUrl = "https://api.uploadthing.com/v7/prepareUpload";

    /// <summary>
    /// UploadThing delete file URL.
    /// </summary>
    public const string UploadThingDeleteUrl = "https://api.uploadthing.com/v6/deleteFiles";

    /// <summary>
    /// UploadThing public file URL format.
    /// </summary>
    public const string UploadThingPublicUrlFormat = "https://utfs.io/f/{0}";

    /// <summary>
    /// UploadThing URL fragment for identification.
    /// </summary>
    public const string UploadThingUrlFragment = "utfs.io/f/";

    /// <summary>
    /// UploadThing token environment variable.
    /// </summary>
    public const string UploadThingTokenEnvVar = "UPLOADTHING_TOKEN";

    /// <summary>
    /// Alternative UploadThing token environment variable.
    /// </summary>
    public const string UploadThingTokenEnvVarAlt = "GENHUB_UPLOADTHING_TOKEN";

    /// <summary>
    /// Gets the default UploadThing token injected at build time (Obfuscated).
    /// </summary>
    public static string BuildTimeUploadThingToken
    {
        get
        {
            // This is a simple XOR obfuscation to prevent the raw token from appearing in strings/debuggers.
            // The actual values are injected during the GitHub Actions build process.
            byte[] data = []; // [PLACEHOLDER_DATA]
            byte[] key = [];  // [PLACEHOLDER_KEY]

            if (data.Length == 0 || key.Length == 0) return string.Empty;

            var result = new byte[data.Length];
            for (int i = 0; i < data.Length; i++)
            {
                result[i] = (byte)(data[i] ^ key[i % key.Length]);
            }

            return System.Text.Encoding.UTF8.GetString(result);
        }
    }

    /// <summary>
    /// UploadThing API key header.
    /// </summary>
    public const string UploadThingApiKeyHeader = "x-uploadthing-api-key";

    /// <summary>
    /// UploadThing version header.
    /// </summary>
    public const string UploadThingVersionHeader = "x-uploadthing-version";

    // Media Types

    /// <summary>
    /// Media type for ZIP files.
    /// </summary>
    public const string MediaTypeZip = "application/zip";

    // GenTool

    /// <summary>
    /// GenTool data URL fragment for identification.
    /// </summary>
    public const string GenToolUrlFragment = "gentool.net/data/";

    // Generals Online

    /// <summary>
    /// Generals Online view match URL fragment.
    /// </summary>
    public const string GeneralsOnlineViewMatchFragment = "playgenerals.online/viewmatch";

    /// <summary>
    /// Format string for GitHub API Workflow Runs endpoint (owner, repo).
    /// </summary>
    public const string GitHubApiWorkflowRunsAllFormat = "https://api.github.com/repos/{0}/{1}/actions/runs?status=success&per_page=20";

    // User agents

    /// <summary>
    /// Gets the default user agent string for HTTP requests.
    /// </summary>
    public static string DefaultUserAgent => $"{AppConstants.AppName}/{AppConstants.AppVersion}";

    /// <summary>
    /// UserAgent string that mimics a standard web browser.
    /// </summary>
    public const string BrowserUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";
}