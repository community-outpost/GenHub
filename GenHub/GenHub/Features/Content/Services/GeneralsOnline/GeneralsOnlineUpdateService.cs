using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Results.Content;
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
public class GeneralsOnlineUpdateService : ContentUpdateServiceBase
{
    private readonly ILogger<GeneralsOnlineUpdateService> _logger;
    private readonly IContentManifestPool _manifestPool;
    private readonly HttpClient _httpClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="GeneralsOnlineUpdateService"/> class.
    /// </summary>
    /// <param name="logger">The logger for diagnostic information.</param>
    /// <param name="manifestPool">The content manifest pool.</param>
    /// <param name="httpClientFactory">Factory for creating HTTP clients.</param>
    public GeneralsOnlineUpdateService(
        ILogger<GeneralsOnlineUpdateService> logger,
        IContentManifestPool manifestPool,
        IHttpClientFactory httpClientFactory)
        : base(logger)
    {
        _logger = logger;
        _manifestPool = manifestPool;
        _httpClient = httpClientFactory.CreateClient(GeneralsOnlineConstants.PublisherType);
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        _httpClient?.Dispose();
        base.Dispose();
    }

    /// <inheritdoc />
    protected override string ServiceName => GeneralsOnlineConstants.ContentName;

    /// <inheritdoc />
    protected override TimeSpan UpdateCheckInterval =>
        TimeSpan.FromHours(GeneralsOnlineConstants.UpdateCheckIntervalHours);

    /// <inheritdoc />
    public override async Task<ContentUpdateCheckResult>
        CheckForUpdatesAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Checking for Generals Online updates");

        try
        {
            // Get current installed version
            var currentVersion = await GetInstalledVersionAsync(cancellationToken);

            // Get latest version from CDN
            var latestVersion = await GetLatestVersionFromCdnAsync(cancellationToken);

            if (string.IsNullOrEmpty(latestVersion))
            {
                _logger.LogWarning("Could not retrieve latest version from CDN");
                return ContentUpdateCheckResult.CreateFailure(
                    "Could not retrieve latest version from CDN",
                    currentVersion);
            }

            var updateAvailable = IsNewerVersion(latestVersion, currentVersion);

            if (updateAvailable)
            {
                return ContentUpdateCheckResult.CreateUpdateAvailable(
                    latestVersion: latestVersion,
                    currentVersion: currentVersion);
            }

            return ContentUpdateCheckResult.CreateNoUpdateAvailable(
                currentVersion: currentVersion,
                latestVersion: latestVersion);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check for Generals Online updates");
            throw;
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
                m.Publisher?.PublisherType?.Equals(GeneralsOnlineConstants.PublisherType, StringComparison.OrdinalIgnoreCase) == true);

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

            _logger.LogWarning("latest.txt not available, status code: {StatusCode}", response.StatusCode);
            return null;
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
        if (latest.Value.Date > current.Value.Date)
        {
            return true;
        }

        if (latest.Value.Date == current.Value.Date)
        {
            // Same date, compare QFE number
            return latest.Value.Qfe > current.Value.Qfe;
        }

        return false;
    }

    private (DateTime Date, int Qfe)? ParseVersion(string version)
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
