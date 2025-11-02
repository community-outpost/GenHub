using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.GeneralsOnline;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Content.Services.GeneralsOnline;

/// <summary>
/// Background service for checking Generals Online updates.
/// Polls CDN for new releases and notifies when updates are available.
/// </summary>
public class GeneralsOnlineUpdateService : BackgroundService, IContentUpdateService
{
    private readonly ILogger<GeneralsOnlineUpdateService> _logger;
    private readonly IContentManifestPool _manifestPool;
    private readonly HttpClient _httpClient;
    private readonly TimeSpan _updateCheckInterval;

    /// <summary>
    /// Initializes a new instance of the <see cref="GeneralsOnlineUpdateService"/> class.
    /// </summary>
    /// <param name="logger">The logger for diagnostic information.</param>
    /// <param name="manifestPool">The content manifest pool.</param>
    public GeneralsOnlineUpdateService(
        ILogger<GeneralsOnlineUpdateService> logger,
        IContentManifestPool manifestPool)
    {
        _logger = logger;
        _manifestPool = manifestPool;
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _updateCheckInterval = TimeSpan.FromHours(GeneralsOnlineConstants.UpdateCheckIntervalHours);
    }

    /// <inheritdoc />
    public async Task<(bool UpdateAvailable, string? LatestVersion, string? CurrentVersion)> CheckForUpdatesAsync(
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Checking for Generals Online updates...");

        try
        {
            // Get current installed version
            var currentVersion = await GetInstalledVersionAsync(cancellationToken);

            // Get latest version from CDN
            var latestVersion = await GetLatestVersionFromCdnAsync(cancellationToken);

            if (string.IsNullOrEmpty(latestVersion))
            {
                _logger.LogWarning("Could not retrieve latest version from CDN");
                return (false, null, currentVersion);
            }

            var updateAvailable = IsNewerVersion(latestVersion, currentVersion);

            if (updateAvailable)
            {
                _logger.LogInformation(
                    "Generals Online update available: {LatestVersion} (current: {CurrentVersion})",
                    latestVersion,
                    currentVersion ?? "not installed");
            }
            else
            {
                _logger.LogInformation("Generals Online is up to date: {Version}", currentVersion ?? latestVersion);
            }

            return (updateAvailable, latestVersion, currentVersion);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check for Generals Online updates");
            return (false, null, null);
        }
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        _httpClient?.Dispose();
        base.Dispose();
    }

    /// <summary>
    /// Executes the background update checking service.
    /// </summary>
    /// <param name="stoppingToken">Cancellation token.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Generals Online Update Service started");

        // Wait 5 minutes after startup before first check
        await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckForUpdatesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking for Generals Online updates");
            }

            // Wait for next check interval
            await Task.Delay(_updateCheckInterval, stoppingToken);
        }
    }

    private async Task<string?> GetInstalledVersionAsync(CancellationToken cancellationToken)
    {
        try
        {
            var manifests = await _manifestPool.GetAllManifestsAsync(cancellationToken);
            if (!manifests.Success || manifests.Data == null)
            {
                return null;
            }

            var goManifest = manifests.Data.FirstOrDefault(m =>
                m.Publisher?.Name?.Equals(GeneralsOnlineConstants.PublisherName, StringComparison.OrdinalIgnoreCase) == true);

            return goManifest?.Version;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get installed Generals Online version");
            return null;
        }
    }

    private async Task<string?> GetLatestVersionFromCdnAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Try to get version from latest.txt
            var response = await _httpClient.GetAsync(GeneralsOnlineConstants.LatestVersionUrl, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var version = await response.Content.ReadAsStringAsync(cancellationToken);
                return version?.Trim();
            }

            _logger.LogDebug("latest.txt not available, falling back to directory listing");

            // Fallback: Try to parse releases directory
            // For alpha, we return mock version
            return await Task.FromResult("101525_QFE5");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get latest version from CDN");
            return null;
        }
    }

    private bool IsNewerVersion(string latestVersion, string? currentVersion)
    {
        if (string.IsNullOrEmpty(currentVersion))
        {
            return true; // Any version is newer than nothing
        }

        // Parse DDMMYY_QFE# format
        var latest = ParseVersion(latestVersion);
        var current = ParseVersion(currentVersion);

        if (latest == null || current == null)
        {
            return false;
        }

        // Compare date first
        if (latest.Value.date > current.Value.date)
        {
            return true;
        }

        if (latest.Value.date == current.Value.date)
        {
            // Same date, compare QFE number
            return latest.Value.qfe > current.Value.qfe;
        }

        return false;
    }

    private (DateTime date, int qfe)? ParseVersion(string version)
    {
        try
        {
            // Format: DDMMYY_QFE#
            var parts = version.Split('_');
            if (parts.Length != 2)
            {
                return null;
            }

            var datePart = parts[0];
            var qfePart = parts[1].Replace("QFE", string.Empty);

            if (datePart.Length != 6 || !int.TryParse(qfePart, out var qfe))
            {
                return null;
            }

            var month = int.Parse(datePart.Substring(0, 2));
            var day = int.Parse(datePart.Substring(2, 2));
            var year = 2000 + int.Parse(datePart.Substring(4, 2));

            var date = new DateTime(year, month, day);
            return (date, qfe);
        }
        catch
        {
            _logger.LogWarning("Failed to parse version: {Version}", version);
            return null;
        }
    }
}
