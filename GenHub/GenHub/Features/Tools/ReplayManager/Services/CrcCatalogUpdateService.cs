using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Tools.ReplayManager;
using GenHub.Core.Models.Results.Content;
using GenHub.Core.Models.Tools.ReplayManager;
using GenHub.Features.Content.Services;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Tools.ReplayManager.Services;

/// <summary>
/// Background update service for periodically polling and refreshing the Replay GameClient CRC catalog.
/// Supports in-memory caching and atomic offline persistence.
/// </summary>
public sealed class CrcCatalogUpdateService(
    HttpClient httpClient,
    ICrcMappingRegistry crcMappingRegistry,
    IDynamicContentCache dynamicContentCache,
    IConfigurationProviderService configurationProviderService,
    ILogger<CrcCatalogUpdateService> logger)
    : ContentUpdateServiceBase(logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    /// <inheritdoc />
    protected override string ServiceName => ReplayManagerConstants.CrcCatalogCacheKey;

    /// <inheritdoc />
    protected override TimeSpan UpdateCheckInterval => ReplayManagerConstants.DefaultCatalogUpdateInterval;

    /// <inheritdoc />
    public override async Task<ContentUpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken)
    {
        try
        {
            // 1. Check in-memory dynamic cache first
            var cached = await dynamicContentCache.GetAsync<CrcCatalog>(ReplayManagerConstants.CrcCatalogCacheKey, cancellationToken);
            if (cached != null && cached.Mappings.Count > 0)
            {
                crcMappingRegistry.LoadCatalog(cached);
                logger.LogDebug("Serving CRC mappings from fresh in-memory cache ({Count} entries)", cached.Mappings.Count);
                return ContentUpdateCheckResult.CreateNoUpdateAvailable(cached.LastUpdated?.ToString("O") ?? "cached");
            }

            // 2. Fetch fresh catalog from remote repository
            var response = await httpClient.GetAsync(ReplayManagerConstants.DefaultCrcCatalogUrl, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Remote CRC catalog fetch returned status {StatusCode}. Attempting local fallback.", response.StatusCode);
                return await LoadLocalFallbackAsync(cancellationToken);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var catalog = await JsonSerializer.DeserializeAsync<CrcCatalog>(stream, JsonOptions, cancellationToken);

            if (catalog == null || catalog.Mappings.Count == 0)
            {
                logger.LogWarning("Deserialized remote CRC catalog was empty. Attempting local fallback.");
                return await LoadLocalFallbackAsync(cancellationToken);
            }

            // 3. Update in-memory registry atomically
            crcMappingRegistry.LoadCatalog(catalog);

            // 4. Update memory cache with TTL
            await dynamicContentCache.SetAsync(
                ReplayManagerConstants.CrcCatalogCacheKey,
                catalog,
                UpdateCheckInterval,
                cancellationToken);

            // 5. Persist local copy atomically for offline support
            await SaveLocalFallbackAsync(catalog, cancellationToken);

            logger.LogInformation("Successfully updated CRC mapping catalog with {Count} entries", catalog.Mappings.Count);
            return ContentUpdateCheckResult.CreateNoUpdateAvailable(
                catalog.LastUpdated?.ToString("O") ?? "1");
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or JsonException)
        {
            logger.LogError(ex, "Failed to update CRC mapping catalog from remote. Attempting local fallback.");
            return await LoadLocalFallbackAsync(cancellationToken);
        }
    }

    private async Task<ContentUpdateCheckResult> LoadLocalFallbackAsync(CancellationToken cancellationToken)
    {
        try
        {
            var fallbackPath = Path.Combine(
                configurationProviderService.GetApplicationDataPath(),
                ReplayManagerConstants.CrcCatalogLocalFileName);

            if (!File.Exists(fallbackPath))
            {
                return ContentUpdateCheckResult.CreateFailure("No remote or local CRC mapping catalog available.");
            }

            await using var stream = File.OpenRead(fallbackPath);
            var catalog = await JsonSerializer.DeserializeAsync<CrcCatalog>(stream, JsonOptions, cancellationToken);

            if (catalog == null || catalog.Mappings.Count == 0)
            {
                return ContentUpdateCheckResult.CreateFailure("Local fallback CRC catalog is empty or invalid.");
            }

            crcMappingRegistry.LoadCatalog(catalog);
            logger.LogInformation("Loaded {Count} CRC mappings from offline local storage", catalog.Mappings.Count);
            return ContentUpdateCheckResult.CreateNoUpdateAvailable(catalog.LastUpdated?.ToString("O") ?? "local");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            logger.LogError(ex, "Failed to load offline CRC catalog fallback");
            return ContentUpdateCheckResult.CreateFailure($"Failed to load offline CRC catalog: {ex.Message}");
        }
    }

    private async Task SaveLocalFallbackAsync(CrcCatalog catalog, CancellationToken cancellationToken)
    {
        var appDataPath = configurationProviderService.GetApplicationDataPath();
        var tempFilePath = Path.Combine(appDataPath, $"{ReplayManagerConstants.CrcCatalogLocalFileName}.tmp");
        var finalFilePath = Path.Combine(appDataPath, ReplayManagerConstants.CrcCatalogLocalFileName);

        try
        {
            Directory.CreateDirectory(appDataPath);

            await using (var stream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream, catalog, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(tempFilePath, finalFilePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Failed to atomically save offline CRC catalog fallback");
            try
            {
                if (File.Exists(tempFilePath))
                {
                    File.Delete(tempFilePath);
                }
            }
            catch (Exception cleanupEx) when (cleanupEx is IOException or UnauthorizedAccessException)
            {
                logger.LogDebug(cleanupEx, "Failed to clean up temporary CRC catalog file {Path}", tempFilePath);
            }
        }
    }
}
