using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security;
using System.Text.Json;
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
public class VelopackUpdateManager : IVelopackUpdateManager, IDisposable
{
    /// <summary>
    /// Length of the git short hash used in versioning (7 characters).
    /// </summary>
    private const int GitShortHashLength = 7;

    /// <summary>
    /// Delay before exit after applying update (5 seconds).
    /// </summary>
    private static readonly TimeSpan PostUpdateExitDelay = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Delay for showing completion message (1.5 seconds).
    /// </summary>
    private static readonly TimeSpan CompletionMessageDelay = TimeSpan.FromMilliseconds(1500);

    private readonly ILogger<VelopackUpdateManager> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IGitHubTokenStorage? _gitHubTokenStorage;
    private readonly IUserSettingsService? _userSettingsService;
    private readonly UpdateManager? _updateManager;
    private readonly GithubSource _githubSource;
    private bool _hasUpdateFromGitHub;
    private string? _latestVersionFromGitHub;
    private ArtifactUpdateInfo? _latestArtifactUpdate;

    /// <inheritdoc/>
    public UpdateChannel CurrentChannel { get; set; }

    /// <inheritdoc/>
    public bool HasArtifactUpdateAvailable => _latestArtifactUpdate != null;

    /// <inheritdoc/>
    public ArtifactUpdateInfo? LatestArtifactUpdate => _latestArtifactUpdate;

    /// <inheritdoc/>
    public int? SubscribedPrNumber { get; set; }

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

        // Initialize CurrentChannel from settings
        CurrentChannel = userSettingsService?.Get()?.UpdateChannel ?? UpdateChannel.Stable;

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

            // Call GitHub API to get latest release
            var apiUrl = $"https://api.github.com/repos/{owner}/{repo}/releases";
            using var client = CreateConfiguredHttpClient();
            var response = await client.GetAsync(apiUrl, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("GitHub API request failed: {StatusCode} - {Reason}", response.StatusCode, response.ReasonPhrase);
                return null;
            }

            var json = await client.GetStringAsync(apiUrl, cancellationToken);

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

            await _updateManager.DownloadUpdatesAsync(updateInfo, velopackProgress);

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
            Task.Delay(PostUpdateExitDelay).Wait();
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
            _logger.LogError(ex, "Failed to apply updates and exit");
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
        _logger.LogInformation("Checking for artifact updates from GitHub Actions CI builds");

        if (_gitHubTokenStorage == null)
        {
            _logger.LogDebug("No GitHub token storage available, skipping artifact updates check");
            return null;
        }

        try
        {
            _latestArtifactUpdate = await FindLatestArtifactAsync(cancellationToken);
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
            var prsResponse = await client.GetAsync(prsUrl, cancellationToken);

            if (!prsResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch open PRs: {Status}", prsResponse.StatusCode);
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
                var title = pr.GetProperty("title").GetString() ?? "Unknown";
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

                // Check if this is our subscribed PR
                if (SubscribedPrNumber == prNumber)
                {
                    subscribedPrFound = true;
                    IsPrMergedOrClosed = false;
                }

                // Find latest artifact for this PR
                ArtifactUpdateInfo? latestArtifact = await FindLatestArtifactForPrAsync(client, prNumber, cancellationToken);

                var prInfo = new PullRequestInfo
                {
                    Number = prNumber,
                    Title = title,
                    BranchName = branchName,
                    Author = author,
                    State = state,
                    UpdatedAt = updatedAt,
                    LatestArtifact = latestArtifact,
                };

                results.Add(prInfo);
            }

            // Update merged/closed status for subscribed PR
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
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch open pull requests");
            return results;
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

        if (_gitHubTokenStorage == null || !_gitHubTokenStorage.HasToken())
        {
            throw new InvalidOperationException("GitHub PAT required to download PR artifacts");
        }

        SimpleHttpServer? server = null;
        string? tempDir = null;

        try
        {
            progress?.Report(new UpdateProgress { Status = "Downloading PR artifact...", PercentComplete = 0 });

            var token = await _gitHubTokenStorage.LoadTokenAsync();
            if (token == null)
            {
                throw new InvalidOperationException("Failed to load GitHub PAT");
            }

            using var client = CreateConfiguredHttpClientWithToken(token);
            var owner = AppConstants.GitHubRepositoryOwner;
            var repo = AppConstants.GitHubRepositoryName;
            var artifactId = prInfo.LatestArtifact.ArtifactId;

            // Download artifact
            var downloadUrl = string.Format(ApiConstants.GitHubApiArtifactDownloadFormat, owner, repo, artifactId);
            _logger.LogInformation("Downloading PR #{Number} artifact from {Url}", prInfo.Number, downloadUrl);

            var response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            // Create temp directory
            tempDir = Path.Combine(Path.GetTempPath(), $"genhub-pr{prInfo.Number}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);

            var zipPath = Path.Combine(tempDir, "artifact.zip");
            using (var fileStream = File.Create(zipPath))
            {
                await response.Content.CopyToAsync(fileStream, cancellationToken);
            }

            progress?.Report(new UpdateProgress { Status = "Extracting artifact...", PercentComplete = 30 });

            // Extract the ZIP
            ZipFile.ExtractToDirectory(zipPath, tempDir);

            // Find .nupkg file
            var nupkgFiles = Directory.GetFiles(tempDir, "*.nupkg", SearchOption.AllDirectories);

            if (nupkgFiles.Length == 0)
            {
                throw new FileNotFoundException("No .nupkg file found in PR artifact");
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
            var versionMatch = System.Text.RegularExpressions.Regex.Match(
                nupkgFileName,
                @"GenHub-(.+)-full\.nupkg",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var fileVersion = versionMatch.Success ? versionMatch.Groups[1].Value : prInfo.LatestArtifact.Version;

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

            var asset = new VelopackAsset
            {
                PackageId = AppConstants.AppName,
                Version = NuGet.Versioning.SemanticVersion.Parse(fileVersion),
                Type = VelopackAssetType.Full,
                FileName = nupkgFileName,
                SHA1 = sha1,
                SHA256 = sha256,
                Size = fileInfo.Length,
            };

            progress?.Report(new UpdateProgress { Status = "Downloading update...", PercentComplete = 70 });

            // Point Velopack to localhost
            var source = new SimpleWebSource($"http://localhost:{port}/{server.SecretToken}/");
            var localUpdateManager = new UpdateManager(source);

            try
            {
                var updateInfo = await localUpdateManager.CheckForUpdatesAsync();

                if (updateInfo == null)
                {
                    var currentVersionStr = AppConstants.AppVersion.Split('+')[0];
                    var targetVersionStr = fileVersion.Split('+')[0];

                    _logger.LogWarning(
                        "Cannot install PR artifact: current version ({Current}) >= target ({Target})",
                        currentVersionStr,
                        targetVersionStr);
                    _logger.LogInformation(
                        "Full versions: current={CurrentFull}, target={TargetFull}",
                        AppConstants.AppVersion,
                        fileVersion);

                    if (currentVersionStr.Equals(targetVersionStr, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException(
                            $"PR build {fileVersion} is already installed (current: {AppConstants.AppVersion}). " +
                            $"This is the same version with different build metadata.");
                    }
                    else
                    {
                        throw new InvalidOperationException(
                            $"Cannot install PR build {fileVersion}: Current version ({AppConstants.AppVersion}) is newer. " +
                            $"To install this older PR build, uninstall GenHub first, then run Setup.exe from the PR artifact.");
                    }
                }

                // Download from localhost
                await localUpdateManager.DownloadUpdatesAsync(updateInfo, p =>
                {
                    progress?.Report(new UpdateProgress
                    {
                        Status = "Downloading update...",
                        PercentComplete = 70 + (int)(p * 0.2),
                    });
                });

                progress?.Report(new UpdateProgress { Status = "Installing update...", PercentComplete = 90 });

                _logger.LogInformation("Applying PR #{Number} update and restarting", prInfo.Number);
                _logger.LogInformation("Update version: {Version}", updateInfo.TargetFullRelease.Version);
                _logger.LogInformation("Update package: {Package}", updateInfo.TargetFullRelease.FileName);

                try
                {
                    _logger.LogInformation("Using ApplyUpdatesAndRestart for PR artifact installation");
                    localUpdateManager.ApplyUpdatesAndRestart(updateInfo.TargetFullRelease);

                    _logger.LogWarning("ApplyUpdatesAndRestart returned without exiting - waiting for exit...");
                    await Task.Delay(PostUpdateExitDelay, cancellationToken);

                    _logger.LogError("Application did not exit after ApplyUpdatesAndRestart. Update may have failed.");
                    throw new InvalidOperationException("Application did not exit after applying update");
                }
                catch (Exception restartEx)
                {
                    _logger.LogError(restartEx, "Failed to apply PR artifact update");
                    _logger.LogError("Update file: {File}", updateInfo.TargetFullRelease.FileName);
                    _logger.LogError("Update version: {Version}", updateInfo.TargetFullRelease.Version);
                    throw;
                }
            }
            finally
            {
                // No cleanup needed for UpdateManager
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to install PR artifact");
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
            var prResponse = await client.GetAsync(prUrl, cancellationToken);

            if (!prResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch PR #{PrNumber} details: {Status}", prNumber, prResponse.StatusCode);
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
            var runsResponse = await client.GetAsync(runsUrl, cancellationToken);

            if (!runsResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch workflow runs for PR #{PrNumber}: {Status}", prNumber, runsResponse.StatusCode);
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
                var shortHash = headSha.Length >= GitShortHashLength ? headSha[..GitShortHashLength] : headSha;

                _logger.LogInformation("Fetching artifacts for workflow run {RunId} (PR #{PrNumber})", runId, prNumber);

                var artifactsUrl = string.Format(ApiConstants.GitHubApiRunArtifactsFormat, owner, repo, runId);
                var artifactsResponse = await client.GetAsync(artifactsUrl, cancellationToken);

                if (!artifactsResponse.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Failed to fetch artifacts for run {RunId}: {Status}", runId, artifactsResponse.StatusCode);
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
    /// Finds the latest artifact overall (for artifact update checking).
    /// </summary>
    private async Task<ArtifactUpdateInfo?> FindLatestArtifactAsync(CancellationToken cancellationToken)
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

            var runsUrl = string.Format(ApiConstants.GitHubApiLatestWorkflowRunsFormat, owner, repo);
            var runsResponse = await client.GetAsync(runsUrl, cancellationToken);

            if (!runsResponse.IsSuccessStatusCode)
                return null;

            var runsJson = await runsResponse.Content.ReadAsStringAsync(cancellationToken);
            var runsData = JsonSerializer.Deserialize<JsonElement>(runsJson);

            if (!runsData.TryGetProperty("workflow_runs", out var runs) || runs.GetArrayLength() == 0)
                return null;

            var latestRun = runs.EnumerateArray().FirstOrDefault();
            var runId = latestRun.GetProperty("id").GetInt64();
            var runUrl = latestRun.GetProperty("html_url").GetString() ?? string.Empty;
            var headSha = latestRun.GetProperty("head_sha").GetString() ?? string.Empty;
            var shortHash = headSha.Length >= GitShortHashLength ? headSha[..GitShortHashLength] : headSha;

            DateTime createdAt;
            try
            {
                createdAt = latestRun.GetProperty("created_at").GetDateTime();
            }
            catch (FormatException)
            {
                createdAt = DateTime.MinValue;
            }

            var artifactsUrl = string.Format(ApiConstants.GitHubApiRunArtifactsFormat, owner, repo, runId);
            var artifactsResponse = await client.GetAsync(artifactsUrl, cancellationToken);

            if (!artifactsResponse.IsSuccessStatusCode)
                return null;

            var artifactsJson = await artifactsResponse.Content.ReadAsStringAsync(cancellationToken);
            var artifactsData = JsonSerializer.Deserialize<JsonElement>(artifactsJson);

            if (!artifactsData.TryGetProperty("artifacts", out var artifacts))
                return null;

            foreach (var artifact in artifacts.EnumerateArray())
            {
                var artifactName = artifact.GetProperty("name").GetString() ?? string.Empty;

                if (!artifactName.Contains("velopack", StringComparison.OrdinalIgnoreCase))
                    continue;

                var artifactId = artifact.GetProperty("id").GetInt64();
                var version = ExtractVersionFromArtifactName(artifactName) ?? "unknown";

                return new ArtifactUpdateInfo(
                    version: version,
                    gitHash: shortHash,
                    pullRequestNumber: null,
                    workflowRunId: runId,
                    workflowRunUrl: runUrl,
                    artifactId: artifactId,
                    artifactName: artifactName,
                    createdAt: createdAt);
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to find latest artifact");
            return null;
        }
    }
}
