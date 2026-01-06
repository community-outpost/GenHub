using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.Results.Content;
using Microsoft.Extensions.Logging;
using System;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Content.Services.GeneralsOnline;

/// <summary>
/// Background service for checking Generals Online updates.
/// Polls CDN for new releases and notifies when updates are available.
/// Uses data-driven configuration from provider.json for endpoints.
/// </summary>
public class GeneralsOnlineUpdateService(
    ILogger<GeneralsOnlineUpdateService> logger,
    IContentManifestPool manifestPool,
    IHttpClientFactory httpClientFactory,
    IProviderDefinitionLoader providerLoader) : ContentUpdateServiceBase(logger)
{
    private readonly HttpClient _httpClient = httpClientFactory.CreateClient(GeneralsOnlineConstants.PublisherType);

    /// <inheritdoc />
    public override void Dispose()
    {
        _httpClient?.Dispose();
        base.Dispose();
        GC.SuppressFinalize(this);
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
        logger.LogInformation("Checking for Generals Online updates");

        try
        {
            // Get current installed version
            var currentVersion = await GetInstalledVersionAsync(cancellationToken);

            // Get latest version from CDN
            var latestVersion = await GetLatestVersionFromCdnAsync(cancellationToken);

            if (string.IsNullOrEmpty(latestVersion))
            {
                logger.LogWarning("Could not retrieve latest version from CDN");
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
            logger.LogError(ex, "Failed to check for Generals Online updates");
            throw;
        }
    }

    private static bool IsNewerVersion(string latestVersion, string? currentVersion)
    {
        if (string.IsNullOrEmpty(currentVersion))
        {
            return true; // Any version is newer than nothing
        }

        // Parse MMddyy_QFE# format
        var latest = GameVersionHelper.ParseGeneralsOnlineVersion(latestVersion);
        var current = GameVersionHelper.ParseGeneralsOnlineVersion(currentVersion);

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

    private async Task<string?> GetInstalledVersionAsync(CancellationToken cancellationToken)
    {
        try
        {
            var manifests = await manifestPool.GetAllManifestsAsync(cancellationToken);
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
            logger.LogWarning(ex, "Failed to get installed Generals Online version");
            return null;
        }
    }

    private async Task<string?> GetLatestVersionFromCdnAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Get provider definition
            var provider = providerLoader.GetProvider(GeneralsOnlineConstants.PublisherType);
            if (provider == null)
            {
                logger.LogError("Provider definition not found for {ProviderId}", GeneralsOnlineConstants.PublisherType);
                return null;
            }

            var latestVersionUrl = provider.Endpoints.GetEndpoint(ProviderEndpointConstants.LatestVersionUrl);
            if (string.IsNullOrEmpty(latestVersionUrl))
            {
                // Fallback to standard endpoint name lookup
                latestVersionUrl = provider.Endpoints.GetEndpoint("latestVersionUrl");
            }

            if (string.IsNullOrEmpty(latestVersionUrl))
            {
                logger.LogError("latestVersionUrl not configured in provider definition (checked both 'custom.latestVersionUrl' and 'latestVersionUrl')");
                return null;
            }

            // Add cache-busting to prevent HTTP caching of old version
            var cacheBuster = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var urlWithCacheBuster = $"{latestVersionUrl}?nocache={cacheBuster}";

            logger.LogDebug("Fetching latest version from CDN with cache-busting: {Url}", urlWithCacheBuster);

            // Try to get version from latest.txt with retries
            HttpResponseMessage? response = null;
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    response = await _httpClient.GetAsync(urlWithCacheBuster, cancellationToken);
                    if (response.IsSuccessStatusCode)
                    {
                        break;
                    }
                }
                catch (Exception ex) when (i < 2)
                {
                    logger.LogWarning(ex, "Attempt {Attempt} failed to fetch latest version", i + 1);
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                }
            }

            if (response == null || !response.IsSuccessStatusCode)
            {
                logger.LogError("Failed to fetch latest version after 3 attempts");
                return null;
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var version = content?.Trim();

            logger.LogInformation("Successfully fetched version from CDN: '{Version}' (length: {Length})", version, version?.Length ?? 0);

            return version;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to get latest version from CDN");
            return null;
        }
    }
}
