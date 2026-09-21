using System;
using System.Diagnostics.CodeAnalysis;

namespace GenHub.Core.Constants;

/// <summary>
/// API and network related constants.
/// </summary>
[SuppressMessage("Major Code Smell", "S1075:URIs should not be hardcoded", Justification = "Centralized fallback API constants and endpoint definitions.")]
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
    /// Format string for GitHub API CI Workflow Runs endpoint by branch (owner, repo, branch).
    /// Scoped to ci.yml so non-build workflows do not displace artifact-producing runs.
    /// </summary>
    public const string GitHubApiWorkflowRunsFormat = "https://api.github.com/repos/{0}/{1}/actions/workflows/ci.yml/runs?status=success&branch={2}&per_page=20";

    /// <summary>
    /// Format string for GitHub API Latest CI Workflow Runs endpoint (owner, repo).
    /// </summary>
    public const string GitHubApiLatestWorkflowRunsFormat = "https://api.github.com/repos/{0}/{1}/actions/workflows/ci.yml/runs?status=success&per_page=1";

    /// <summary>
    /// Format string for GitHub API Run Artifacts endpoint (owner, repo, runId).
    /// </summary>
    public const string GitHubApiRunArtifactsFormat = "https://api.github.com/repos/{0}/{1}/actions/runs/{2}/artifacts";

    /// <summary>
    /// Format string for GitHub API Branches endpoint (owner, repo).
    /// </summary>
    public const string GitHubApiBranchesFormat = "https://api.github.com/repos/{0}/{1}/branches?per_page=100";

    /// <summary>
    /// Format string for GitHub API Releases endpoint (owner, repo).
    /// </summary>
    public const string GitHubApiReleasesFormat = "https://api.github.com/repos/{0}/{1}/releases";

    /// <summary>
    /// GitHub API user endpoint for token validation.
    /// </summary>
    public const string GitHubApiUserEndpoint = "https://api.github.com/user";

    /// <summary>
    /// Format string for legacy community content dependency archives (content code).
    /// </summary>
    public const string LegacyContentDependencyFormat = "https://legi.cc/gp2/f/{0}.dat";

    /// <summary>
    /// Format string for legacy community patch dependency archives (content code).
    /// </summary>
    public const string LegacyPatchDependencyFormat = "https://legi.cc/patch/{0}.dat";

    /// <summary>
    /// OneDrive direct download endpoint for cloud mirror links.
    /// </summary>
    public const string OneDriveDownloadEndpoint = "https://onedrive.live.com/download";

    // Upload Gateway & Cloud Storage

    /// <summary>
    /// Environment variable name for overriding the upload gateway base URL during local development/staging.
    /// </summary>
    public const string UploadGatewayBaseUrlEnvVar = "GENHUB_UPLOAD_GATEWAY_URL";

    /// <summary>
    /// Base URL for the GenHub community upload gateway.
    /// </summary>
    public const string DefaultUploadGatewayBaseUrl = "https://genhub-upload-gateway.mustafa2146.workers.dev";

    /// <summary>
    /// Gets the active base URL for the upload gateway, checking environment variable overrides first.
    /// </summary>
    public static string UploadGatewayBaseUrl =>
        Environment.GetEnvironmentVariable(UploadGatewayBaseUrlEnvVar) is { Length: > 0 } customUrl
            ? customUrl.TrimEnd('/')
            : DefaultUploadGatewayBaseUrl;

    /// <summary>
    /// Endpoint path for cloud uploads.
    /// </summary>
    public const string UploadEndpoint = "/api/v1/uploads";

    /// <summary>
    /// Endpoint path for deleting cloud uploads.
    /// </summary>
    public const string UploadDeleteEndpoint = "/api/v1/uploads/delete";

    /// <summary>
    /// Gets the full default URL for cloud uploads.
    /// </summary>
    public static string DefaultUploadUrl => UploadGatewayBaseUrl + UploadEndpoint;

    /// <summary>
    /// Gets the full default URL for deleting cloud uploads.
    /// </summary>
    public static string DefaultUploadDeleteUrl => UploadGatewayBaseUrl + UploadDeleteEndpoint;

    /// <summary>
    /// Format string for constructing UploadThing public file URLs.
    /// </summary>
    public const string UploadThingPublicUrlFormat = "https://utfs.io/f/{0}";

    /// <summary>
    /// UploadThing URL fragment for identification.
    /// </summary>
    public const string UploadThingUrlFragment = "utfs.io/f/";

    /// <summary>
    /// Modern UploadThing (v7) UFS URL fragment for identification.
    /// </summary>
    public const string UploadThingUfsUrlFragment = ".ufs.sh/f/";

    /// <summary>
    /// Modern UploadThing (v7) UFS short URL fragment for identification.
    /// </summary>
    public const string UploadThingUfsShortUrlFragment = "ufs.sh/f/";

    /// <summary>
    /// Media type for ZIP archives.
    /// </summary>
    public const string MediaTypeZip = "application/zip";

    /// <summary>
    /// Default filename fallback for generic uploads when a source filename cannot be determined.
    /// </summary>
    public const string DefaultUploadFileName = "upload.zip";

    /// <summary>
    /// Multipart form field name carrying the uploaded file.
    /// </summary>
    public const string UploadMultipartFileFieldName = "file";

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

    // GameReplays / Strata

    /// <summary>
    /// GameReplays Strata domain URL fragment for identification.
    /// </summary>
    public const string StrataUrlFragment = "strata.gamereplays.org";

    /// <summary>
    /// GameReplays domain URL fragment for identification.
    /// </summary>
    public const string GameReplaysDomainFragment = "gamereplays.org";

    /// <summary>
    /// Format string for GitHub API CI Workflow Runs endpoint across all branches (owner, repo).
    /// </summary>
    public const string GitHubApiWorkflowRunsAllFormat = "https://api.github.com/repos/{0}/{1}/actions/workflows/ci.yml/runs?status=success&per_page=20";

    // YouTube

    /// <summary>
    /// Base watch URL prefix for YouTube videos.
    /// </summary>
    public const string YouTubeWatchUrlPrefix = "https://www.youtube.com/watch?v=";

    // Online (virtual LAN) edge

    /// <summary>
    /// Environment variable name for overriding the Online edge base URL.
    /// </summary>
    public const string OnlineEdgeBaseUrlEnvVar = "GENHUB_ONLINE_EDGE_URL";

    /// <summary>
    /// Environment variable name for overriding the primary Online edge URL (e.g. self-hosted VPS).
    /// </summary>
    public const string OnlinePrimaryUrlEnvVar = "GENHUB_ONLINE_PRIMARY_URL";

    /// <summary>
    /// Environment variable name for overriding the fallback/backup Online edge URL.
    /// </summary>
    public const string OnlineFallbackUrlEnvVar = "GENHUB_ONLINE_FALLBACK_URL";

    /// <summary>
    /// Default primary base URL for the GenHub Online edge (self-hosted VPS instance).
    /// </summary>
    public const string DefaultPrimaryEdgeBaseUrl = "http://152.70.171.121:8787";

    /// <summary>
    /// Default fallback base URL for the GenHub Online edge (Cloudflare Worker backup).
    /// </summary>
    public const string DefaultOnlineEdgeBaseUrl = "https://genhub-online-edge.mustafa2146.workers.dev";

    private static volatile string? _activeEdgeBaseUrl;

    /// <summary>
    /// Gets or sets the actively resolved base URL for the Online edge.
    /// </summary>
    public static string ActiveOnlineEdgeBaseUrl
    {
        get => _activeEdgeBaseUrl ?? OnlineEdgeBaseUrl;
        set => _activeEdgeBaseUrl = string.IsNullOrWhiteSpace(value) ? null : value.TrimEnd('/');
    }

    /// <summary>
    /// Gets the primary base URL for the Online edge.
    /// </summary>
    public static string PrimaryOnlineEdgeBaseUrl =>
        Environment.GetEnvironmentVariable(OnlinePrimaryUrlEnvVar) is { Length: > 0 } primaryUrl
            ? primaryUrl.TrimEnd('/')
            : (Environment.GetEnvironmentVariable(OnlineEdgeBaseUrlEnvVar) is { Length: > 0 } customUrl
                ? customUrl.TrimEnd('/')
                : DefaultPrimaryEdgeBaseUrl);

    /// <summary>
    /// Gets the fallback/backup base URL for the Online edge.
    /// </summary>
    public static string FallbackOnlineEdgeBaseUrl =>
        Environment.GetEnvironmentVariable(OnlineFallbackUrlEnvVar) is { Length: > 0 } fallbackUrl
            ? fallbackUrl.TrimEnd('/')
            : DefaultOnlineEdgeBaseUrl;

    /// <summary>
    /// Gets the active base URL for the Online edge, checking runtime active selection and environment variable overrides first.
    /// </summary>
    public static string OnlineEdgeBaseUrl =>
        _activeEdgeBaseUrl
        ?? (Environment.GetEnvironmentVariable(OnlineEdgeBaseUrlEnvVar) is { Length: > 0 } customUrl
            ? customUrl.TrimEnd('/')
            : PrimaryOnlineEdgeBaseUrl);

    /// <summary>
    /// Resets the active edge base URL to the configured primary.
    /// </summary>
    public static void ResetActiveOnlineEdgeBaseUrl()
    {
        _activeEdgeBaseUrl = null;
    }

    /// <summary>
    /// Endpoint path for anonymous session issuance.
    /// </summary>
    public const string OnlineSessionsEndpoint = "/v1/sessions/anonymous";

    /// <summary>
    /// Endpoint path for the public network directory.
    /// </summary>
    public const string OnlineNetworksEndpoint = "/v1/networks";

    /// <summary>
    /// Format string for the directory search query (escaped search text).
    /// </summary>
    public const string OnlineNetworksSearchFormat = "?search={0}";

    /// <summary>
    /// Format string for the single-network endpoint used by detail and update (network id).
    /// </summary>
    public const string OnlineNetworkByIdFormat = "/v1/networks/{0}";

    /// <summary>
    /// Format string for the network join endpoint (network id).
    /// </summary>
    public const string OnlineNetworkJoinFormat = "/v1/networks/{0}/join";

    /// <summary>
    /// Format string for the network presence socket endpoint (network id).
    /// </summary>
    public const string OnlinePresenceFormat = "/v1/networks/{0}/presence";

    /// <summary>
    /// Format string for the network leave endpoint (network id).
    /// </summary>
    public const string OnlineLeaveFormat = "/v1/networks/{0}/leave";

    /// <summary>
    /// Format string for the member report endpoint (network id).
    /// </summary>
    public const string OnlineReportFormat = "/v1/networks/{0}/report";

    /// <summary>
    /// Format string for the member ban endpoint (network id).
    /// </summary>
    public const string OnlineBanFormat = "/v1/networks/{0}/ban";

    /// <summary>
    /// Format string for the grant refresh endpoint (network id).
    /// </summary>
    public const string OnlineCertFormat = "/v1/networks/{0}/cert";

    /// <summary>
    /// Format string for the connection-outcome telemetry endpoint (network id).
    /// </summary>
    public const string OnlineOutcomeFormat = "/v1/networks/{0}/outcome";

    /// <summary>
    /// Environment variable name for overriding the STUN hostname.
    /// </summary>
    public const string OnlineStunHostEnvVar = "GENHUB_ONLINE_STUN_HOST";

    /// <summary>
    /// Default STUN hostname for reflexive endpoint discovery.
    /// </summary>
    public const string DefaultOnlineStunHost = "stun.cloudflare.com";

    /// <summary>
    /// Gets the active STUN hostname, checking environment variable overrides first.
    /// </summary>
    public static string OnlineStunHost =>
        Environment.GetEnvironmentVariable(OnlineStunHostEnvVar) is { Length: > 0 } customHost
            ? customHost
            : DefaultOnlineStunHost;

    // User agents

    /// <summary>
    /// Gets the default user agent string for HTTP requests.
    /// </summary>
    public static string DefaultUserAgent => $"{AppConstants.AppName}/{AppConstants.AppVersion}";

    /// <summary>
    /// UserAgent string that mimics a standard web browser.
    /// </summary>
    public const string BrowserUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

    // NuGet

    /// <summary>
    /// Environment variable name for overriding the NuGet flat container base URL during local development/staging.
    /// </summary>
    public const string NuGetFlatContainerBaseUrlEnvVar = "GENHUB_NUGET_BASE_URL";

    /// <summary>
    /// Default base URL for the NuGet flat container feed.
    /// </summary>
    public const string DefaultNuGetFlatContainerBaseUrl = "https://api.nuget.org/v3-flatcontainer";

    /// <summary>
    /// Gets the active base URL for the NuGet flat container feed, checking environment variable overrides first.
    /// </summary>
    public static string NuGetFlatContainerBaseUrl =>
        Environment.GetEnvironmentVariable(NuGetFlatContainerBaseUrlEnvVar) is { Length: > 0 } customUrl
            ? customUrl.TrimEnd('/')
            : DefaultNuGetFlatContainerBaseUrl;

    /// <summary>
    /// Builds the direct download URL for a NuGet package archive.
    /// </summary>
    /// <param name="packageId">The package id (for example, microsoft.playwright).</param>
    /// <param name="version">The exact package version (for example, 1.55.0).</param>
    /// <returns>The download URL for the .nupkg archive.</returns>
    public static string GetNuGetPackageDownloadUrl(string packageId, string version)
    {
        var normalizedId = packageId.ToLowerInvariant();
        var normalizedVersion = version.ToLowerInvariant();
        return $"{NuGetFlatContainerBaseUrl}/{normalizedId}/{normalizedVersion}/{normalizedId}.{normalizedVersion}.nupkg";
    }
}
