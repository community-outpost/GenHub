using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Interfaces.Telemetry;
using GenHub.Core.Models.AppUpdate;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Services.Tools;
using GenHub.Features.AppUpdate.Interfaces;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
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
using System.Xml.Linq;
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
    private readonly IGitHubAuthService? _gitHubAuthService;
    private readonly IUserSettingsService? _userSettingsService;
    private readonly IFileDownloader _fileDownloader;
    private readonly ITelemetryService? _telemetryService;
    private readonly UpdateManager? _updateManager;
    private readonly GithubSource _githubSource;
    private readonly IContentManifestPool? _contentManifestPool;
    private readonly IContentStorageService? _contentStorageService;
    private readonly IInstallationLocationTracker? _installationLocationTracker;

    private bool _hasUpdateFromGitHub;
    private string? _latestVersionFromGitHub;
    private ArtifactUpdateInfo? _latestArtifactUpdate;

    // Caching fields
    private DateTime _lastUpdateCheckTime = DateTime.MinValue;
    private UpdateInfo? _cachedUpdateInfo;
    private DateTime _lastArtifactCheckTime = DateTime.MinValue;
    private ArtifactUpdateInfo? _cachedArtifactUpdateInfo;
    private int? _cachedArtifactSubscribedPrNumber;
    private string? _cachedArtifactSubscribedBranch;
    private DateTime _lastPrListCheckTime = DateTime.MinValue;
    private IReadOnlyList<PullRequestInfo>? _cachedPrList;
    private DateTime _lastBranchListCheckTime = DateTime.MinValue;
    private IReadOnlyList<string>? _cachedBranchList;

    private int? _subscribedPrNumber;
    private string? _subscribedBranch;

    /// <inheritdoc/>
    public bool HasArtifactUpdateAvailable => _latestArtifactUpdate != null;

    /// <inheritdoc/>
    public ArtifactUpdateInfo? LatestArtifactUpdate => _latestArtifactUpdate;

    /// <inheritdoc/>
    public int? SubscribedPrNumber
    {
        get => _subscribedPrNumber;
        set
        {
            if (_subscribedPrNumber != value)
            {
                _subscribedPrNumber = value;
                _cachedArtifactUpdateInfo = null;
                _lastArtifactCheckTime = DateTime.MinValue;
            }
        }
    }

    /// <inheritdoc/>
    public string? SubscribedBranch
    {
        get => _subscribedBranch;
        set
        {
            if (!string.Equals(_subscribedBranch, value, StringComparison.OrdinalIgnoreCase))
            {
                _subscribedBranch = value;
                _cachedArtifactUpdateInfo = null;
                _lastArtifactCheckTime = DateTime.MinValue;
            }
        }
    }

    /// <inheritdoc/>
    public bool IsPrMergedOrClosed { get; private set; }

    /// <summary>
    /// Gets the application version used for update evaluation.
    /// Defaults to <see cref="AppConstants.AppVersion"/>.
    /// </summary>
    internal string CurrentAppVersion { get; init; } = AppConstants.AppVersion;

    /// <summary>
    /// Gets a value indicating whether this instance represents a local development build.
    /// Defaults to <see cref="AppConstants.IsLocalBuild"/>.
    /// </summary>
    internal bool IsLocalDevelopmentBuild { get; init; } = AppConstants.IsLocalBuild;

    /// <summary>
    /// Initializes a new instance of the <see cref="VelopackUpdateManager"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="httpClientFactory">The HTTP client factory for creating HttpClient instances.</param>
    /// <param name="gitHubAuthService">The GitHub authentication service (optional).</param>
    /// <param name="userSettingsService">The user settings service (optional).</param>
    /// <param name="fileDownloader">The high-performance file downloader (optional).</param>
    /// <param name="telemetryService">The telemetry service (optional).</param>
    /// <param name="contentManifestPool">The content manifest pool (optional).</param>
    /// <param name="contentStorageService">The content storage service (optional).</param>
    /// <param name="installationLocationTracker">The installation location tracker (optional).</param>
    [SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters", Justification = "Update manager requires DI dependencies for updater operations.")]
    public VelopackUpdateManager(
        ILogger<VelopackUpdateManager> logger,
        IHttpClientFactory httpClientFactory,
        IGitHubAuthService? gitHubAuthService = null,
        IUserSettingsService? userSettingsService = null,
        IFileDownloader? fileDownloader = null,
        ITelemetryService? telemetryService = null,
        IContentManifestPool? contentManifestPool = null,
        IContentStorageService? contentStorageService = null,
        IInstallationLocationTracker? installationLocationTracker = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _gitHubAuthService = gitHubAuthService;
        _userSettingsService = userSettingsService;
        _fileDownloader = fileDownloader ?? new FastHttpClientFileDownloader();
        _telemetryService = telemetryService;
        _contentManifestPool = contentManifestPool;
        _contentStorageService = contentStorageService;
        _installationLocationTracker = installationLocationTracker;

        // Always initialize GithubSource for update checking with high-performance downloader
        _githubSource = new GithubSource(AppConstants.GitHubRepositoryUrl, string.Empty, true, _fileDownloader);

        _ = Task.Run(() => SweepStaleBuildDirectories(_logger));

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
    /// Initializes a new instance of the <see cref="VelopackUpdateManager"/> class with version overrides for testing.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="httpClientFactory">The HTTP client factory for creating HttpClient instances.</param>
    /// <param name="gitHubAuthService">The GitHub authentication service (optional).</param>
    /// <param name="userSettingsService">The user settings service (optional).</param>
    /// <param name="fileDownloader">The high-performance file downloader (optional).</param>
    /// <param name="currentAppVersion">The current application version to evaluate against updates.</param>
    /// <param name="isLocalDevelopmentBuild">Whether this instance should be treated as a local development build.</param>
    internal VelopackUpdateManager(
        ILogger<VelopackUpdateManager> logger,
        IHttpClientFactory httpClientFactory,
        IGitHubAuthService? gitHubAuthService,
        IUserSettingsService? userSettingsService,
        IFileDownloader? fileDownloader,
        string currentAppVersion,
        bool isLocalDevelopmentBuild)
        : this(logger, httpClientFactory, gitHubAuthService, userSettingsService, fileDownloader)
    {
        CurrentAppVersion = currentAppVersion;
        IsLocalDevelopmentBuild = isLocalDevelopmentBuild;
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

    private string TelemetryChannel
    {
        get
        {
            if (_subscribedPrNumber.HasValue)
            {
                return $"{TelemetryConstants.PullRequestChannelPrefix}{_subscribedPrNumber}";
            }

            return _subscribedBranch ?? TelemetryConstants.ReleaseChannel;
        }
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

        if (IsLocalDevelopmentBuild)
        {
            _logger.LogInformation(
                "Skipping release update check for local development build (Current={Current})",
                CurrentAppVersion);
            _cachedUpdateInfo = null;
            _lastUpdateCheckTime = DateTime.UtcNow;
            return null;
        }

        _logger.LogInformation("Starting GitHub update check for repository: {Url}", AppConstants.GitHubRepositoryUrl);

        _telemetryService?.TrackEvent(TelemetryConstants.Events.AppUpdateChecked, new Dictionary<string, object?>
        {
            [TelemetryConstants.Properties.FromVersion] = CurrentAppVersion,
            [TelemetryConstants.Properties.FullDisplayVersion] = AppConstants.FullDisplayVersion,
            [TelemetryConstants.Properties.BuildChannel] = AppConstants.BuildChannel,
            [TelemetryConstants.Properties.Channel] = TelemetryChannel,
            [TelemetryConstants.Properties.Platform] = RuntimeInformation.OSDescription,
        });

        try
        {
            var uri = new Uri(AppConstants.GitHubRepositoryUrl);
            var pathParts = uri.AbsolutePath.Trim('/').Split('/');
            if (pathParts.Length < 2)
            {
                _logger.LogError("Invalid GitHub repository URL format: {Url}", AppConstants.GitHubRepositoryUrl);
                return null;
            }

            var owner = pathParts[0];
            var repo = pathParts[1];

            _logger.LogInformation("Fetching releases from GitHub API: {Owner}/{Repo}", owner, repo);

            var json = await FetchGitHubReleasesJsonAsync(owner, repo, cancellationToken);
            if (json == null)
            {
                return await CheckViaUpdateManagerAsync();
            }

            if (!TryParseReleases(json, out var releases))
            {
                return null;
            }

            if (!SemanticVersion.TryParse(CurrentAppVersion, out var currentVersion))
            {
                _logger.LogError("Failed to parse current version: {Version}", CurrentAppVersion);
                return null;
            }

            _logger.LogDebug("Current version parsed: {Version}, Prerelease: {IsPrerelease}", currentVersion, currentVersion.IsPrerelease);

            var (latestVersion, latestRelease) = ParseLatestRelease(releases);
            if (latestVersion == null || latestRelease == null)
            {
                _logger.LogWarning("No valid releases found");
                return null;
            }

            _logger.LogInformation("Latest available version: {Version}", latestVersion);
            _logger.LogInformation("Comparing: Current={Current} vs Latest={Latest}", currentVersion, latestVersion);

            if (latestVersion <= currentVersion)
            {
                _logger.LogInformation("No update available. Current version {Current} is up to date", currentVersion);
                _cachedUpdateInfo = null;
                _lastUpdateCheckTime = DateTime.UtcNow;
                return null;
            }

            _logger.LogInformation("Update available: Current={Current}, Latest={Latest}", currentVersion, latestVersion);
            _hasUpdateFromGitHub = true;
            _latestVersionFromGitHub = latestVersion.ToString();

            var updateInfo = await TryGetInstalledUpdateInfoAsync();
            if (updateInfo != null)
            {
                return updateInfo;
            }

            _logger.LogWarning("Update detected via GitHub API but UpdateManager unavailable (running from debug)");
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
            _telemetryService?.TrackEvent(TelemetryConstants.Events.AppUpdateDownloaded, new Dictionary<string, object?>
            {
                [TelemetryConstants.Properties.FromVersion] = CurrentAppVersion,
                [TelemetryConstants.Properties.ToVersion] = updateInfo.TargetFullRelease.Version.ToString(),
                [TelemetryConstants.Properties.FullDisplayVersion] = AppConstants.FullDisplayVersion,
                [TelemetryConstants.Properties.BuildChannel] = AppConstants.BuildChannel,
                [TelemetryConstants.Properties.Channel] = TelemetryChannel,
                [TelemetryConstants.Properties.Platform] = RuntimeInformation.OSDescription,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download updates");
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task ApplyUpdatesAndRestartAsync(UpdateInfo updateInfo, CancellationToken cancellationToken = default)
    {
        if (_updateManager == null)
        {
            throw new InvalidOperationException("UpdateManager not initialized");
        }

        ArgumentNullException.ThrowIfNull(updateInfo);

        try
        {
            CleanStrayAppDirectoryArtifacts();
            _logger.LogInformation("Applying update {Version} and restarting...", updateInfo.TargetFullRelease.Version);
            _logger.LogInformation("Update package: {Package}", updateInfo.TargetFullRelease.FileName);
            _logger.LogInformation("Current app will exit and restart with new version");

            await TrackUpdateAppliedAndFlushAsync(updateInfo.TargetFullRelease.Version.ToString(), null, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            _updateManager.ApplyUpdatesAndRestart(updateInfo.TargetFullRelease);

            // If we reach here, restart might have failed
            _logger.LogWarning("ApplyUpdatesAndRestart returned without exiting - this is unexpected");

            // Wait a bit for exit to happen without blocking the calling thread
            await Task.Delay(AppUpdateConstants.PostUpdateExitDelay, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ObjectDisposedException)
        {
            // The caller's token source was disposed (for example the update window
            // closed mid-apply). Fail without the exit fallback: re-applying a package
            // the restart path already applied would be spurious.
            throw;
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
    public async Task ApplyUpdatesAndExitAsync(UpdateInfo updateInfo, CancellationToken cancellationToken = default)
    {
        if (_updateManager == null)
        {
            throw new InvalidOperationException("UpdateManager not initialized");
        }

        ArgumentNullException.ThrowIfNull(updateInfo);

        try
        {
            CleanStrayAppDirectoryArtifacts();
            _logger.LogInformation("Applying update {Version} and exiting...", updateInfo.TargetFullRelease.Version);
            await TrackUpdateAppliedAndFlushAsync(updateInfo.TargetFullRelease.Version.ToString(), null, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            _updateManager.ApplyUpdatesAndExit(updateInfo.TargetFullRelease);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply updates and exit");
            throw;
        }
    }

    private async Task TrackUpdateAppliedAndFlushAsync(string targetVersion, string? channel = null, CancellationToken cancellationToken = default)
    {
        try
        {
            _telemetryService?.TrackEvent(TelemetryConstants.Events.AppUpdateApplied, new Dictionary<string, object?>
            {
                [TelemetryConstants.Properties.FromVersion] = CurrentAppVersion,
                [TelemetryConstants.Properties.ToVersion] = targetVersion,
                [TelemetryConstants.Properties.FullDisplayVersion] = AppConstants.FullDisplayVersion,
                [TelemetryConstants.Properties.BuildChannel] = AppConstants.BuildChannel,
                [TelemetryConstants.Properties.Channel] = channel ?? TelemetryChannel,
                [TelemetryConstants.Properties.Platform] = RuntimeInformation.OSDescription,
            });

            if (_telemetryService != null)
            {
                using var flushCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                flushCts.CancelAfter(TimeSpan.FromSeconds(TelemetryConstants.FlushTimeoutSeconds));
                await _telemetryService.FlushAsync(flushCts.Token).WaitAsync(flushCts.Token).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to flush telemetry before Velopack update apply");
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
        var targetPrNumber = SubscribedPrNumber;
        var targetBranch = SubscribedBranch;

        // check cache
        if (DateTime.UtcNow - _lastArtifactCheckTime < AppUpdateConstants.CacheDuration &&
            _cachedArtifactSubscribedPrNumber == targetPrNumber &&
            string.Equals(_cachedArtifactSubscribedBranch, targetBranch, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("Returning cached artifact update info (checked {TimeLess} ago)", (DateTime.UtcNow - _lastArtifactCheckTime).ToString(@"mm\:ss"));
            return _cachedArtifactUpdateInfo;
        }

        if (IsLocalDevelopmentBuild)
        {
            _logger.LogInformation(
                "Skipping artifact update check for local development build (Current={Current})",
                CurrentAppVersion);
            _cachedArtifactUpdateInfo = null;
            _cachedArtifactSubscribedPrNumber = targetPrNumber;
            _cachedArtifactSubscribedBranch = targetBranch;
            _lastArtifactCheckTime = DateTime.UtcNow;
            return null;
        }

        _logger.LogInformation("Checking for artifact updates from GitHub Actions CI builds");

        if (_gitHubAuthService == null || !_gitHubAuthService.IsAuthenticated)
        {
            _logger.LogDebug("GitHub authentication not available, skipping artifact updates check");
            return null;
        }

        try
        {
            ArtifactUpdateInfo? artifactUpdate = null;

            // priority:
            // 1. subscribed pr
            // 2. subscribed branch
            // 3. overall latest
            if (targetPrNumber.HasValue)
            {
                _logger.LogInformation("Checking for artifacts for subscribed PR #{PrNumber}", targetPrNumber.Value);
                var prs = await GetOpenPullRequestsAsync(cancellationToken);
                var subscribedPr = prs.FirstOrDefault(p => p.Number == targetPrNumber.Value);
                artifactUpdate = subscribedPr?.LatestArtifact;
            }
            else if (!string.IsNullOrEmpty(targetBranch))
            {
                _logger.LogInformation("Checking for artifacts for subscribed branch: {Branch}", targetBranch);
                artifactUpdate = await FindLatestArtifactAsync(targetBranch, cancellationToken);
            }
            else
            {
                _logger.LogInformation("Checking for overall latest artifact");
                artifactUpdate = await FindLatestArtifactAsync(null, cancellationToken);
            }

            // verify subscription did not change while awaiting
            if (SubscribedPrNumber != targetPrNumber ||
                !string.Equals(SubscribedBranch, targetBranch, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("Subscription changed during artifact check, discarding result");
                return null;
            }

            _latestArtifactUpdate = artifactUpdate;
            _cachedArtifactUpdateInfo = artifactUpdate;
            _cachedArtifactSubscribedPrNumber = targetPrNumber;
            _cachedArtifactSubscribedBranch = targetBranch;
            _lastArtifactCheckTime = DateTime.UtcNow;
            return artifactUpdate;
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

        // Check if GitHub authentication is available
        if (_gitHubAuthService == null || !_gitHubAuthService.IsAuthenticated)
        {
            _logger.LogDebug("GitHub authentication not available, skipping PR list fetch");
            return results;
        }

        try
        {
            using var token = await _gitHubAuthService.GetAccessTokenAsync(cancellationToken);
            if (token == null)
            {
                _logger.LogWarning("Failed to load GitHub access token");
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

            foreach (var pr in prsData.EnumerateArray())
            {
                var prNumber = pr.GetProperty("number").GetInt32();
                var title = pr.GetProperty("title").GetString() ?? GameClientConstants.UnknownVersion;
                var branchName = pr.TryGetProperty("head", out var head)
                    ? head.GetProperty("ref").GetString() ?? "unknown"
                    : "unknown";
                var author = pr.TryGetProperty("user", out var user)
                    ? user.GetProperty("login").GetString() ?? "unknown"
                    : "unknown";
                var state = pr.GetProperty("state").GetString() ?? "open";
                var updatedAt = pr.TryGetProperty("updated_at", out var updatedAtProp)
                    ? updatedAtProp.GetDateTimeOffset()
                    : (DateTimeOffset?)null;

                // Only fetch artifacts for the subscribed PR to prevent exhausting rate limits across all open PRs
                ArtifactUpdateInfo? latestArtifact = null;
                if (SubscribedPrNumber.HasValue && SubscribedPrNumber.Value == prNumber)
                {
                    latestArtifact = await FindLatestArtifactForPrAsync(client, prNumber, cancellationToken);
                }

                results.Add(new PullRequestInfo
                {
                    Number = prNumber,
                    Title = title,
                    BranchName = branchName,
                    Author = author,
                    State = state,
                    UpdatedAt = updatedAt,
                    LatestArtifact = latestArtifact,
                });
            }

            var sortedPrs = results
                .OrderByDescending(p => p.UpdatedAt ?? DateTimeOffset.MinValue)
                .ToList();
            results = sortedPrs;

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
                    var statusState = statusData.TryGetProperty("state", out var stProp) ? stProp.GetString() : null;

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

        if (_gitHubAuthService == null || !_gitHubAuthService.IsAuthenticated)
        {
            _logger.LogDebug("GitHub authentication not available, skipping branch list fetch");

            // Return at least main & development as defaults if we can't fetch real ones
            return ["main", "development"];
        }

        try
        {
            using var token = await _gitHubAuthService.GetAccessTokenAsync(cancellationToken);
            if (token == null)
            {
                return ["main", "development"];
            }

            using var client = CreateConfiguredHttpClientWithToken(token);
            var owner = AppConstants.GitHubRepositoryOwner;
            var repo = AppConstants.GitHubRepositoryName;
            var branchesUrl = string.Format(ApiConstants.GitHubApiBranchesFormat, owner, repo);

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

        var isDirectUrl = !string.IsNullOrWhiteSpace(artifactInfo.DownloadUrl);
        if (isDirectUrl && !NetworkSecurityHelper.IsSafeUrl(artifactInfo.DownloadUrl, out var failureReason))
        {
            throw new InvalidOperationException($"Invalid or unsafe download URL for artifact: {failureReason}");
        }

        if (!isDirectUrl && (_gitHubAuthService == null || !_gitHubAuthService.IsAuthenticated))
        {
            throw new InvalidOperationException("GitHub authentication required to download artifacts");
        }

        if (await TryInstallFromLocalCacheAsync(artifactInfo, progress, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        string? tempDir = null;
        try
        {
            var label = GetArtifactLabel(artifactInfo);
            var commitInfo = !string.IsNullOrEmpty(artifactInfo.GitHash) ? $" ({artifactInfo.GitHash})" : string.Empty;
            progress?.Report(new UpdateProgress { Status = $"Downloading artifact for {label}{commitInfo}...", PercentComplete = 0 });

            var (downloadUrl, headers) = await ResolveArtifactDownloadDetailsAsync(artifactInfo, label, cancellationToken).ConfigureAwait(false);

            tempDir = Path.Combine(AppDataPathHelper.GetDataRoot(), "Temp", $"genhub-art-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);

            var targetFileName = DetermineArtifactFileName(artifactInfo, downloadUrl);
            var targetFilePath = Path.Combine(tempDir, targetFileName);

            var downloadProgress = new Action<int>(percent =>
            {
                var totalPercent = (int)(percent * 0.3);
                progress?.Report(new UpdateProgress
                {
                    Status = $"Downloading artifact for {label}{commitInfo}... {percent}%",
                    PercentComplete = totalPercent,
                });
            });

            await _fileDownloader.DownloadFile(
                downloadUrl,
                targetFilePath,
                downloadProgress,
                headers,
                timeout: 300,
                cancelToken: cancellationToken);

            var isDirectExe = targetFilePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
            if (TryInstallDirectExecutable(targetFilePath, isDirectExe, progress))
            {
                return;
            }

            await InstallFromExtractedArtifactAsync(tempDir, targetFilePath, artifactInfo, label, progress, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to install artifact");
            progress?.Report(new UpdateProgress { Status = "Installation failed", HasError = true, ErrorMessage = ex.Message });
            throw;
        }
        finally
        {
            CleanupTempDirectory(tempDir);
        }
    }

    /// <inheritdoc/>
    public async Task InstallPrArtifactAsync(
        PullRequestInfo prInfo,
        IProgress<UpdateProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var artifact = prInfo.LatestArtifact;
        if (artifact == null && _gitHubAuthService is { } auth)
        {
            using var token = await auth.GetAccessTokenAsync(cancellationToken);
            if (token != null)
            {
                using var client = CreateConfiguredHttpClientWithToken(token);
                artifact = await FindLatestArtifactForPrAsync(client, prInfo.Number, cancellationToken);
            }
        }

        if (artifact == null)
        {
            throw new InvalidOperationException($"PR #{prInfo.Number} has no artifacts available");
        }

        await InstallArtifactAsync(artifact, progress, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task InstallDownloadedBuildAsync(
        string filePath,
        string? originalFileName = null,
        IProgress<UpdateProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        _logger.LogInformation("Installing downloaded build from '{Path}'", filePath);

        var targetPath = filePath;
        if (Directory.Exists(filePath))
        {
            var resolved = ResolveDirectoryInstallTarget(filePath, originalFileName);
            if (resolved == null)
            {
                return;
            }

            targetPath = resolved;
        }

        if (!File.Exists(targetPath))
        {
            throw new FileNotFoundException($"Target build file not found: '{targetPath}'");
        }

        var extension = Path.GetExtension(targetPath).ToLowerInvariant();
        switch (extension)
        {
            case ".exe":
                await InstallExeBuildAsync(targetPath, progress, cancellationToken).ConfigureAwait(false);
                break;
            case ".zip":
                await InstallZipBuildAsync(targetPath, progress, cancellationToken).ConfigureAwait(false);
                break;
            case ".nupkg":
                await InstallNupkgBuildAsync(targetPath, progress, cancellationToken).ConfigureAwait(false);
                break;
            default:
                throw new NotSupportedException($"Unsupported build file extension: '{extension}'");
        }
    }

    /// <inheritdoc/>
    public void ClearCache()
    {
        _lastUpdateCheckTime = DateTime.MinValue;
        _cachedUpdateInfo = null;
        _lastArtifactCheckTime = DateTime.MinValue;
        _cachedArtifactUpdateInfo = null;
        _cachedArtifactSubscribedPrNumber = null;
        _cachedArtifactSubscribedBranch = null;
        _lastPrListCheckTime = DateTime.MinValue;
        _cachedPrList = null;
        _lastBranchListCheckTime = DateTime.MinValue;
        _cachedBranchList = null;
        _hasUpdateFromGitHub = false;
        _latestVersionFromGitHub = null;
        IsPrMergedOrClosed = false;
        _logger.LogInformation("Update manager cache cleared");
    }

    /// <inheritdoc/>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("DeepSource", "CS-W1005", Justification = "Explicit application termination required after launching uninstaller.")]
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
                Environment.Exit(0); // skipcq: CS-W1005
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

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ArtifactUpdateInfo>> GetArtifactsForPullRequestAsync(int prNumber, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Fetching all artifacts for PR #{PrNumber}", prNumber);

        if (_gitHubAuthService == null || !_gitHubAuthService.IsAuthenticated)
        {
            _logger.LogWarning("GitHub authentication not available, cannot fetch artifacts");
            return [];
        }

        try
        {
            using var token = await _gitHubAuthService.GetAccessTokenAsync(cancellationToken);
            if (token == null) return [];

            using var client = CreateConfiguredHttpClientWithToken(token);

            var owner = AppConstants.GitHubRepositoryOwner;
            var repo = AppConstants.GitHubRepositoryName;
            var prUrl = string.Format(ApiConstants.GitHubApiPrDetailFormat, owner, repo, prNumber);

            var prResponse = await SendWithRetryAsync(client, prUrl, cancellationToken);
            if (prResponse == null || !prResponse.IsSuccessStatusCode) return [];

            var prJson = await prResponse.Content.ReadAsStringAsync(cancellationToken);
            using var prDoc = JsonDocument.Parse(prJson);
            var headRef = prDoc.RootElement.GetProperty("head").GetProperty("ref").GetString();

            if (string.IsNullOrEmpty(headRef)) return [];

            return await FindArtifactsAsync(client, headRef, prNumber, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get artifacts for PR #{PrNumber}", prNumber);
            return [];
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ArtifactUpdateInfo>> GetArtifactsForBranchAsync(string branchName, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Fetching all artifacts for branch '{Branch}'", branchName);

        if (_gitHubAuthService == null || !_gitHubAuthService.IsAuthenticated)
        {
            _logger.LogWarning("GitHub authentication not available, cannot fetch artifacts");
            return [];
        }

        try
        {
            using var token = await _gitHubAuthService.GetAccessTokenAsync(cancellationToken);
            if (token == null) return [];

            using var client = CreateConfiguredHttpClientWithToken(token);
            return await FindArtifactsAsync(client, branchName, null, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get artifacts for branch '{Branch}'", branchName);
            return [];
        }
    }

    /// <summary>
    /// Determines whether a GitHub Actions workflow run matches the specified branch or PR number criteria.
    /// </summary>
    /// <param name="run">The workflow run JSON element.</param>
    /// <param name="branchName">The optional target branch name.</param>
    /// <param name="prNumber">The optional pull request number.</param>
    /// <returns><c>true</c> if the workflow run matches; otherwise, <c>false</c>.</returns>
    internal static bool IsMatchingWorkflowRun(JsonElement run, string? branchName, int? prNumber)
    {
        var actualBranch = run.TryGetProperty("head_branch", out var b) ? b.GetString() : branchName ?? "unknown";
        var eventType = run.TryGetProperty("event", out var e) ? e.GetString() : "unknown";

        if (prNumber.HasValue)
        {
            return MatchesPullRequestCriteria(run, prNumber.Value, actualBranch, branchName);
        }

        if (!string.IsNullOrEmpty(branchName))
        {
            return MatchesBranchCriteria(actualBranch, branchName, eventType);
        }

        return true;
    }

    /// <summary>
    /// Checks whether a local content manifest matches the target build artifact update.
    /// Requires exact URL match, word-delimited PR match, target artifact file name match, or Git hash match.
    /// </summary>
    /// <param name="manifest">The content manifest to check.</param>
    /// <param name="artifactInfo">The artifact update info.</param>
    /// <returns><c>true</c> if the manifest matches the artifact; otherwise, <c>false</c>.</returns>
    internal static bool IsManifestMatchingArtifact(ContentManifest manifest, ArtifactUpdateInfo artifactInfo)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(artifactInfo);

        var cleanManifestVersion = manifest.Version?.TrimStart('v', 'V');
        var cleanArtifactVersion = artifactInfo.Version?.TrimStart('v', 'V');
        var matchesVersion = !string.IsNullOrWhiteSpace(cleanArtifactVersion) &&
            string.Equals(cleanManifestVersion, cleanArtifactVersion, StringComparison.OrdinalIgnoreCase);

        var resolvedFiles = ManifestVariantResolver.ResolveFiles(manifest);

        if (MatchesDirectDownloadUrl(resolvedFiles, artifactInfo.DownloadUrl))
        {
            return true;
        }

        if (MatchesPullRequestNumber(manifest.Name, artifactInfo.PullRequestNumber, matchesVersion))
        {
            return true;
        }

        if (MatchesArtifactFileName(manifest.Name, resolvedFiles, artifactInfo.ArtifactName, matchesVersion))
        {
            return true;
        }

        return MatchesGitHash(manifest, artifactInfo.GitHash, matchesVersion);
    }

    /// <summary>
    /// Cleans stray mutable build artifacts (.Build, .Release, .modbuilder_cache, etc.)
    /// from the application directory prior to applying an update.
    /// This prevents Windows file-lock (ERROR_ACCESS_DENIED) errors during Velopack package replacement.
    /// </summary>
    internal void CleanStrayAppDirectoryArtifacts()
    {
        try
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var sampleProjectsDir = Path.Combine(baseDir, ModBuilderConstants.SampleProjectsDirectoryName);
            if (Directory.Exists(sampleProjectsDir))
            {
                CleanSampleProjectArtifacts(sampleProjectsDir);
            }

            CleanStrayMsgpackFiles(baseDir);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during pre-update app directory cleanup");
        }
    }

    private static bool MatchesDirectDownloadUrl(IReadOnlyList<ManifestFile> resolvedFiles, string? downloadUrl)
    {
        return !string.IsNullOrWhiteSpace(downloadUrl) &&
            resolvedFiles.Any(f => string.Equals(f.DownloadUrl, downloadUrl, StringComparison.OrdinalIgnoreCase));
    }

    private static bool MatchesPullRequestNumber(string? manifestName, int? prNumber, bool matchesVersion)
    {
        if (!prNumber.HasValue || !matchesVersion)
        {
            return false;
        }

        var prPattern = $@"(?:^|\W)(?:PR\s*#?|#){prNumber.Value}(?!\d)";
        return Regex.IsMatch(manifestName ?? string.Empty, prPattern, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(500));
    }

    private static bool MatchesArtifactFileName(
        string? manifestName,
        IReadOnlyList<ManifestFile> resolvedFiles,
        string? artifactName,
        bool matchesVersion)
    {
        if (string.IsNullOrWhiteSpace(artifactName) || !matchesVersion)
        {
            return false;
        }

        var targetFileName = Path.GetFileName(artifactName);
        var matchesFileName = resolvedFiles.Any(f => string.Equals(Path.GetFileName(f.RelativePath), targetFileName, StringComparison.OrdinalIgnoreCase));
        var matchesNameInManifest = string.Equals(manifestName, artifactName, StringComparison.OrdinalIgnoreCase) ||
            (manifestName?.Contains(artifactName, StringComparison.OrdinalIgnoreCase) == true);

        return matchesFileName && matchesNameInManifest;
    }

    private static bool MatchesGitHash(ContentManifest manifest, string? gitHash, bool matchesVersion)
    {
        if (string.IsNullOrWhiteSpace(gitHash) || gitHash.Length < 7 || !matchesVersion)
        {
            return false;
        }

        return (manifest.Name?.Contains(gitHash, StringComparison.OrdinalIgnoreCase) == true) ||
            (manifest.Metadata?.Description?.Contains(gitHash, StringComparison.OrdinalIgnoreCase) == true);
    }

    private static bool MatchesPullRequestCriteria(JsonElement run, int prNumber, string? actualBranch, string? branchName)
    {
        if (run.TryGetProperty("pull_requests", out var prs) && prs.ValueKind == JsonValueKind.Array)
        {
            var prCount = 0;
            foreach (var pr in prs.EnumerateArray())
            {
                prCount++;
                if (pr.TryGetProperty("number", out var num) && num.GetInt32() == prNumber)
                {
                    return true;
                }
            }

            if (prCount > 0)
            {
                return false;
            }
        }

        return string.IsNullOrEmpty(branchName) || string.Equals(actualBranch, branchName, StringComparison.Ordinal);
    }

    private static bool MatchesBranchCriteria(string? actualBranch, string branchName, string? eventType)
    {
        if (!string.Equals(actualBranch, branchName, StringComparison.Ordinal))
        {
            return false;
        }

        return string.Equals(eventType, "push", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(eventType, "workflow_dispatch", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(eventType, "pull_request", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Extracts version from artifact name.
    /// Expected format: genhub-velopack-{platform}-{version}.
    /// </summary>
    private static string? ExtractVersionFromArtifactName(string artifactName)
    {
        var prefixes = new[]
        {
            AppUpdateConstants.ArtifactPrefixWindows,
            AppUpdateConstants.ArtifactPrefixLinux,
            AppUpdateConstants.ArtifactPrefixMacOS,
        };

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

    private static string? GetCurrentPlatformFilter()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return AppUpdateConstants.PlatformWindows;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return AppUpdateConstants.PlatformLinux;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return AppUpdateConstants.PlatformMacOS;
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

    private static bool IsStrayArtifactDirectory(string dirName) =>
        dirName.Equals(ModBuilderConstants.DefaultBuildDir, StringComparison.OrdinalIgnoreCase) ||
        dirName.Equals(ModBuilderConstants.DefaultReleaseDir, StringComparison.OrdinalIgnoreCase) ||
        dirName.StartsWith(ModBuilderConstants.StagingDirectoryPrefix, StringComparison.OrdinalIgnoreCase) ||
        dirName.Equals(ModBuilderConstants.CacheDirectoryName, StringComparison.OrdinalIgnoreCase);

    private static bool IsProjectDirectory(string? parentDir)
    {
        if (parentDir == null)
        {
            return false;
        }

        return Directory.GetFiles(parentDir, ModBuilderConstants.ProjectFilePattern).Length > 0 ||
               Directory.Exists(Path.Combine(parentDir, ModBuilderConstants.LowercaseConfigDir)) ||
               Directory.Exists(Path.Combine(parentDir, ModBuilderConstants.ConfigDir));
    }

    private static bool IsMatchingBranchRun(JsonElement run, string? branch, out string actualBranch, out string eventType)
    {
        eventType = run.TryGetProperty("event", out var e) ? e.GetString() ?? "unknown" : "unknown";
        actualBranch = run.TryGetProperty("head_branch", out var b) && b.GetString() is { } hb
            ? hb
            : branch ?? "unknown";

        if (!string.IsNullOrEmpty(branch) && !string.Equals(actualBranch, branch, StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(branch) &&
            !string.Equals(eventType, "push", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(eventType, "workflow_dispatch", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static DateTime ParseRunCreatedAt(JsonElement run)
    {
        if (run.TryGetProperty("created_at", out var catProp))
        {
            try
            {
                return catProp.GetDateTime();
            }
            catch (FormatException)
            {
                return DateTime.MinValue;
            }
        }

        return DateTime.MinValue;
    }

    private static void SweepStaleBuildDirectories(ILogger? logger)
    {
        try
        {
            var tempRoot = Path.Combine(AppDataPathHelper.GetDataRoot(), "Temp");
            var prefixes = new[] { "genhub-art-", "genhub-build-", "genhub-local-build-", "genhub-build-install-", "genhub-installer-" };
            if (Directory.Exists(tempRoot))
            {
                foreach (var dir in Directory.EnumerateDirectories(tempRoot, "genhub-*"))
                {
                    var dirName = Path.GetFileName(dir);
                    if (prefixes.Any(prefix => dirName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                    {
                        TryDeleteStaleDirectory(dir, logger);
                    }
                }
            }

            var legacyInstallerRoot = Path.Combine(Path.GetTempPath(), "GenHub-Installer");
            if (Directory.Exists(legacyInstallerRoot))
            {
                foreach (var dir in Directory.EnumerateDirectories(legacyInstallerRoot))
                {
                    TryDeleteStaleDirectory(dir, logger);
                }
            }
        }
        catch (Exception ex)
        {
            logger?.LogDebug(ex, "Failed to sweep stale build directories");
        }
    }

    private static void TryDeleteStaleDirectory(string dir, ILogger? logger)
    {
        try
        {
            var info = new DirectoryInfo(dir);
            var now = DateTime.UtcNow;
            if (now - info.CreationTimeUtc > TimeSpan.FromHours(1) &&
                now - info.LastWriteTimeUtc > TimeSpan.FromHours(1))
            {
                Directory.Delete(dir, recursive: true);
                logger?.LogDebug("Swept stale build temp directory: {Dir}", dir);
            }
        }
        catch (Exception ex)
        {
            logger?.LogTrace(ex, "Failed to delete stale temp directory {Dir}", dir);
        }
    }

    private static string DetermineArtifactFileName(ArtifactUpdateInfo artifactInfo, string? downloadUrl)
    {
        var candidateName = !string.IsNullOrWhiteSpace(artifactInfo.ArtifactName)
            ? artifactInfo.ArtifactName
            : downloadUrl;

        if (!string.IsNullOrWhiteSpace(candidateName))
        {
            var uri = Uri.TryCreate(candidateName, UriKind.Absolute, out var parsed) ? parsed : null;
            var fileName = uri != null ? Path.GetFileName(uri.AbsolutePath) : Path.GetFileName(candidateName);
            var ext = Path.GetExtension(fileName);
            if (ext.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".nupkg", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".zip", StringComparison.OrdinalIgnoreCase))
            {
                return fileName;
            }
        }

        return "artifact.zip";
    }

    private static bool IsWindowsExecutable(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return false;
        }

        using var fs = File.OpenRead(filePath);
        var magic = new byte[2];
        return fs.Read(magic, 0, 2) == 2 && magic[0] == 0x4D && magic[1] == 0x5A;
    }

    private static string GetArtifactLabel(ArtifactUpdateInfo artifactInfo)
    {
        if (artifactInfo.PullRequestNumber.HasValue)
        {
            return $"PR #{artifactInfo.PullRequestNumber.Value}";
        }

        return !string.IsNullOrWhiteSpace(artifactInfo.ArtifactName)
            ? artifactInfo.ArtifactName
            : $"Artifact v{artifactInfo.Version}";
    }

    private static bool TryParseSuccessfulRun(
        JsonElement run,
        string expectedBranch,
        out long runId,
        out string runUrl,
        out string shortHash,
        out DateTime createdAt)
    {
        runId = 0;
        runUrl = string.Empty;
        shortHash = string.Empty;
        createdAt = DateTime.MinValue;

        var id = run.TryGetProperty("id", out var idProp) && idProp.TryGetInt64(out var rId) ? rId : 0;
        var runBranch = run.TryGetProperty("head_branch", out var hb) ? hb.GetString() : string.Empty;
        if (!string.Equals(runBranch, expectedBranch, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var status = run.TryGetProperty("status", out var statusProp) ? statusProp.GetString() : string.Empty;
        var conclusion = run.TryGetProperty("conclusion", out var conclusionProp) ? conclusionProp.GetString() : string.Empty;
        if (!string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(conclusion, "success", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        runId = id;
        runUrl = run.TryGetProperty("html_url", out var huProp) ? huProp.GetString() ?? string.Empty : string.Empty;
        if (run.TryGetProperty("created_at", out var catProp) && catProp.ValueKind == JsonValueKind.String && catProp.TryGetDateTime(out var dt))
        {
            createdAt = dt;
        }

        var headSha = run.TryGetProperty("head_sha", out var hsProp) ? hsProp.GetString() ?? string.Empty : string.Empty;
        shortHash = headSha.Length >= AppConstants.GitShortHashLength ? headSha[..AppConstants.GitShortHashLength] : headSha;
        return true;
    }

    private static string? ResolveDirectoryInstallTarget(string dirPath, string? originalFileName = null)
    {
        if (!string.IsNullOrWhiteSpace(originalFileName))
        {
            var match = Directory.GetFiles(dirPath, originalFileName, SearchOption.AllDirectories).FirstOrDefault();
            if (match != null)
            {
                return match;
            }
        }

        var nupkgs = Directory.GetFiles(dirPath, "*.nupkg", SearchOption.AllDirectories);
        if (nupkgs.Length > 0)
        {
            return nupkgs[0];
        }

        var zips = Directory.GetFiles(dirPath, "*.zip", SearchOption.AllDirectories);
        if (zips.Length > 0)
        {
            return zips[0];
        }

        var exes = Directory.GetFiles(dirPath, "*.exe", SearchOption.AllDirectories)
            .Where(e => !Path.GetFileName(e).StartsWith("createdump", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (exes.Length > 0)
        {
            var targetExe = exes.FirstOrDefault(e => Path.GetFileName(e).Contains("Setup", StringComparison.OrdinalIgnoreCase)) ?? exes[0];
            return targetExe;
        }

        throw new FileNotFoundException($"No valid installer, nupkg, or zip archive found in '{dirPath}'");
    }

    private void LaunchInstallerProcess(string exePath, IProgress<UpdateProgress>? progress)
    {
        _logger.LogInformation("Launching installer executable '{Exe}'", exePath);
        progress?.Report(new UpdateProgress { Status = "Launching installer...", PercentComplete = 100 });

        var stagedExe = exePath;
        var stagedDir = Path.GetDirectoryName(exePath) ?? AppDataPathHelper.GetDataRoot();
        try
        {
            var stageDir = Path.Combine(AppDataPathHelper.GetDataRoot(), "Temp", $"genhub-installer-{Guid.NewGuid():N}");
            Directory.CreateDirectory(stageDir);
            var targetStagedExe = Path.Combine(stageDir, Path.GetFileName(exePath));
            File.Copy(exePath, targetStagedExe, overwrite: true);
            stagedExe = targetStagedExe;
            stagedDir = stageDir;
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Failed to stage installer executable to temp dir, executing from original path: {Path}", exePath);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Failed to stage installer executable to temp dir, executing from original path: {Path}", exePath);
        }

        var customPath = _installationLocationTracker?.GetRegisteredCustomInstallPath();
        var arguments = string.Empty;
        if (!string.IsNullOrWhiteSpace(customPath))
        {
            arguments = $"--installto \"{customPath}\"";
        }

        ProcessStartInfo startInfo;
        if (OperatingSystem.IsWindows())
        {
            var comSpec = Environment.GetEnvironmentVariable("ComSpec");
            if (string.IsNullOrWhiteSpace(comSpec))
            {
                comSpec = Path.Combine(Environment.SystemDirectory, "cmd.exe");
            }

            var cmdArgs = $"/c timeout /t 1 /nobreak >nul && start \"\" \"{stagedExe}\" {arguments}".Trim();
            startInfo = new ProcessStartInfo(comSpec, cmdArgs)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = stagedDir,
            };
        }
        else
        {
            startInfo = new ProcessStartInfo(stagedExe, arguments)
            {
                UseShellExecute = true,
                WorkingDirectory = stagedDir,
            };
        }

        using var proc = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start installer '{stagedExe}'");
        _logger.LogInformation("Installer launcher process started with PID {ProcessId}", proc.Id);
        Thread.Sleep(300);
        Environment.Exit(0); // skipcq: CS-W1005, CS-R1081
    }

    private async Task InstallExeBuildAsync(
        string targetPath,
        IProgress<UpdateProgress>? progress,
        CancellationToken cancellationToken)
    {
        var tempDir = Path.Combine(AppDataPathHelper.GetDataRoot(), "Temp", $"genhub-build-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            var extractedNupkg = Path.Combine(tempDir, $"{Path.GetFileNameWithoutExtension(targetPath)}.nupkg");

            if (VelopackBundleExtractor.TryExtractBundle(targetPath, extractedNupkg, out var bundleBytes))
            {
                _logger.LogInformation("Successfully extracted {Bytes:N0}-byte embedded Velopack nupkg from '{Exe}'", bundleBytes, targetPath);
                await InstallLocalNupkgAsync(extractedNupkg, tempDir, Path.GetFileName(targetPath), progress, cancellationToken).ConfigureAwait(false);
                return;
            }

            _logger.LogInformation("'{Exe}' is not a Velopack bundle; launching standalone installer process", targetPath);
            LaunchInstallerProcess(targetPath, progress);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    private async Task InstallZipBuildAsync(
        string targetPath,
        IProgress<UpdateProgress>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(new UpdateProgress { Status = "Extracting build archive...", PercentComplete = 20 });
        var tempDir = Path.Combine(AppDataPathHelper.GetDataRoot(), "Temp", $"genhub-build-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            ZipArchiveGuard.ExtractToDirectory(targetPath, tempDir, cancellationToken);

            var nupkgFiles = Directory.GetFiles(tempDir, "*.nupkg", SearchOption.AllDirectories);
            if (nupkgFiles.Length > 0)
            {
                await InstallLocalNupkgAsync(nupkgFiles[0], tempDir, Path.GetFileName(targetPath), progress, cancellationToken).ConfigureAwait(false);
                return;
            }

            await InstallZipExeCandidateAsync(tempDir, progress, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    private async Task InstallZipExeCandidateAsync(
        string tempDir,
        IProgress<UpdateProgress>? progress,
        CancellationToken cancellationToken)
    {
        var exeFiles = Directory.GetFiles(tempDir, "*.exe", SearchOption.AllDirectories)
            .Where(e => !Path.GetFileName(e).StartsWith("createdump", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (exeFiles.Length == 0)
        {
            throw new FileNotFoundException("No .nupkg or installer executable found inside zip archive");
        }

        var targetExe = exeFiles.FirstOrDefault(e => Path.GetFileName(e).Contains("Setup", StringComparison.OrdinalIgnoreCase)) ?? exeFiles[0];
        var extractedNupkg = Path.Combine(tempDir, $"{Path.GetFileNameWithoutExtension(targetExe)}.nupkg");

        if (VelopackBundleExtractor.TryExtractBundle(targetExe, extractedNupkg, out var bundleBytes))
        {
            _logger.LogInformation("Successfully extracted {Bytes:N0}-byte embedded Velopack nupkg from '{Exe}' inside zip", bundleBytes, targetExe);
            await InstallLocalNupkgAsync(extractedNupkg, tempDir, Path.GetFileName(targetExe), progress, cancellationToken).ConfigureAwait(false);
            return;
        }

        LaunchInstallerProcess(targetExe, progress);
    }

    private async Task InstallNupkgBuildAsync(
        string targetPath,
        IProgress<UpdateProgress>? progress,
        CancellationToken cancellationToken)
    {
        var tempDir = Path.Combine(AppDataPathHelper.GetDataRoot(), "Temp", $"genhub-build-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            await InstallLocalNupkgAsync(targetPath, tempDir, Path.GetFileName(targetPath), progress, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    private void SafeDeleteDirectory(string dir)
    {
        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
        {
            try
            {
                Directory.Delete(dir, true);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to clean up temp build directory {Dir}", dir);
            }
        }
    }

    private async Task<(string DownloadUrl, Dictionary<string, string> Headers)> ResolveArtifactDownloadDetailsAsync(
        ArtifactUpdateInfo artifactInfo,
        string label,
        CancellationToken cancellationToken)
    {
        var isDirectUrl = !string.IsNullOrWhiteSpace(artifactInfo.DownloadUrl);
        string downloadUrl = isDirectUrl
            ? artifactInfo.DownloadUrl!
            : string.Format(ApiConstants.GitHubApiArtifactDownloadFormat, AppConstants.GitHubRepositoryOwner, AppConstants.GitHubRepositoryName, artifactInfo.ArtifactId);

        var headers = new Dictionary<string, string>
        {
            { "User-Agent", AppConstants.AppName },
        };

        _logger.LogInformation("Downloading {Label} from {Url}", label, downloadUrl);

        var isGitHubApiUrl = Uri.TryCreate(downloadUrl, UriKind.Absolute, out var parsedUri) &&
                             parsedUri.Scheme == Uri.UriSchemeHttps &&
                             string.Equals(parsedUri.Host, "api.github.com", StringComparison.OrdinalIgnoreCase);

        if ((!isDirectUrl || isGitHubApiUrl) && _gitHubAuthService != null && _gitHubAuthService.IsAuthenticated)
        {
            using var token = await _gitHubAuthService.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
            if (token != null)
            {
                headers["Accept"] = ApiConstants.GitHubApiHeaderAccept;
                UseSecureStringAsPlainText(token, plainText =>
                {
                    headers["Authorization"] = $"Bearer {plainText}";
                });
            }
        }
        else if (!isDirectUrl && (_gitHubAuthService == null || !_gitHubAuthService.IsAuthenticated))
        {
            throw new InvalidOperationException("GitHub authentication required to download artifacts");
        }

        return (downloadUrl, headers);
    }

    private bool TryInstallDirectExecutable(string targetFilePath, bool isDirectExe, IProgress<UpdateProgress>? progress)
    {
        var isExe = IsWindowsExecutable(targetFilePath);
        if (!isDirectExe && !isExe)
        {
            return false;
        }

        if (!isExe)
        {
            throw new InvalidOperationException("Downloaded artifact was expected to be an executable installer, but the file does not have a valid Windows executable (PE/MZ) header.");
        }

        var exePath = targetFilePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? targetFilePath
            : Path.ChangeExtension(targetFilePath, ".exe");

        if (!string.Equals(targetFilePath, exePath, StringComparison.OrdinalIgnoreCase) && !File.Exists(exePath))
        {
            File.Move(targetFilePath, exePath);
        }

        LaunchInstallerProcess(exePath, progress);
        return true;
    }

    private async Task<bool> TryInstallFromLocalCacheAsync(
        ArtifactUpdateInfo artifactInfo,
        IProgress<UpdateProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (_contentManifestPool == null || _contentStorageService == null)
        {
            return false;
        }

        var localPath = await TryFindLocalArtifactPathAsync(artifactInfo, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrEmpty(localPath))
        {
            return false;
        }

        _logger.LogInformation("Found locally cached build artifact for {Version}, installing from local storage without redownloading", artifactInfo.Version);
        try
        {
            await InstallDownloadedBuildAsync(localPath, artifactInfo.ArtifactName, progress, cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally
        {
            try
            {
                if (Directory.Exists(localPath))
                {
                    Directory.Delete(localPath, recursive: true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogTrace(ex, "Failed to clean up local build path {Path}", localPath);
            }
        }
    }

    private string GetArtifactChannel(ArtifactUpdateInfo artifactInfo)
    {
        if (artifactInfo.PullRequestNumber.HasValue)
        {
            return $"{TelemetryConstants.PullRequestChannelPrefix}{artifactInfo.PullRequestNumber.Value}";
        }

        return !string.IsNullOrEmpty(artifactInfo.ArtifactName)
            ? artifactInfo.ArtifactName
            : TelemetryChannel;
    }

    private async Task InstallFromExtractedArtifactAsync(
        string tempDir,
        string targetFilePath,
        ArtifactUpdateInfo artifactInfo,
        string label,
        IProgress<UpdateProgress>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(new UpdateProgress { Status = "Extracting artifact...", PercentComplete = 30 });
        ZipArchiveGuard.ExtractToDirectory(targetFilePath, tempDir, cancellationToken);

        var nupkgFiles = Directory.GetFiles(tempDir, "*.nupkg", SearchOption.AllDirectories);
        if (nupkgFiles.Length == 0)
        {
            var exeFiles = Directory.GetFiles(tempDir, "*.exe", SearchOption.AllDirectories)
                .Where(e => !Path.GetFileName(e).StartsWith("createdump", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (exeFiles.Length > 0)
            {
                var targetExe = exeFiles.FirstOrDefault(e => Path.GetFileName(e).Contains("Setup", StringComparison.OrdinalIgnoreCase)) ?? exeFiles[0];
                LaunchInstallerProcess(targetExe, progress);
                return;
            }

            throw new FileNotFoundException("No .nupkg or installer file found in artifact");
        }

        var nupkgFile = nupkgFiles[0];
        _logger.LogInformation("Found nupkg: {File}", Path.GetFileName(nupkgFile));

        var (releasesPath, nupkgFileName, fileVersion, sha1, sha256, fileLength) =
            await CreateLocalReleasesJsonAsync(tempDir, nupkgFile, artifactInfo.Version, cancellationToken).ConfigureAwait(false);

        progress?.Report(new UpdateProgress { Status = "Starting local server...", PercentComplete = 50 });

        var port = FindAvailablePort();
        using var server = new SimpleHttpServer(nupkgFile, releasesPath, port, _logger);
        server.Start();

        var artifactChannel = GetArtifactChannel(artifactInfo);
        var updateRequest = new LocalVelopackUpdateRequest(
            server,
            port,
            fileVersion,
            nupkgFileName,
            sha1,
            sha256,
            fileLength,
            label,
            artifactChannel);

        await ExecuteLocalVelopackUpdateAsync(
            updateRequest,
            progress,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<(string ReleasesPath, string NupkgFileName, string FileVersion, string Sha1, string Sha256, long FileLength)> CreateLocalReleasesJsonAsync(
        string tempDir,
        string nupkgFile,
        string fallbackVersion,
        CancellationToken cancellationToken)
    {
        var releasesPath = Path.Combine(tempDir, "releases.win.json");
        var nupkgFileName = Path.GetFileName(nupkgFile);
        var fileInfo = new FileInfo(nupkgFile);
        var sha1 = CalculateSHA1(nupkgFile);
        var sha256 = CalculateSHA256(nupkgFile);

        var versionMatch = NupkgVersionRegex().Match(nupkgFileName);
        var fileVersion = versionMatch.Success ? versionMatch.Groups[1].Value : fallbackVersion;

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
        await File.WriteAllTextAsync(releasesPath, jsonContent, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Created releases.win.json with version {Version}", fileVersion);

        return (releasesPath, nupkgFileName, fileVersion, sha1, sha256, fileInfo.Length);
    }

    private sealed record LocalVelopackUpdateRequest(
        SimpleHttpServer Server,
        int Port,
        string FileVersion,
        string NupkgFileName,
        string Sha1,
        string Sha256,
        long FileSize,
        string Label,
        string ArtifactChannel);

    private async Task ExecuteLocalVelopackUpdateAsync(
        LocalVelopackUpdateRequest request,
        IProgress<UpdateProgress>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(new UpdateProgress { Status = "Preparing update...", PercentComplete = 60 });
        progress?.Report(new UpdateProgress { Status = "Downloading update...", PercentComplete = 70 });

        var source = new SimpleWebSource($"http://localhost:{request.Port}/{request.Server.SecretToken}/", _fileDownloader);
        var localUpdateManager = new UpdateManager(source);

        var asset = new VelopackAsset
        {
            PackageId = AppConstants.AppName,
            Version = SemanticVersion.Parse(request.FileVersion),
            Type = VelopackAssetType.Full,
            FileName = request.NupkgFileName,
            SHA1 = request.Sha1,
            SHA256 = request.Sha256,
            Size = request.FileSize,
        };

        var updateInfo = new UpdateInfo(asset, true);

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
            cancellationToken).ConfigureAwait(false);

        _telemetryService?.TrackEvent(TelemetryConstants.Events.AppUpdateDownloaded, new Dictionary<string, object?>
        {
            [TelemetryConstants.Properties.FromVersion] = CurrentAppVersion,
            [TelemetryConstants.Properties.ToVersion] = request.FileVersion,
            [TelemetryConstants.Properties.FullDisplayVersion] = AppConstants.FullDisplayVersion,
            [TelemetryConstants.Properties.BuildChannel] = AppConstants.BuildChannel,
            [TelemetryConstants.Properties.Channel] = request.ArtifactChannel,
            [TelemetryConstants.Properties.Platform] = RuntimeInformation.OSDescription,
        });

        progress?.Report(new UpdateProgress { Status = "Installing update...", PercentComplete = 90 });

        CleanStrayAppDirectoryArtifacts();
        _logger.LogInformation("Applying {Label} update and restarting", request.Label);

        await TrackUpdateAppliedAndFlushAsync(request.FileVersion, request.ArtifactChannel, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        localUpdateManager.ApplyUpdatesAndRestart(updateInfo.TargetFullRelease);

        _logger.LogWarning("ApplyUpdatesAndRestart returned without exiting - waiting for exit...");
        await Task.Delay(AppUpdateConstants.PostUpdateExitDelay, cancellationToken).ConfigureAwait(false);

        _logger.LogError("Application did not exit after ApplyUpdatesAndRestart. Update may have failed.");
        throw new InvalidOperationException("Application did not exit after applying update");
    }

    private void CleanupTempDirectory(string? tempDir)
    {
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

    private void CleanSampleProjectArtifacts(string sampleProjectsDir)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            MatchCasing = MatchCasing.CaseInsensitive,
            AttributesToSkip = FileAttributes.None,
        };

        try
        {
            var strayDirs = Directory.EnumerateDirectories(sampleProjectsDir, "*", options)
                         .Where(d => IsStrayArtifactDirectory(Path.GetFileName(d)))
                         .ToList();

            foreach (var dir in strayDirs)
            {
                TryDeleteStrayDirectory(dir);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Error enumerating sample projects directory: {Dir}", sampleProjectsDir);
        }
    }

    private void TryDeleteStrayDirectory(string dir)
    {
        try
        {
            if (!Directory.Exists(dir))
            {
                return;
            }

            var parentDir = Path.GetDirectoryName(dir);
            if (IsProjectDirectory(parentDir))
            {
                Directory.Delete(dir, recursive: true);
                _logger.LogInformation("Pre-update cleanup removed stray directory: {Dir}", dir);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Pre-update cleanup could not delete directory: {Dir}", dir);
        }
    }

    private void CleanStrayMsgpackFiles(string baseDir)
    {
        foreach (var file in Directory.GetFiles(baseDir, "*.msgpack", SearchOption.TopDirectoryOnly))
        {
            try
            {
                File.Delete(file);
                _logger.LogInformation("Pre-update cleanup removed stray file: {File}", file);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Pre-update cleanup could not delete file: {File}", file);
            }
        }
    }

    /// <summary>
    /// Gets or creates an HttpClient instance with proper configuration.
    /// </summary>
    /// <returns>An HttpClient instance.</returns>
    [SuppressMessage("Major Code Smell", "S2325", Justification = "Preserve method ordering and instance scope")]
    private string GetReleasesFileName()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return "releases.linux.json";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return "releases.osx.json";
        }

        return "releases.win.json";
    }

    private string? TryExtractNuspecVersion(string nupkgFile)
    {
        try
        {
            using var archive = ZipFile.OpenRead(nupkgFile);
            var nuspecEntry = archive.Entries.FirstOrDefault(e => e.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
            if (nuspecEntry != null)
            {
                using var stream = nuspecEntry.Open();
                var doc = XDocument.Load(stream);
                var versionElement = doc.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals("version", StringComparison.OrdinalIgnoreCase));
                if (versionElement != null && !string.IsNullOrWhiteSpace(versionElement.Value))
                {
                    return versionElement.Value.Trim();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read .nuspec from nupkg '{File}'", nupkgFile);
        }

        return null;
    }

    private async Task InstallLocalNupkgAsync(
        string nupkgFile,
        string tempDir,
        string releaseTitle,
        IProgress<UpdateProgress>? progress,
        CancellationToken cancellationToken)
    {
        var releasesFileName = GetReleasesFileName();
        var releasesPath = Path.Combine(tempDir, releasesFileName);
        var nupkgFileName = Path.GetFileName(nupkgFile);
        var fileInfo = new FileInfo(nupkgFile);
        var sha1 = CalculateSHA1(nupkgFile);
        var sha256 = CalculateSHA256(nupkgFile);

        var fileVersion = TryExtractNuspecVersion(nupkgFile);
        if (string.IsNullOrWhiteSpace(fileVersion))
        {
            var versionMatch = NupkgVersionRegex().Match(nupkgFileName);
            if (versionMatch.Success)
            {
                fileVersion = versionMatch.Groups[1].Value;
            }
        }

        if (string.IsNullOrWhiteSpace(fileVersion))
        {
            throw new InvalidOperationException($"Unable to determine package version from nupkg '{nupkgFileName}'.");
        }

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
        _logger.LogInformation("Created local {FileName} for build '{Title}' with version {Version}", releasesFileName, releaseTitle, fileVersion);

        progress?.Report(new UpdateProgress { Status = "Starting local installation server...", PercentComplete = 50 });

        var port = FindAvailablePort();
        using var server = new SimpleHttpServer(nupkgFile, releasesPath, port, _logger);
        server.Start();

        progress?.Report(new UpdateProgress { Status = "Preparing update...", PercentComplete = 60 });

        var source = new SimpleWebSource($"http://localhost:{port}/{server.SecretToken}/", _fileDownloader);
        var localUpdateManager = new UpdateManager(source);

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

        var updateInfo = new UpdateInfo(asset, true);

        await localUpdateManager.DownloadUpdatesAsync(
            updateInfo,
            p =>
            {
                progress?.Report(new UpdateProgress
                {
                    Status = $"Preparing update: {p}%",
                    PercentComplete = 60 + (int)(p * 0.3),
                });
            },
            cancellationToken);

        progress?.Report(new UpdateProgress { Status = "Applying update and restarting...", PercentComplete = 100 });
        _logger.LogInformation("Applying update from local build package and restarting");
        localUpdateManager.ApplyUpdatesAndRestart(updateInfo);
    }

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

    private async Task<string?> FetchGitHubReleasesJsonAsync(string owner, string repo, CancellationToken cancellationToken)
    {
        var apiUrl = string.Format(ApiConstants.GitHubApiReleasesFormat, owner, repo);
        using var token = _gitHubAuthService != null
            ? await _gitHubAuthService.GetAccessTokenAsync(cancellationToken)
            : null;
        HttpClient client;
        if (token != null)
        {
            _logger.LogDebug("Using GitHub authentication for update check to increase rate limits");
            client = CreateConfiguredHttpClientWithToken(token);
        }
        else
        {
            _logger.LogDebug("No GitHub authentication available for update check, using anonymous request");
            client = CreateConfiguredHttpClient();
        }

        using (client)
        {
            var response = await SendWithRetryAsync(client, apiUrl, cancellationToken);
            if (response == null || !response.IsSuccessStatusCode)
            {
                _logger.LogError("GitHub API request failed after retries");
                return null;
            }

            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
    }

    private bool TryParseReleases(string json, out JsonElement releases)
    {
        try
        {
            releases = JsonSerializer.Deserialize<JsonElement>(json);
            if (releases.ValueKind.Equals(JsonValueKind.Array) && releases.GetArrayLength() > 0)
            {
                return true;
            }

            _logger.LogWarning("No releases found on GitHub");
            return false;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse GitHub API response as JSON");
            _logger.LogDebug("Raw JSON response: {Json}", json);
            releases = default;
            return false;
        }
    }

    private (SemanticVersion? Version, JsonElement? Release) ParseLatestRelease(JsonElement releases)
    {
        SemanticVersion? latestVersion = null;
        JsonElement? latestRelease = null;

        foreach (var release in releases.EnumerateArray())
        {
            var tagName = release.GetProperty("tag_name").GetString();
            if (string.IsNullOrEmpty(tagName))
            {
                continue;
            }

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

        return (latestVersion, latestRelease);
    }

    private async Task<UpdateInfo?> CheckViaUpdateManagerAsync()
    {
        if (_updateManager == null)
        {
            return null;
        }

        try
        {
            _logger.LogDebug("Calling UpdateManager.CheckForUpdatesAsync()");
            var updateInfo = await _updateManager.CheckForUpdatesAsync();
            if (updateInfo != null)
            {
                _logger.LogDebug("UpdateInfo version: {Version}", updateInfo.TargetFullRelease.Version);
                _cachedUpdateInfo = updateInfo;
                _lastUpdateCheckTime = DateTime.UtcNow;
            }

            return updateInfo;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "UpdateManager.CheckForUpdatesAsync failed");
            _logger.LogWarning("Update is available from GitHub, but cannot be downloaded/installed due to UpdateManager exception");
            return null;
        }
    }

    private async Task<UpdateInfo?> TryGetInstalledUpdateInfoAsync()
    {
        if (_updateManager == null)
        {
            _logger.LogWarning("UpdateManager is null - was not initialized successfully");
            return null;
        }

        var updateInfo = await CheckViaUpdateManagerAsync();
        if (updateInfo != null)
        {
            _logger.LogInformation("UpdateManager also confirmed update is available and can be installed");
            return updateInfo;
        }

        _logger.LogWarning("UpdateManager returned null - no update found via Velopack (but GitHub says there is one)");
        return null;
    }

    private ArtifactUpdateInfo? FindPlatformArtifactInRun(
        JsonElement artifacts,
        string platformFilter,
        int? prNumber,
        long runId,
        string runUrl,
        string shortHash,
        DateTime createdAt)
    {
        foreach (var artifact in artifacts.EnumerateArray())
        {
            var artifactName = artifact.GetProperty("name").GetString() ?? string.Empty;
            if (!artifactName.Contains("velopack", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!artifactName.Contains(platformFilter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            _logger.LogInformation("Found {Platform} Velopack artifact: {Name}", platformFilter, artifactName);

            var artifactId = artifact.GetProperty("id").GetInt64();
            var fallbackVersion = prNumber.HasValue ? $"PR{prNumber.Value}" : "0.0.0";
            var version = ExtractVersionFromArtifactName(artifactName) ?? fallbackVersion;

            var artifactInfo = new ArtifactUpdateInfo(
                Version: version,
                GitHash: shortHash,
                PullRequestNumber: prNumber,
                WorkflowRunId: runId,
                WorkflowRunUrl: runUrl,
                ArtifactId: artifactId,
                ArtifactName: artifactName,
                CreatedAt: createdAt,
                DownloadUrl: artifact.GetProperty("archive_download_url").GetString(),
                Size: artifact.GetProperty("size_in_bytes").GetInt64());

            _logger.LogInformation("Selected {Platform} artifact: {Name} (ID: {Id})", platformFilter, artifactName, artifactId);
            return artifactInfo;
        }

        return null;
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

            var headBranch = await GetPrHeadBranchAsync(client, owner, repo, prNumber, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrEmpty(headBranch))
            {
                return null;
            }

            var platformFilter = GetCurrentPlatformFilter();
            if (platformFilter == null)
            {
                _logger.LogWarning("Unsupported platform for artifact updates");
                return null;
            }

            var runs = await FetchWorkflowRunsForBranchAsync(client, owner, repo, headBranch, prNumber, cancellationToken).ConfigureAwait(false);
            if (runs == null)
            {
                return null;
            }

            var context = new PrArtifactContext(client, headBranch, platformFilter, prNumber, owner, repo);
            foreach (var run in runs.Value.EnumerateArray())
            {
                var artifact = await FindMatchingArtifactInRunAsync(context, run, cancellationToken).ConfigureAwait(false);
                if (artifact != null)
                {
                    return artifact;
                }
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

    private async Task<string?> GetPrHeadBranchAsync(
        HttpClient client,
        string owner,
        string repo,
        int prNumber,
        CancellationToken cancellationToken)
    {
        var prUrl = string.Format(ApiConstants.GitHubApiPrDetailFormat, owner, repo, prNumber);
        var prResponse = await SendWithRetryAsync(client, prUrl, cancellationToken).ConfigureAwait(false);

        if (prResponse == null || !prResponse.IsSuccessStatusCode)
        {
            _logger.LogWarning("Failed to fetch PR #{PrNumber} details: {Status}", prNumber, prResponse?.StatusCode);
            return null;
        }

        var prJson = await prResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var prData = JsonSerializer.Deserialize<JsonElement>(prJson);

        var headBranch = prData.TryGetProperty("head", out var head) && head.ValueKind == JsonValueKind.Object && head.TryGetProperty("ref", out var headRef)
            ? headRef.GetString()
            : null;

        if (string.IsNullOrEmpty(headBranch))
        {
            _logger.LogWarning("Could not determine head branch for PR #{PrNumber}", prNumber);
            return null;
        }

        _logger.LogInformation("PR #{PrNumber} head branch: {Branch}", prNumber, headBranch);
        return headBranch;
    }

    private async Task<JsonElement?> FetchWorkflowRunsForBranchAsync(
        HttpClient client,
        string owner,
        string repo,
        string headBranch,
        int prNumber,
        CancellationToken cancellationToken)
    {
        var runsUrl = string.Format(ApiConstants.GitHubApiWorkflowRunsFormat, owner, repo, headBranch);
        var runsResponse = await SendWithRetryAsync(client, runsUrl, cancellationToken).ConfigureAwait(false);

        if (runsResponse == null || !runsResponse.IsSuccessStatusCode)
        {
            _logger.LogWarning("Failed to fetch workflow runs for PR #{PrNumber}: {Status}", prNumber, runsResponse?.StatusCode);
            return null;
        }

        var runsJson = await runsResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var runsData = JsonSerializer.Deserialize<JsonElement>(runsJson);

        if (!runsData.TryGetProperty("workflow_runs", out var runs) || runs.ValueKind != JsonValueKind.Array)
        {
            _logger.LogWarning("No workflow_runs property in response for PR #{PrNumber}", prNumber);
            return null;
        }

        _logger.LogInformation("Found {Count} workflow runs for PR #{PrNumber} on branch {Branch}", runs.GetArrayLength(), prNumber, headBranch);
        return runs;
    }

    private sealed record PrArtifactContext(
        HttpClient Client,
        string HeadBranch,
        string PlatformFilter,
        int PrNumber,
        string Owner,
        string Repo);

    private async Task<ArtifactUpdateInfo?> FindMatchingArtifactInRunAsync(
        PrArtifactContext context,
        JsonElement run,
        CancellationToken cancellationToken)
    {
        if (!TryParseSuccessfulRun(run, context.HeadBranch, out var runId, out var runUrl, out var shortHash, out var createdAt))
        {
            return null;
        }

        var artifactsUrl = string.Format(ApiConstants.GitHubApiRunArtifactsFormat, context.Owner, context.Repo, runId);
        var artifactsResponse = await SendWithRetryAsync(context.Client, artifactsUrl, cancellationToken).ConfigureAwait(false);

        if (artifactsResponse == null || !artifactsResponse.IsSuccessStatusCode)
        {
            _logger.LogWarning("Failed to fetch artifacts for run {RunId}: {Status}", runId, artifactsResponse?.StatusCode);
            return null;
        }

        var artifactsJson = await artifactsResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var artifactsData = JsonSerializer.Deserialize<JsonElement>(artifactsJson);

        if (!artifactsData.TryGetProperty("artifacts", out var artifacts) || artifacts.ValueKind != JsonValueKind.Array)
        {
            _logger.LogWarning("No artifacts property in response for run {RunId}", runId);
            return null;
        }

        var platformArtifact = FindPlatformArtifactInRun(artifacts, context.PlatformFilter, context.PrNumber, runId, runUrl, shortHash, createdAt);
        if (platformArtifact != null)
        {
            _logger.LogInformation("Found artifact for PR #{PrNumber}: {Version}", context.PrNumber, platformArtifact.Version);
            return platformArtifact;
        }

        return null;
    }

    /// <summary>
    /// Finds the latest artifact overall or for a specific branch (for artifact update checking).
    /// </summary>
    private async Task<ArtifactUpdateInfo?> FindLatestArtifactAsync(string? branch, CancellationToken cancellationToken)
    {
        if (_gitHubAuthService == null || !_gitHubAuthService.IsAuthenticated)
            return null;

        try
        {
            using var token = await _gitHubAuthService.GetAccessTokenAsync(cancellationToken);
            if (token == null)
                return null;

            using var client = CreateConfiguredHttpClientWithToken(token);
            var owner = AppConstants.GitHubRepositoryOwner;
            var repo = AppConstants.GitHubRepositoryName;

            var runsUrl = !string.IsNullOrEmpty(branch)
                ? string.Format(ApiConstants.GitHubApiWorkflowRunsFormat, owner, repo, branch)
                : string.Format(ApiConstants.GitHubApiWorkflowRunsAllFormat, owner, repo);

            if (!string.IsNullOrEmpty(branch))
            {
                _logger.LogInformation("Searching for latest workflow success on branch: {Branch}", branch);
            }
            else
            {
                _logger.LogInformation("Searching for overall latest workflow success");
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

            var platformFilter = GetCurrentPlatformFilter();
            if (platformFilter == null)
            {
                _logger.LogWarning("No update artifacts are published for {Platform}", RuntimeInformation.OSDescription);
                return null;
            }

            foreach (var run in runs.EnumerateArray())
            {
                var selectedArtifact = await CheckRunForLatestArtifactAsync(client, run, branch, platformFilter, owner, repo, cancellationToken);
                if (selectedArtifact != null)
                {
                    return selectedArtifact;
                }
            }

            _logger.LogWarning("No suitable artifacts found in workflow runs for branch {Branch}", branch ?? "any");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to find latest artifact for branch {Branch}", branch ?? "any");
            return null;
        }
    }

    private async Task<ArtifactUpdateInfo?> CheckRunForLatestArtifactAsync(
        HttpClient client,
        JsonElement run,
        string? branch,
        string platformFilter,
        string owner,
        string repo,
        CancellationToken cancellationToken)
    {
        var runId = run.TryGetProperty("id", out var idProp) && idProp.TryGetInt64(out var rId) ? rId : 0;
        var runUrl = run.TryGetProperty("html_url", out var huProp) ? huProp.GetString() ?? string.Empty : string.Empty;
        var headSha = run.TryGetProperty("head_sha", out var hsProp) ? hsProp.GetString() ?? string.Empty : string.Empty;
        var shortHash = headSha.Length >= AppConstants.GitShortHashLength ? headSha[..AppConstants.GitShortHashLength] : headSha;

        if (!IsMatchingBranchRun(run, branch, out var actualBranch, out var eventType))
        {
            _logger.LogDebug("Skipping run {RunId} ({ActualBranch}, {EventType}) for branch {Branch}", runId, actualBranch, eventType, branch);
            return null;
        }

        _logger.LogDebug("Checking run {RunId} ({EventType}) on branch {ActualBranch}", runId, eventType, actualBranch);

        if (!string.IsNullOrEmpty(branch) && !string.Equals(actualBranch, branch, StringComparison.Ordinal))
        {
            _logger.LogDebug("Skipping run {RunId} ({ActualBranch}) - does not match requested branch {Branch}", runId, actualBranch, branch);
            return null;
        }

        if (!string.IsNullOrEmpty(branch) &&
            !string.Equals(eventType, "push", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(eventType, "workflow_dispatch", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(eventType, "pull_request", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug("Skipping run {RunId} ({EventType}) - not a push, workflow_dispatch, or pull_request event for branch {Branch}", runId, eventType, branch);
            return null;
        }

        var createdAt = ParseRunCreatedAt(run);

        _logger.LogDebug("Checking run {RunId} on branch {Branch} ({Hash}) for artifacts...", runId, actualBranch, shortHash);

        var artifactsUrl = string.Format(ApiConstants.GitHubApiRunArtifactsFormat, owner, repo, runId);
        var artifactsResponse = await SendWithRetryAsync(client, artifactsUrl, cancellationToken).ConfigureAwait(false);

        if (artifactsResponse == null || !artifactsResponse.IsSuccessStatusCode)
        {
            _logger.LogWarning("Failed to fetch artifacts for run {RunId}: {Status}", runId, artifactsResponse?.StatusCode);
            return null;
        }

        var artifactsJson = await artifactsResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var artifactsData = JsonSerializer.Deserialize<JsonElement>(artifactsJson);

        if (!artifactsData.TryGetProperty("artifacts", out var artifacts) || artifacts.GetArrayLength() == 0)
        {
            _logger.LogWarning("No artifacts found for run {RunId}", runId);
            return null;
        }

        var selectedArtifact = FindPlatformArtifactInRun(artifacts, platformFilter, null, runId, runUrl, shortHash, createdAt);
        if (selectedArtifact != null)
        {
            return selectedArtifact;
        }

        _logger.LogDebug("No suitable Velopack artifacts found for current platform in run {RunId}, checking next run", runId);
        return null;
    }

    private async Task<IReadOnlyList<ArtifactUpdateInfo>> FindArtifactsAsync(HttpClient client, string? branchName, int? prNumber, CancellationToken cancellationToken)
    {
        var owner = AppConstants.GitHubRepositoryOwner;
        var repo = AppConstants.GitHubRepositoryName;

        var runsUrl = !string.IsNullOrEmpty(branchName)
            ? string.Format(ApiConstants.GitHubApiWorkflowRunsFormat, owner, repo, branchName)
            : string.Format(ApiConstants.GitHubApiWorkflowRunsAllFormat, owner, repo);

        var runsResponse = await SendWithRetryAsync(client, runsUrl, cancellationToken);
        if (runsResponse == null || !runsResponse.IsSuccessStatusCode)
        {
            return [];
        }

        var runsJson = await runsResponse.Content.ReadAsStringAsync(cancellationToken);
        using var runsDoc = JsonDocument.Parse(runsJson);
        if (!runsDoc.RootElement.TryGetProperty("workflow_runs", out var workflowRuns))
        {
            return [];
        }

        var platformFilter = GetCurrentPlatformFilter();
        if (platformFilter == null)
        {
            _logger.LogWarning("Unsupported platform for artifacts");
            return [];
        }

        var results = new List<ArtifactUpdateInfo>();
        var addedVersions = new HashSet<string>();

        foreach (var run in workflowRuns.EnumerateArray())
        {
            if (!IsMatchingWorkflowRun(run, branchName, prNumber))
            {
                continue;
            }

            await ExtractArtifactsFromWorkflowRunAsync(client, run, prNumber, platformFilter, addedVersions, results, cancellationToken);
        }

        return [.. results.OrderByDescending(r => r.CreatedAt)];
    }

    private async Task ExtractArtifactsFromWorkflowRunAsync(
        HttpClient client,
        JsonElement run,
        int? prNumber,
        string platformFilter,
        HashSet<string> addedVersions,
        List<ArtifactUpdateInfo> results,
        CancellationToken cancellationToken)
    {
        var artifactsUrl = run.TryGetProperty("artifacts_url", out var u) ? u.GetString() : null;
        if (string.IsNullOrEmpty(artifactsUrl))
        {
            return;
        }

        var artifactsResponse = await SendWithRetryAsync(client, artifactsUrl, cancellationToken);
        if (artifactsResponse == null || !artifactsResponse.IsSuccessStatusCode)
        {
            return;
        }

        var artifactsJson = await artifactsResponse.Content.ReadAsStringAsync(cancellationToken);
        using var artifactsDoc = JsonDocument.Parse(artifactsJson);
        if (!artifactsDoc.RootElement.TryGetProperty("artifacts", out var artifacts))
        {
            return;
        }

        if (!run.TryGetProperty("id", out var idProp) || !idProp.TryGetInt64(out var runId) ||
            !run.TryGetProperty("run_number", out var runNumProp) || !runNumProp.TryGetInt32(out var runNum) ||
            !run.TryGetProperty("created_at", out var createdAtProp) || !createdAtProp.TryGetDateTimeOffset(out var createdAt))
        {
            return;
        }

        var headSha = run.TryGetProperty("head_sha", out var sha) ? sha.GetString() ?? string.Empty : string.Empty;
        var shortHash = headSha.Length >= AppConstants.GitShortHashLength ? headSha[..AppConstants.GitShortHashLength] : headSha;
        var workflowRunUrl = run.TryGetProperty("html_url", out var html) ? html.GetString() ?? string.Empty : string.Empty;

        foreach (var artifact in artifacts.EnumerateArray())
        {
            var info = TryParseArtifactUpdateInfo(artifact, runId, runNum, createdAt.UtcDateTime, shortHash, workflowRunUrl, prNumber, platformFilter, addedVersions);
            if (info != null)
            {
                results.Add(info);
            }
        }
    }

    private ArtifactUpdateInfo? TryParseArtifactUpdateInfo(
        JsonElement artifact,
        long runId,
        int runNum,
        DateTime createdAtUtc,
        string shortHash,
        string workflowRunUrl,
        int? prNumber,
        string platformFilter,
        HashSet<string> addedVersions)
    {
        var name = artifact.TryGetProperty("name", out var n) ? n.GetString() : null;
        if (string.IsNullOrEmpty(name) || !name.Contains("velopack", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!name.Contains(platformFilter, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug("Skipping artifact {Name} - doesn't match platform {Platform}", name, platformFilter);
            return null;
        }

        var version = ExtractVersionFromArtifactName(name) ?? $"0.0.0-ci.{runNum}";
        var uniqueKey = $"{version}|{shortHash}";
        if (!addedVersions.Add(uniqueKey))
        {
            _logger.LogDebug("Skipping duplicate artifact: {Version} ({Hash})", version, shortHash);
            return null;
        }

        var id = artifact.TryGetProperty("id", out var idProp) && idProp.TryGetInt64(out var aId) ? aId : 0;
        var size = artifact.TryGetProperty("size_in_bytes", out var sizeProp) && sizeProp.TryGetInt64(out var s) ? s : 0;
        var downloadUrl = artifact.TryGetProperty("archive_download_url", out var dl) ? dl.GetString() : null;

        return new ArtifactUpdateInfo(
            Version: version,
            GitHash: shortHash,
            PullRequestNumber: prNumber,
            WorkflowRunId: runId,
            WorkflowRunUrl: workflowRunUrl,
            ArtifactId: id,
            ArtifactName: name,
            CreatedAt: createdAtUtc,
            DownloadUrl: downloadUrl,
            Size: size);
    }

    private async Task<string?> TryFindLocalArtifactPathAsync(ArtifactUpdateInfo artifactInfo, CancellationToken cancellationToken)
    {
        if (_contentManifestPool == null || _contentStorageService == null)
        {
            return null;
        }

        try
        {
            var manifestsResult = await _contentManifestPool.GetAllManifestsAsync(cancellationToken).ConfigureAwait(false);
            if (!manifestsResult.Success || manifestsResult.Data == null)
            {
                return null;
            }

            foreach (var manifest in manifestsResult.Data)
            {
                if (!GenHubBuildInspector.IsGenHubApplicationBuild(manifest.ContentType, manifest.Name, manifest.Metadata?.Tags))
                {
                    continue;
                }

                if (!IsManifestMatchingArtifact(manifest, artifactInfo))
                {
                    continue;
                }

                var localPath = await TryRetrieveLocalManifestAsync(manifest.Id, cancellationToken).ConfigureAwait(false);
                if (localPath != null)
                {
                    return localPath;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Failed to check local storage for artifact {Name} ({Version})", artifactInfo.ArtifactName, artifactInfo.Version);
        }

        return null;
    }

    private async Task<string?> TryRetrieveLocalManifestAsync(string manifestId, CancellationToken cancellationToken)
    {
        var isAcquiredResult = await _contentManifestPool!.IsManifestAcquiredAsync(manifestId, cancellationToken).ConfigureAwait(false);
        if (!isAcquiredResult.Success || !isAcquiredResult.Data)
        {
            return null;
        }

        var tempDir = Path.Combine(AppDataPathHelper.GetDataRoot(), "Temp", $"genhub-local-build-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var success = false;
        try
        {
            var retrieveResult = await _contentStorageService!.RetrieveContentAsync(manifestId, tempDir, cancellationToken).ConfigureAwait(false);
            if (retrieveResult.Success)
            {
                success = true;
                return tempDir;
            }

            return null;
        }
        finally
        {
            if (!success)
            {
                try
                {
                    if (Directory.Exists(tempDir))
                    {
                        Directory.Delete(tempDir, recursive: true);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogTrace(ex, "Failed to delete aborted local build temp dir {Path}", tempDir);
                }
            }
        }
    }
}
