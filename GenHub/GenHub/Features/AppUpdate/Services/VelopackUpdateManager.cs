using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Models.AppUpdate;
using GenHub.Features.AppUpdate.Interfaces;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;
using Velopack;
using Velopack.Sources;

namespace GenHub.Features.AppUpdate.Services;

/// <summary>
/// Velopack-based update manager service.
/// </summary>
public class VelopackUpdateManager : IVelopackUpdateManager, IDisposable
{
    private readonly ILogger<VelopackUpdateManager> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly UpdateManager? _updateManager;
    private readonly GithubSource _githubSource;
    private bool _hasUpdateFromGitHub;
    private string? _latestVersionFromGitHub;

    /// <summary>
    /// Initializes a new instance of the <see cref="VelopackUpdateManager"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="httpClientFactory">The HTTP client factory for creating HttpClient instances.</param>
    public VelopackUpdateManager(ILogger<VelopackUpdateManager> logger, IHttpClientFactory httpClientFactory)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));

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
            _updateManager.ApplyUpdatesAndRestart(updateInfo.TargetFullRelease);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply updates and restart");
            throw;
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

    /// <summary>
    /// Gets or creates an HttpClient instance with proper configuration.
    /// </summary>
    /// <returns>An HttpClient instance.</returns>
    private HttpClient CreateConfiguredHttpClient()
    {
        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.UserAgent.Clear();
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("GenHub", AppConstants.AppVersion));
        return client;
    }
}
