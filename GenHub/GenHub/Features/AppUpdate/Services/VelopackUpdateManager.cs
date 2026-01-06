using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Core.Models.AppUpdate;
using GenHub.Core.Models.Enums;
using GenHub.Features.AppUpdate.Interfaces;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;
using Velopack;
using Velopack.Sources;

namespace GenHub.Features.AppUpdate.Services;

/// <summary>
/// Velopack-based update manager service with support for release and artifact update channels.
/// </summary>
public partial class VelopackUpdateManager : IVelopackUpdateManager, IDisposable
{
    /// <summary>
    /// Regex for extracting version from nupkg filename.
    /// </summary>
    [GeneratedRegex(@"GenHub-(.+)-full\.nupkg", RegexOptions.IgnoreCase)]
    private static partial Regex NupkgVersionRegex();

    private readonly ILogger<VelopackUpdateManager> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IGitHubTokenStorage? _gitHubTokenStorage;
    private readonly IUserSettingsService? _userSettingsService;
    private readonly UpdateManager? _updateManager;
    private readonly GithubSource _githubSource;

    private bool _hasUpdateFromGitHub;
    private string? _latestVersionFromGitHub;
    private ArtifactUpdateInfo? _latestArtifactUpdate;

    // Caching fields
    private DateTime _lastUpdateCheckTime = DateTime.MinValue;
    private UpdateInfo? _cachedUpdateInfo;
    private DateTime _lastArtifactCheckTime = DateTime.MinValue;
    private ArtifactUpdateInfo? _cachedArtifactUpdateInfo;
    private DateTime _lastPrListCheckTime = DateTime.MinValue;
    private IReadOnlyList<PullRequestInfo>? _cachedPrList;
    private DateTime _lastBranchListCheckTime = DateTime.MinValue;
    private IReadOnlyList<string>? _cachedBranchList;

    /// <inheritdoc/>
    public bool HasArtifactUpdateAvailable => _latestArtifactUpdate != null;

    /// <inheritdoc/>
    public ArtifactUpdateInfo? LatestArtifactUpdate => _latestArtifactUpdate;

    /// <inheritdoc/>
    public int? SubscribedPrNumber { get; set; }

    /// <inheritdoc/>
    public string? SubscribedBranch { get; set; }

    /// <inheritdoc/>
    public bool IsPrMergedOrClosed { get; private set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="VelopackUpdateManager"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="httpClientFactory">The HTTP client factory for creating HttpClient instances.</param>
    /// <param name="gitHubTokenStorage">The GitHub token storage (optional).</param>
    /// <param name="userSettingsService">The user settings service (optional).</param>
    public VelopackUpdateManager(
        ILogger<VelopackUpdateManager> logger,
        IHttpClientFactory httpClientFactory,
        IGitHubTokenStorage? gitHubTokenStorage = null,
        IUserSettingsService? userSettingsService = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _gitHubTokenStorage = gitHubTokenStorage;
        _userSettingsService = userSettingsService;

        // Always initialize GithubSource for update checking
        _githubSource = new GithubSource(AppConstants.GitHubRepositoryUrl, string.Empty, true);

        try
        {
            // Try to initialize UpdateManager for downloading/applying updates
            // This will only work if app is installed, but that's OK - we check GitHub directly
            _updateManager = new UpdateManager(_githubSource);
            _logger.LogInformation("Velopack UpdateManager initialized successfully for: {Repository}", AppConstants.GitHubRepositoryUrl);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Velopack UpdateManager not available (running from Debug)");
            _logger.LogDebug("Update CHECKING will still work via GitHub API, but downloading/installing requires installed app");
        }
    }

    /// <summary>
    /// Disposes of managed resources.
    /// </summary>
    public void Dispose()
    {
        // Dispose UpdateManager if it implements IDisposable
        if (_updateManager is IDisposable disposableUpdateManager)
        {
            disposableUpdateManager.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    /// <inheritdoc/>
    public async Task<UpdateInfo?> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        // Check cache
        if (DateTime.UtcNow - _lastUpdateCheckTime < AppUpdateConstants.CacheDuration)
        {
            _logger.LogInformation("Returning cached update info (checked {TimeLess} ago)", (DateTime.UtcNow - _lastUpdateCheckTime).ToString(@"mm\:ss"));
            return _cachedUpdateInfo;
        }

        _logger.LogInformation("Starting GitHub update check for repository: {Url}", AppConstants.GitHubRepositoryUrl);

        try
        {
            // Extract owner and repo from URL
            // Format: https://github.com/owner/repo
            var uri = new Uri(AppConstants.GitHubRepositoryUrl);
            var pathParts = uri.AbsolutePath.Trim('/').Split('/');
            if (pathParts.Length < 2)
            {
                _logger.LogError("Invalid GitHub repository URL format: {Url}", AppConstants.GitHubRepositoryUrl);
                return null;
            }

            var owner = pathParts[0];
            var repo = pathParts[1];

            _logger.LogInformation("🔍 Fetching releases from GitHub API: {Owner}/{Repo}", owner, repo);

            // Call GitHub API to get latest release with retries
            var apiUrl = $"https://api.github.com/repos/{owner}/{repo}/releases";

            // Use PAT if available to increase rate limits
            HttpClient client;
            if (_gitHubTokenStorage != null && await _gitHubTokenStorage.LoadTokenAsync() is { } token)
            {
                _logger.LogDebug("Using GitHub PAT for update check to increase rate limits");
                client = CreateConfiguredHttpClientWithToken(token);
            }
            else
            {
                _logger.LogDebug("No GitHub PAT available for update check, using anonymous request");
                client = CreateConfiguredHttpClient();
            }

            string json;
            using (client)
            {
                var response = await SendWithRetryAsync(client, apiUrl, cancellationToken);

                if (response == null || !response.IsSuccessStatusCode)
                {
                    _logger.LogError("GitHub API request failed after retries");

                    // Fallback to UpdateManager if available
                    if (_updateManager != null)
                    {
                        _logger.LogInformation("Falling back to UpdateManager.CheckForUpdatesAsync()");
                        var updateInfo = await _updateManager.CheckForUpdatesAsync();
                        if (updateInfo != null)
                        {
                            _cachedUpdateInfo = updateInfo;
                            _lastUpdateCheckTime = DateTime.UtcNow;
                        }

                        return updateInfo;
                    }

                    return null;
                }

                json = await response.Content.ReadAsStringAsync(cancellationToken);
            }

            JsonElement releases;
            try
            {
                releases = JsonSerializer.Deserialize<JsonElement>(json);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Failed to parse GitHub API response as JSON");
                _logger.LogDebug("Raw JSON response: {Json}", json);
                return null;
            }

            if (!releases.ValueKind.Equals(JsonValueKind.Array) || releases.GetArrayLength() == 0)
            {
                _logger.LogWarning("No releases found on GitHub");
                return null;
            }

            // Parse current version
            if (!SemanticVersion.TryParse(AppConstants.AppVersion, out var currentVersion))
            {
                _logger.LogError("Failed to parse current version: {Version}", AppConstants.AppVersion);
                return null;
            }

            _logger.LogDebug("Current version parsed: {Version}, Prerelease: {IsPrerelease}", currentVersion, currentVersion.IsPrerelease);

            // Find the latest release (including prereleases)
            SemanticVersion? latestVersion = null;
            JsonElement? latestRelease = null;

            foreach (var release in releases.EnumerateArray())
            {
                var tagName = release.GetProperty("tag_name").GetString();
                if (string.IsNullOrEmpty(tagName))
                    continue;

                // Remove 'v' prefix if present
                var versionString = tagName.TrimStart('v', 'V');

                if (!SemanticVersion.TryParse(versionString, out var releaseVersion))
                {
                    _logger.LogDebug("Skipping release with invalid version: {TagName}", tagName);
                    continue;
                }

                _logger.LogDebug("Found release: {Version}, Prerelease: {IsPrerelease}", releaseVersion, releaseVersion.IsPrerelease);

                if (latestVersion == null || releaseVersion > latestVersion)
                {
                    latestVersion = releaseVersion;
                    latestRelease = release;
                }
            }

            if (latestVersion == null || latestRelease == null)
            {
                _logger.LogWarning("No valid releases found");
                return null;
            }

            _logger.LogInformation("Latest available version: {Version}", latestVersion);
            _logger.LogInformation("Comparing: Current={Current} vs Latest={Latest}", currentVersion, latestVersion);

            // Check if update is available
            if (latestVersion <= currentVersion)
            {
                _logger.LogInformation("No update available. Current version {Current} is up to date", currentVersion);
                _cachedUpdateInfo = null;
                _lastUpdateCheckTime = DateTime.UtcNow;
                return null;
            }

            _logger.LogInformation("Update available: Current={Current}, Latest={Latest}", currentVersion, latestVersion);

            // Store GitHub update detection result
            _hasUpdateFromGitHub = true;
            _latestVersionFromGitHub = latestVersion.ToString();

            // If UpdateManager is available, use it to get proper UpdateInfo
            // Otherwise, return null (can still show user there's an update, but can't install)
            if (_updateManager != null)
            {
                try
                {
                    _logger.LogDebug("Calling UpdateManager.CheckForUpdatesAsync()");

                    var updateInfo = await _updateManager.CheckForUpdatesAsync();

                    _logger.LogDebug("UpdateManager.CheckForUpdatesAsync() completed. UpdateInfo is null: {IsNull}", updateInfo == null);
                    if (updateInfo != null)
                    {
                        _logger.LogDebug("UpdateInfo version: {Version}", updateInfo.TargetFullRelease.Version);
                    }

                    if (updateInfo != null)
                    {
                        _logger.LogInformation("✅ UpdateManager also confirmed update is available and can be installed");
                        _cachedUpdateInfo = updateInfo;
                        _lastUpdateCheckTime = DateTime.UtcNow;
                        return updateInfo;
                    }
                    else
                    {
                        _logger.LogWarning("⚠️ UpdateManager returned NULL - no update found via Velopack (but GitHub says there is one)");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "UpdateManager.CheckForUpdatesAsync failed");
                    _logger.LogWarning("Update is available from GitHub, but cannot be downloaded/installed due to UpdateManager exception");
                }
            }
            else
            {
                _logger.LogWarning("⚠️ UpdateManager is NULL - was not initialized successfully");
            }

            // Return null but flag is set (UI can show "update available" but disable install)
            _logger.LogWarning("⚠️ Update detected via GitHub API but UpdateManager unavailable (running from debug)");
            _logger.LogWarning("   Install the app using Setup.exe to enable automatic updates");

            _cachedUpdateInfo = null;
            _lastUpdateCheckTime = DateTime.UtcNow;
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check for updates");
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task DownloadUpdatesAsync(UpdateInfo updateInfo, IProgress<UpdateProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (_updateManager == null)
        {
            throw new InvalidOperationException("UpdateManager not initialized");
        }

        ArgumentNullException.ThrowIfNull(updateInfo);

        try
        {
            _logger.LogInformation("Downloading update {Version}...", updateInfo.TargetFullRelease.Version);

            // Wrap Velopack progress into our UpdateProgress model
            Action<int>? velopackProgress = null;
            if (progress != null)
            {
                velopackProgress = percent =>
                {
                    progress.Report(new UpdateProgress
                    {
                        PercentComplete = percent,
                        Message = $"Downloading update... {percent}%",
                        Status = "Downloading",
                    });
                };
            }

            await _updateManager.DownloadUpdatesAsync(updateInfo, velopackProgress, cancellationToken);

            progress?.Report(new UpdateProgress
            {
                PercentComplete = 100,
                Message = "Download complete",
                Status = "Downloaded",
                IsCompleted = true,
            });

            _logger.LogInformation("Update downloaded successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download updates");
            throw;
        }
    }

    /// <inheritdoc/>
    public void ApplyUpdatesAndRestart(UpdateInfo updateInfo)
    {
        if (_updateManager == null)
        {
            throw new InvalidOperationException("UpdateManager not initialized");
        }

        ArgumentNullException.ThrowIfNull(updateInfo);

        try
        {
            _logger.LogInformation("Applying update {Version} and restarting...", updateInfo.TargetFullRelease.Version);
            _logger.LogInformation("Update package: {Package}", updateInfo.TargetFullRelease.FileName);
            _logger.LogInformation("Current app will exit and restart with new version");

            _updateManager.ApplyUpdatesAndRestart(updateInfo.TargetFullRelease);

            // If we reach here, restart might have failed
            _logger.LogWarning("ApplyUpdatesAndRestart returned without exiting - this is unexpected");

            // Wait a bit for exit to happen
            Task.Delay(AppUpdateConstants.PostUpdateExitDelay).Wait();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply updates and restart. Attempting fallback to ApplyUpdatesAndExit...");

            // Try fallback to exit-only mode
            try
            {
                _updateManager.ApplyUpdatesAndExit(updateInfo.TargetFullRelease);
                _logger.LogInformation("Fallback to ApplyUpdatesAndExit succeeded. Please restart the application manually.");
            }
            catch (Exception fallbackEx)
            {
                _logger.LogError(fallbackEx, "Fallback to ApplyUpdatesAndExit also failed");
                throw new InvalidOperationException("Failed to apply update. Both restart and exit methods failed.", ex);
            }
        }
    }

    /// <inheritdoc/>
    public void ApplyUpdatesAndExit(UpdateInfo updateInfo)
    {
        if (_updateManager == null)
        {
            throw new InvalidOperationException("UpdateManager not initialized");
        }

        ArgumentNullException.ThrowIfNull(updateInfo);

        try
        {
            _logger.LogInformation("Applying update {Version} and exiting...", updateInfo.TargetFullRelease.Version);
            _updateManager.ApplyUpdatesAndExit(updateInfo.TargetFullRelease);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply updates and restart");
            throw;
        }
    }

    /// <inheritdoc/>
    public bool IsUpdatePendingRestart => _updateManager?.UpdatePendingRestart != null;

    /// <inheritdoc/>
    public bool HasUpdateAvailableFromGitHub
    {
        get
        {
            _logger.LogDebug("HasUpdateAvailableFromGitHub property accessed: {Value}", _hasUpdateFromGitHub);
            return _hasUpdateFromGitHub;
        }
    }

    /// <inheritdoc/>
    public string? LatestVersionFromGitHub
    {
        get
        {
            _logger.LogDebug("LatestVersionFromGitHub property accessed: '{Value}'", _latestVersionFromGitHub ?? "NULL");
            return _latestVersionFromGitHub;
        }
    }

    /// <inheritdoc/>
    public async Task<ArtifactUpdateInfo?> CheckForArtifactUpdatesAsync(CancellationToken cancellationToken = default)
    {
        // Check cache
        if (DateTime.UtcNow - _lastArtifactCheckTime < AppUpdateConstants.CacheDuration)
        {
            _logger.LogInformation("Returning cached artifact update info (checked {TimeLess} ago)", (DateTime.UtcNow - _lastArtifactCheckTime).ToString(@"mm\:ss"));
            return _cachedArtifactUpdateInfo;
        }

        _logger.LogInformation("Checking for artifact updates from GitHub Actions CI builds");

        if (_gitHubTokenStorage == null)
        {
            _logger.LogDebug("No GitHub token storage available, skipping artifact updates check");
            return null;
        }

        try
        {
            // Reset latest artifact if switching modes/channels
            _latestArtifactUpdate = null;

            // Priority:
            // 1. Subscribed PR
            // 2. Subscribed Branch
            // 3. Overall latest
            if (SubscribedPrNumber.HasValue)
            {
                _logger.LogInformation("Checking for artifacts for subscribed PR #{PrNumber}", SubscribedPrNumber.Value);
                var prs = await GetOpenPullRequestsAsync(cancellationToken);
                var subscribedPr = prs.FirstOrDefault(p => p.Number == SubscribedPrNumber.Value);
                _latestArtifactUpdate = subscribedPr?.LatestArtifact;
            }
            else if (!string.IsNullOrEmpty(SubscribedBranch))
            {
                _logger.LogInformation("Checking for artifacts for subscribed branch: {Branch}", SubscribedBranch);
                _latestArtifactUpdate = await FindLatestArtifactAsync(SubscribedBranch, cancellationToken);
            }
            else
            {
                _logger.LogInformation("Checking for overall latest artifact");
                _latestArtifactUpdate = await FindLatestArtifactAsync(null, cancellationToken);
            }

            _cachedArtifactUpdateInfo = _latestArtifactUpdate;
            _lastArtifactCheckTime = DateTime.UtcNow;
            return _latestArtifactUpdate;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check for artifact updates");
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<PullRequestInfo>> GetOpenPullRequestsAsync(CancellationToken cancellationToken = default)
    {
        // Check cache
        if (DateTime.UtcNow - _lastPrListCheckTime < AppUpdateConstants.CacheDuration && _cachedPrList != null)
        {
            _logger.LogInformation("Returning cached PR list (checked {TimeAgo} ago)", (DateTime.UtcNow - _lastPrListCheckTime).ToString(@"mm\:ss"));
            return _cachedPrList;
        }

        _logger.LogInformation("Fetching open pull requests with artifacts");

        // Reset merged/closed tracking
        IsPrMergedOrClosed = false;

        var results = new List<PullRequestInfo>();

        // Check if PAT is available
        if (_gitHubTokenStorage == null || !_gitHubTokenStorage.HasToken())
        {
            _logger.LogDebug("No GitHub PAT available, skipping PR list fetch");
            return results;
        }

        try
        {
            var token = await _gitHubTokenStorage.LoadTokenAsync();
            if (token == null)
            {
                _logger.LogWarning("Failed to load GitHub PAT");
                return results;
            }

            using var client = CreateConfiguredHttpClientWithToken(token);
            var owner = AppConstants.GitHubRepositoryOwner;
            var repo = AppConstants.GitHubRepositoryName;

            // Get open pull requests
            var prsUrl = string.Format(ApiConstants.GitHubApiPrsFormat, owner, repo);
            var prsResponse = await SendWithRetryAsync(client, prsUrl, cancellationToken);

            if (prsResponse == null || !prsResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch open PRs: {Status}", prsResponse?.StatusCode);
                return results;
            }

            var prsJson = await prsResponse.Content.ReadAsStringAsync(cancellationToken);
            var prsData = JsonSerializer.Deserialize<JsonElement>(prsJson);

            if (!prsData.ValueKind.Equals(JsonValueKind.Array))
            {
                return results;
            }

            // Track if subscribed PR is still open
            bool subscribedPrFound = false;
            var prTasks = new List<Task<PullRequestInfo>>();

            foreach (var pr in prsData.EnumerateArray())
            {
                var prJson = pr.Clone();
                prTasks.Add(Task.Run(
                    async () =>
                {
                    var prNumber = prJson.GetProperty("number").GetInt32();
                    var title = prJson.GetProperty("title").GetString() ?? GameClientConstants.UnknownVersion;
                    var branchName = prJson.TryGetProperty("head", out var head)
                        ? head.GetProperty("ref").GetString() ?? "unknown"
                        : "unknown";
                    var author = prJson.TryGetProperty("user", out var user)
                        ? user.GetProperty("login").GetString() ?? "unknown"
                        : "unknown";
                    var state = prJson.GetProperty("state").GetString() ?? "open";
                    var updatedAt = prJson.TryGetProperty("updated_at", out var updatedAtProp)
                        ? updatedAtProp.GetDateTimeOffset()
                        : (DateTimeOffset?)null;

                    // Find latest artifact for this PR
                    ArtifactUpdateInfo? latestArtifact = await FindLatestArtifactForPrAsync(client, prNumber, cancellationToken);

                    return new PullRequestInfo
                    {
                        Number = prNumber,
                        Title = title,
                        BranchName = branchName,
                        Author = author,
                        State = state,
                        UpdatedAt = updatedAt,
                        LatestArtifact = latestArtifact,
                    };
                },
                    cancellationToken));
            }

            var prInfos = await Task.WhenAll(prTasks);
            results.AddRange(prInfos);

            // Check if subscribed PR is still open
            subscribedPrFound = results.Any(p => p.Number == SubscribedPrNumber);
            if (SubscribedPrNumber.HasValue && !subscribedPrFound)
            {
                // PR is no longer in open PRs list - check if merged or closed
                var prStatusUrl = string.Format(ApiConstants.GitHubApiPrDetailFormat, owner, repo, SubscribedPrNumber);
                var statusResponse = await client.GetAsync(prStatusUrl, cancellationToken);

                if (statusResponse.IsSuccessStatusCode)
                {
                    var statusJson = await statusResponse.Content.ReadAsStringAsync(cancellationToken);
                    var statusData = JsonSerializer.Deserialize<JsonElement>(statusJson);
                    var statusState = statusData.GetProperty("state").GetString();

                    IsPrMergedOrClosed = statusState != null && !statusState.Equals("open", StringComparison.OrdinalIgnoreCase);
                    if (IsPrMergedOrClosed)
                    {
                        _logger.LogInformation("Subscribed PR #{PrNumber} has been merged/closed", SubscribedPrNumber);
                    }
                }
            }

            _logger.LogInformation("Found {Count} open PRs", results.Count);
            _cachedPrList = results;
            _lastPrListCheckTime = DateTime.UtcNow;
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch open pull requests");
            return results;
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<string>> GetBranchesAsync(CancellationToken cancellationToken = default)
    {
        // Check cache
        if (DateTime.UtcNow - _lastBranchListCheckTime < AppUpdateConstants.CacheDuration && _cachedBranchList != null)
        {
            _logger.LogInformation("Returning cached branch list (checked {TimeAgo} ago)", (DateTime.UtcNow - _lastBranchListCheckTime).ToString(@"mm\:ss"));
            return _cachedBranchList;
        }

        _logger.LogInformation("Fetching available branches");
        List<string> results = [];

        if (_gitHubTokenStorage == null || !_gitHubTokenStorage.HasToken())
        {
            _logger.LogDebug("No GitHub PAT available, skipping branch list fetch");

            // Return at least main & development as defaults if we can't fetch real ones
            return ["main", "development"];
        }

        try
        {
            var token = await _gitHubTokenStorage.LoadTokenAsync();
            if (token == null)
            {
                return ["main", "development"];
            }

            using var client = CreateConfiguredHttpClientWithToken(token);
            var owner = AppConstants.GitHubRepositoryOwner;
            var repo = AppConstants.GitHubRepositoryName;
            var branchesUrl = $"https://api.github.com/repos/{owner}/{repo}/branches?per_page=100";

            var response = await client.GetAsync(branchesUrl, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch branches: {Status}", response.StatusCode);
                return ["main", "development"];
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var branches = JsonSerializer.Deserialize<JsonElement>(json);

            if (branches.ValueKind == JsonValueKind.Array)
            {
                foreach (var branch in branches.EnumerateArray())
                {
                    var name = branch.GetProperty("name").GetString();
                    if (!string.IsNullOrEmpty(name))
                    {
                        results.Add(name);
                    }
                }
            }

            _logger.LogInformation("Found {Count} branches", results.Count);

            // Ensure main and development are always present if not found
            if (!results.Contains("main")) results.Add("main");
            if (!results.Contains("development")) results.Add("development");

            var sortedResults = results.OrderBy(b => b).ToList();
            _cachedBranchList = sortedResults;
            _lastBranchListCheckTime = DateTime.UtcNow;
            return sortedResults;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch branches");
            return ["main", "development"];
        }
    }

    /// <inheritdoc/>
    public async Task InstallArtifactAsync(
        ArtifactUpdateInfo artifactInfo,
        IProgress<UpdateProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(artifactInfo);

        if (_gitHubTokenStorage == null || !_gitHubTokenStorage.HasToken())
        {
            throw new InvalidOperationException("GitHub PAT required to download artifacts");
        }

        SimpleHttpServer? server = null;
        string? tempDir = null;

        try
        {
            var label = artifactInfo.PullRequestNumber.HasValue
                ? $"PR #{artifactInfo.PullRequestNumber}"
                : $"Branch {artifactInfo.ArtifactName}";

            progress?.Report(new UpdateProgress { Status = $"Downloading artifact for {label}...", PercentComplete = 0 });

            if (await _gitHubTokenStorage.LoadTokenAsync() is not { } token)
            {
                throw new InvalidOperationException("Failed to load GitHub PAT");
            }

            using var client = CreateConfiguredHttpClientWithToken(token);
            var owner = AppConstants.GitHubRepositoryOwner;
            var repo = AppConstants.GitHubRepositoryName;
            var artifactId = artifactInfo.ArtifactId;

            // Download artifact
            var downloadUrl = $"https://api.github.com/repos/{owner}/{repo}/actions/artifacts/{artifactId}/zip";
            _logger.LogInformation("Downloading {Label} artifact from {Url}", label, downloadUrl);

            // Create temp directory
            tempDir = Path.Combine(Path.GetTempPath(), $"genhub-art-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);

            var zipPath = Path.Combine(tempDir, "artifact.zip");

            // Download artifact
            var downloadProgress = new Progress<double>(p =>
            {
                progress?.Report(new UpdateProgress
                {
                    Status = $"Downloading artifact for {label}...",
                    PercentComplete = (int)(p * 0.3), // Scale 0-100% download to 0-30% total progress
                });
            });

            await DownloadFileWithProgressAsync(client, downloadUrl, zipPath, downloadProgress, cancellationToken);

            progress?.Report(new UpdateProgress { Status = "Extracting artifact...", PercentComplete = 30 });

            // Extract the ZIP
            ZipFile.ExtractToDirectory(zipPath, tempDir);

            // Find .nupkg file
            var nupkgFiles = Directory.GetFiles(tempDir, "*.nupkg", SearchOption.AllDirectories);

            if (nupkgFiles.Length == 0)
            {
                throw new FileNotFoundException("No .nupkg file found in artifact");
            }

            var nupkgFile = nupkgFiles[0];
            _logger.LogInformation("Found nupkg: {File}", Path.GetFileName(nupkgFile));

            // Create releases.win.json
            var releasesPath = Path.Combine(tempDir, "releases.win.json");
            var nupkgFileName = Path.GetFileName(nupkgFile);
            var fileInfo = new FileInfo(nupkgFile);
            var sha1 = CalculateSHA1(nupkgFile);
            var sha256 = CalculateSHA256(nupkgFile);

            // Extract version from nupkg filename
            var versionMatch = NupkgVersionRegex().Match(nupkgFileName);
            var fileVersion = versionMatch.Success ? versionMatch.Groups[1].Value : artifactInfo.Version;

            var releasesJson = new
            {
                Assets = new[]
                {
                    new
                    {
                        PackageId = AppConstants.AppName,
                        Version = fileVersion,
                        Type = "Full",
                        FileName = nupkgFileName,
                        SHA1 = sha1,
                        SHA256 = sha256,
                        Size = fileInfo.Length,
                    },
                },
            };

            var jsonContent = JsonSerializer.Serialize(releasesJson);
            await File.WriteAllTextAsync(releasesPath, jsonContent, cancellationToken);
            _logger.LogInformation("Created releases.win.json with version {Version}", fileVersion);

            progress?.Report(new UpdateProgress { Status = "Starting local server...", PercentComplete = 50 });

            // Start HTTP server
            var port = FindAvailablePort();
            server = new SimpleHttpServer(nupkgFile, releasesPath, port, _logger);
            server.Start();

            progress?.Report(new UpdateProgress { Status = "Preparing update...", PercentComplete = 60 });

            progress?.Report(new UpdateProgress { Status = "Downloading update...", PercentComplete = 70 });

            // Point Velopack to localhost
            var source = new SimpleWebSource($"http://localhost:{port}/{server.SecretToken}/");
            var localUpdateManager = new UpdateManager(source);

            try
            {
                // Create asset description manually
                var asset = new VelopackAsset
                {
                    PackageId = AppConstants.AppName,
                    Version = SemanticVersion.Parse(fileVersion),
                    Type = VelopackAssetType.Full,
                    FileName = nupkgFileName,
                    SHA1 = sha1,
                    SHA256 = sha256,
                    Size = fileInfo.Length,
                };

                // Manually construct UpdateInfo to force the update (IsDowngrade = true)
                // This bypasses the version check that prevents installing older versions/artifacts
                var updateInfo = new UpdateInfo(asset, true);

                // Download from localhost
                await localUpdateManager.DownloadUpdatesAsync(
                    updateInfo,
                    p =>
                    {
                        progress?.Report(new UpdateProgress
                        {
                            Status = "Downloading update...",
                            PercentComplete = 70 + (int)(p * 0.2),
                        });
                    },
                    cancellationToken);

                progress?.Report(new UpdateProgress { Status = "Installing update...", PercentComplete = 90 });

                _logger.LogInformation("Applying {Label} update and restarting", label);

                localUpdateManager.ApplyUpdatesAndRestart(updateInfo.TargetFullRelease);

                _logger.LogWarning("ApplyUpdatesAndRestart returned without exiting - waiting for exit...");
                await Task.Delay(AppUpdateConstants.PostUpdateExitDelay, cancellationToken);

                _logger.LogError("Application did not exit after ApplyUpdatesAndRestart. Update may have failed.");
                throw new InvalidOperationException("Application did not exit after applying update");
            }
            finally
            {
                // No cleanup needed for UpdateManager
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to install artifact");
            progress?.Report(new UpdateProgress { Status = "Installation failed", HasError = true, ErrorMessage = ex.Message });
            throw;
        }
        finally
        {
            // Cleanup
            server?.Dispose();

            if (tempDir != null && Directory.Exists(tempDir))
            {
                try
                {
                    Directory.Delete(tempDir, recursive: true);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to cleanup temp directory: {Path}", tempDir);
                }
            }
        }
    }

    /// <inheritdoc/>
    public async Task InstallPrArtifactAsync(
        PullRequestInfo prInfo,
        IProgress<UpdateProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (prInfo.LatestArtifact == null)
        {
            throw new InvalidOperationException($"PR #{prInfo.Number} has no artifacts available");
        }

        await InstallArtifactAsync(prInfo.LatestArtifact, progress, cancellationToken);
    }

    /// <inheritdoc/>
    public void ClearCache()
    {
        _lastUpdateCheckTime = DateTime.MinValue;
        _cachedUpdateInfo = null;
        _lastArtifactCheckTime = DateTime.MinValue;
        _cachedArtifactUpdateInfo = null;
        _lastPrListCheckTime = DateTime.MinValue;
        _cachedPrList = null;
        _lastBranchListCheckTime = DateTime.MinValue;
        _cachedBranchList = null;
        _hasUpdateFromGitHub = false;
        _latestVersionFromGitHub = null;
        _logger.LogInformation("Update manager cache cleared");
    }

    /// <inheritdoc/>
    public void Uninstall()
    {
        try
        {
            // Update.exe is typically in the parent directory of the current app directory (app-{version})
            var updateExe = System.IO.Path.Combine(AppContext.BaseDirectory, "..", "Update.exe");

            // Normalize path
            updateExe = System.IO.Path.GetFullPath(updateExe);

            if (System.IO.File.Exists(updateExe))
            {
                _logger.LogInformation("Invoking uninstaller: {Path}", updateExe);
                Process.Start(new ProcessStartInfo(updateExe, "--uninstall") { UseShellExecute = true });
                Environment.Exit(0);
            }
            else
            {
                _logger.LogWarning("Update.exe not found at {Path}. Uninstall not possible (Debug/Portable mode?)", updateExe);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to uninstall application");
            throw; // Re-throw so ViewModel can show error
        }
    }

    /// <summary>
    /// Extracts version from artifact name.
    /// Expected format: genhub-velopack-{platform}-{version}.
    /// </summary>
    private static string? ExtractVersionFromArtifactName(string artifactName)
    {
        var prefixes = new[] { "genhub-velopack-windows-", "genhub-velopack-linux-" };

        foreach (var prefix in prefixes)
        {
            if (artifactName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                var version = artifactName[prefix.Length..];
                return string.IsNullOrWhiteSpace(version) ? null : version;
            }
        }

        return null;
    }

    /// <summary>
    /// Uses a SecureString as plain text in a callback to minimize memory exposure.
    /// </summary>
    private static void UseSecureStringAsPlainText(SecureString secureString, Action<string> callback)
    {
        var ptr = Marshal.SecureStringToGlobalAllocUnicode(secureString);
        try
        {
            var plainText = Marshal.PtrToStringUni(ptr) ?? string.Empty;
            callback(plainText);
        }
        finally
        {
            Marshal.ZeroFreeGlobalAllocUnicode(ptr);
        }
    }

    /// <summary>
    /// Calculates SHA1 hash of a file.
    /// </summary>
    private static string CalculateSHA1(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        using var sha1 = System.Security.Cryptography.SHA1.Create();
        var hash = sha1.ComputeHash(stream);
        return BitConverter.ToString(hash).Replace("-", string.Empty);
    }

    /// <summary>
    /// Calculates SHA256 hash of a file.
    /// </summary>
    private static string CalculateSHA256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var hash = sha256.ComputeHash(stream);
        return BitConverter.ToString(hash).Replace("-", string.Empty);
    }

    /// <summary>
    /// Finds an available network port.
    /// </summary>
    private static int FindAvailablePort()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>
    /// Downloads a file with progress reporting.
    /// </summary>
    private static async Task DownloadFileWithProgressAsync(
        HttpClient client,
        string requestUrl,
        string destinationPath,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(requestUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1L;
        var canReportProgress = totalBytes != -1L;

        // Create temp directory if it doesn't exist (handle case where this method creates the file)
        var directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

        var totalRead = 0L;
        var buffer = new byte[8192];
        var isMoreToRead = true;

        while (isMoreToRead)
        {
            var read = await contentStream.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                isMoreToRead = false;
            }
            else
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);

                totalRead += read;
                if (canReportProgress && progress != null)
                {
                    progress.Report((double)totalRead / totalBytes * 100);
                }
            }
        }
    }

    /// <summary>
    /// Gets or creates an HttpClient instance with proper configuration.
    /// </summary>
    /// <returns>An HttpClient instance.</returns>
    private HttpClient CreateConfiguredHttpClient()
    {
        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.UserAgent.Clear();
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue(AppConstants.AppName, AppConstants.AppVersion));
        return client;
    }

    /// <summary>
    /// Creates an HttpClient with token authentication.
    /// </summary>
    private HttpClient CreateConfiguredHttpClientWithToken(SecureString token)
    {
        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.UserAgent.Clear();
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue(AppConstants.AppName, AppConstants.AppVersion));
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue(ApiConstants.GitHubApiHeaderAccept));

        UseSecureStringAsPlainText(token, plainText =>
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", plainText);
        });

        return client;
    }

    /// <summary>
    /// Sends a GET request with retry logic.
    /// </summary>
    private async Task<HttpResponseMessage?> SendWithRetryAsync(
        HttpClient client,
        string url,
        CancellationToken cancellationToken,
        int maxRetries = AppUpdateConstants.MaxHttpRetries)
    {
        HttpResponseMessage? response = null;
        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                response = await client.GetAsync(url, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    return response;
                }

                _logger.LogWarning("HTTP request failed (Attempt {Count}): {StatusCode} for {Url}", i + 1, response.StatusCode, url);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "HTTP request exception (Attempt {Count}) for {Url}", i + 1, url);
            }

            if (i < maxRetries - 1)
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, i + 1)), cancellationToken);
            }
        }

        return response;
    }

    /// <summary>
    /// Finds the latest artifact for a specific PR.
    /// </summary>
    private async Task<ArtifactUpdateInfo?> FindLatestArtifactForPrAsync(
        HttpClient client,
        int prNumber,
        CancellationToken cancellationToken)
    {
        try
        {
            var owner = AppConstants.GitHubRepositoryOwner;
            var repo = AppConstants.GitHubRepositoryName;

            _logger.LogInformation("Searching for artifacts for PR #{PrNumber}", prNumber);

            // First, get the PR details to find the head branch
            var prUrl = string.Format(ApiConstants.GitHubApiPrDetailFormat, owner, repo, prNumber);
            var prResponse = await SendWithRetryAsync(client, prUrl, cancellationToken);

            if (prResponse == null || !prResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch PR #{PrNumber} details: {Status}", prNumber, prResponse?.StatusCode);
                return null;
            }

            var prJson = await prResponse.Content.ReadAsStringAsync(cancellationToken);
            var prData = JsonSerializer.Deserialize<JsonElement>(prJson);

            var headBranch = prData.TryGetProperty("head", out var head)
                ? head.GetProperty("ref").GetString() ?? string.Empty
                : string.Empty;

            if (string.IsNullOrEmpty(headBranch))
            {
                _logger.LogWarning("Could not determine head branch for PR #{PrNumber}", prNumber);
                return null;
            }

            _logger.LogInformation("PR #{PrNumber} head branch: {Branch}", prNumber, headBranch);

            // Fetch workflow runs for this branch
            var runsUrl = string.Format(ApiConstants.GitHubApiWorkflowRunsFormat, owner, repo, headBranch);
            var runsResponse = await SendWithRetryAsync(client, runsUrl, cancellationToken);

            if (runsResponse == null || !runsResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch workflow runs for PR #{PrNumber}: {Status}", prNumber, runsResponse?.StatusCode);
                return null;
            }

            var runsJson = await runsResponse.Content.ReadAsStringAsync(cancellationToken);
            var runsData = JsonSerializer.Deserialize<JsonElement>(runsJson);

            if (!runsData.TryGetProperty("workflow_runs", out var runs))
            {
                _logger.LogWarning("No workflow_runs property in response for PR #{PrNumber}", prNumber);
                return null;
            }

            var runCount = runs.GetArrayLength();
            _logger.LogInformation("Found {Count} workflow runs for PR #{PrNumber} on branch {Branch}", runCount, prNumber, headBranch);

            foreach (var run in runs.EnumerateArray())
            {
                var runId = run.GetProperty("id").GetInt64();
                var runBranch = run.TryGetProperty("head_branch", out var hb) ? hb.GetString() : string.Empty;

                _logger.LogDebug("Checking workflow run {RunId} for branch {Branch}", runId, runBranch);

                // Verify this run is actually for our PR branch
                if (!string.Equals(runBranch, headBranch, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogDebug("Skipping run {RunId} - branch mismatch: {RunBranch} != {HeadBranch}", runId, runBranch, headBranch);
                    continue;
                }

                var runUrl = run.GetProperty("html_url").GetString() ?? string.Empty;

                DateTime createdAt;
                try
                {
                    createdAt = run.GetProperty("created_at").GetDateTime();
                }
                catch (FormatException ex)
                {
                    _logger.LogWarning(ex, "Failed to parse created_at date from workflow run");
                    createdAt = DateTime.MinValue;
                }

                var headSha = run.GetProperty("head_sha").GetString() ?? string.Empty;
                var shortHash = headSha.Length >= AppConstants.GitShortHashLength ? headSha[..AppConstants.GitShortHashLength] : headSha;

                _logger.LogInformation("Fetching artifacts for workflow run {RunId} (PR #{PrNumber})", runId, prNumber);

                var artifactsUrl = string.Format(ApiConstants.GitHubApiRunArtifactsFormat, owner, repo, runId);
                var artifactsResponse = await SendWithRetryAsync(client, artifactsUrl, cancellationToken);

                if (artifactsResponse == null || !artifactsResponse.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Failed to fetch artifacts for run {RunId}: {Status}", runId, artifactsResponse?.StatusCode);
                    continue;
                }

                var artifactsJson = await artifactsResponse.Content.ReadAsStringAsync(cancellationToken);
                var artifactsData = JsonSerializer.Deserialize<JsonElement>(artifactsJson);

                if (!artifactsData.TryGetProperty("artifacts", out var artifacts))
                {
                    _logger.LogWarning("No artifacts property in response for run {RunId}", runId);
                    continue;
                }

                var artifactCount = artifacts.GetArrayLength();
                _logger.LogInformation("Found {Count} artifacts for run {RunId}", artifactCount, runId);

                ArtifactUpdateInfo? windowsArtifact = null;
                ArtifactUpdateInfo? fallbackArtifact = null;

                foreach (var artifact in artifacts.EnumerateArray())
                {
                    var artifactName = artifact.GetProperty("name").GetString() ?? string.Empty;
                    _logger.LogDebug("Checking artifact: {Name}", artifactName);

                    if (!artifactName.Contains("velopack", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogDebug("Skipping artifact {Name} - doesn't contain 'velopack'", artifactName);
                        continue;
                    }

                    _logger.LogInformation("Found Velopack artifact: {Name}", artifactName);

                    var artifactId = artifact.GetProperty("id").GetInt64();
                    var version = ExtractVersionFromArtifactName(artifactName) ?? $"PR{prNumber}";

                    var artifactInfo = new ArtifactUpdateInfo(
                        version: version,
                        gitHash: shortHash,
                        pullRequestNumber: prNumber,
                        workflowRunId: runId,
                        workflowRunUrl: runUrl,
                        artifactId: artifactId,
                        artifactName: artifactName,
                        createdAt: createdAt);

                    if (artifactName.Contains("windows", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogInformation("Selected Windows artifact: {Name} (ID: {Id})", artifactName, artifactId);
                        windowsArtifact = artifactInfo;
                        break;
                    }
                    else if (fallbackArtifact == null && !artifactName.Contains("linux", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogDebug("Found fallback artifact: {Name}", artifactName);
                        fallbackArtifact = artifactInfo;
                    }
                }

                var selectedArtifact = windowsArtifact ?? fallbackArtifact;
                if (selectedArtifact != null)
                {
                    _logger.LogInformation("Found artifact for PR #{PrNumber}: {Version}", prNumber, selectedArtifact.Version);
                    return selectedArtifact;
                }

                _logger.LogDebug("No suitable artifacts found in run {RunId}, checking next run", runId);
            }

            _logger.LogWarning("No artifacts found for PR #{PrNumber} across all workflow runs", prNumber);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to find latest artifact for PR #{PrNumber}", prNumber);
            return null;
        }
    }

    /// <summary>
    /// Finds the latest artifact overall or for a specific branch (for artifact update checking).
    /// </summary>
    private async Task<ArtifactUpdateInfo?> FindLatestArtifactAsync(string? branch, CancellationToken cancellationToken)
    {
        if (_gitHubTokenStorage == null || !_gitHubTokenStorage.HasToken())
            return null;

        try
        {
            var token = await _gitHubTokenStorage.LoadTokenAsync();
            if (token == null)
                return null;

            using var client = CreateConfiguredHttpClientWithToken(token);
            var owner = AppConstants.GitHubRepositoryOwner;
            var repo = AppConstants.GitHubRepositoryName;

            string runsUrl;

            // Always request multiple runs to ensure we can find one with artifacts if the latest failed/expired
            if (!string.IsNullOrEmpty(branch))
            {
                _logger.LogInformation("Searching for latest workflow success on branch: {Branch}", branch);
                runsUrl = string.Format(ApiConstants.GitHubApiWorkflowRunsFormat, owner, repo, branch);
            }
            else
            {
                _logger.LogInformation("Searching for overall latest workflow success");

                // Use the same format but without branch param, creating a custom URL since Constant has per_page=1
                runsUrl = $"https://api.github.com/repos/{owner}/{repo}/actions/runs?status=success&event=push&per_page=10";
            }

            var runsResponse = await SendWithRetryAsync(client, runsUrl, cancellationToken);

            if (runsResponse == null || !runsResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch workflow runs: {Status}", runsResponse?.StatusCode);
                return null;
            }

            var runsJson = await runsResponse.Content.ReadAsStringAsync(cancellationToken);
            var runsData = JsonSerializer.Deserialize<JsonElement>(runsJson);

            if (!runsData.TryGetProperty("workflow_runs", out var runs) || runs.GetArrayLength() == 0)
            {
                _logger.LogWarning("No workflow runs found in response for URL: {Url}", runsUrl);
                return null;
            }

            // Iterate through runs to find the first one with valid artifacts
            foreach (var run in runs.EnumerateArray())
            {
                var runId = run.GetProperty("id").GetInt64();
                var runUrl = run.GetProperty("html_url").GetString() ?? string.Empty;
                var eventType = run.TryGetProperty("event", out var e) ? e.GetString() : "unknown";
                var headSha = run.GetProperty("head_sha").GetString() ?? string.Empty;
                var shortHash = headSha.Length >= AppConstants.GitShortHashLength ? headSha[..AppConstants.GitShortHashLength] : headSha;
                var actualBranch = run.TryGetProperty("head_branch", out var b) ? b.GetString() : branch ?? "unknown";

                // CRITICAL FIX: Only accept 'push' events for branch/latest updates.
                // 'pull_request' events should ONLY be handled by specific PR subscriptions.
                if (!string.Equals(eventType, "push", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogDebug("Skipping run {RunId} ({EventType}) - only 'push' events are valid for branch subscriptions", runId, eventType);
                    continue;
                }

                // Double-check branch name if specified
                if (!string.IsNullOrEmpty(branch) && !string.Equals(actualBranch, branch, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogDebug("Skipping run {RunId} ({ActualBranch}) - does not match requested branch {Branch}", runId, actualBranch, branch);
                    continue;
                }

                DateTime createdAt;
                try
                {
                    createdAt = run.GetProperty("created_at").GetDateTime();
                }
                catch (FormatException)
                {
                    createdAt = DateTime.MinValue;
                }

                _logger.LogDebug("Checking run {RunId} on branch {Branch} ({Hash}) for artifacts...", runId, actualBranch, shortHash);

                var artifactsUrl = string.Format(ApiConstants.GitHubApiRunArtifactsFormat, owner, repo, runId);
                var artifactsResponse = await SendWithRetryAsync(client, artifactsUrl, cancellationToken);

                if (artifactsResponse == null || !artifactsResponse.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Failed to fetch artifacts for run {RunId}: {Status}", runId, artifactsResponse?.StatusCode);
                    continue;
                }

                var artifactsJson = await artifactsResponse.Content.ReadAsStringAsync(cancellationToken);
                var artifactsData = JsonSerializer.Deserialize<JsonElement>(artifactsJson);

                if (!artifactsData.TryGetProperty("artifacts", out var artifacts) || artifacts.GetArrayLength() == 0)
                {
                    _logger.LogDebug("No artifacts found in run {RunId}, checking next run", runId);
                    continue;
                }

                // Filter artifacts by platform to prevent cross-platform installation
                ArtifactUpdateInfo? windowsArtifact = null;
                ArtifactUpdateInfo? linuxArtifact = null;
                ArtifactUpdateInfo? fallbackArtifact = null;

                foreach (var artifact in artifacts.EnumerateArray())
                {
                    var artifactName = artifact.GetProperty("name").GetString() ?? string.Empty;

                    if (!artifactName.Contains("velopack", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogDebug("Skipping artifact {Name} - doesn't contain 'velopack'", artifactName);
                        continue;
                    }

                    _logger.LogInformation("Found Velopack artifact: {Name} (ID: {Id}) in run {RunId}", artifactName, artifact.GetProperty("id").GetInt64(), runId);

                    var artifactId = artifact.GetProperty("id").GetInt64();
                    var version = ExtractVersionFromArtifactName(artifactName) ?? "unknown";

                    var artifactInfo = new ArtifactUpdateInfo(
                        version: version,
                        gitHash: shortHash,
                        pullRequestNumber: null,
                        workflowRunId: runId,
                        workflowRunUrl: runUrl,
                        artifactId: artifactId,
                        artifactName: artifactName,
                        createdAt: createdAt);

                    // Categorize by platform
                    if (artifactName.Contains("windows", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogDebug("Found Windows artifact: {Name}", artifactName);
                        windowsArtifact = artifactInfo;
                    }
                    else if (artifactName.Contains("linux", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogDebug("Found Linux artifact: {Name}", artifactName);
                        linuxArtifact = artifactInfo;
                    }
                    else if (fallbackArtifact == null)
                    {
                        _logger.LogDebug("Found platform-agnostic artifact: {Name}", artifactName);
                        fallbackArtifact = artifactInfo;
                    }
                }

                // Select the appropriate artifact based on current platform
                ArtifactUpdateInfo? selectedArtifact = null;

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    selectedArtifact = windowsArtifact ?? fallbackArtifact;
                    if (selectedArtifact != null)
                    {
                        _logger.LogInformation("Selected Windows artifact: {Name} (ID: {Id})", selectedArtifact.ArtifactName, selectedArtifact.ArtifactId);
                    }
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    selectedArtifact = linuxArtifact ?? fallbackArtifact;
                    if (selectedArtifact != null)
                    {
                        _logger.LogInformation("Selected Linux artifact: {Name} (ID: {Id})", selectedArtifact.ArtifactName, selectedArtifact.ArtifactId);
                    }
                }
                else
                {
                    selectedArtifact = fallbackArtifact;
                    if (selectedArtifact != null)
                    {
                        _logger.LogWarning("Unknown platform, using fallback artifact: {Name} (ID: {Id})", selectedArtifact.ArtifactName, selectedArtifact.ArtifactId);
                    }
                }

                if (selectedArtifact != null)
                {
                    return selectedArtifact;
                }

                _logger.LogDebug("No suitable Velopack artifacts found for current platform in run {RunId}, checking next run", runId);
            }

            _logger.LogWarning("No suitable artifacts found in the last 10 'push' runs for branch {Branch}", branch ?? "any");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to find latest artifact for branch {Branch}", branch ?? "any");
            return null;
        }
    }
}
