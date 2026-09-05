using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Content.Services;

/// <summary>
/// Stores remote CSV catalog content for reuse and offline validation.
/// </summary>
public sealed class CsvCatalogCache
{
    /// <summary>
    /// Cached catalog content and whether it remains within the normal refresh interval.
    /// </summary>
    /// <param name="Content">Cached text content.</param>
    /// <param name="IsFresh">Whether the cache entry is fresh enough to use without a network request.</param>
    internal sealed record CsvCatalogCacheEntry(string Content, bool IsFresh);

    private static readonly TimeSpan Freshness = TimeSpan.FromHours(CatalogConstants.DefaultCatalogCacheExpirationHours);
    private static readonly TimeSpan Retention = TimeSpan.FromDays(CsvConstants.CacheRetentionDays);
    private readonly string _cacheDirectory;

    /// <summary>
    /// Initializes a new instance of the <see cref="CsvCatalogCache"/> class.
    /// </summary>
    /// <param name="configurationProvider">Application configuration provider.</param>
    /// <param name="logger">Logger instance.</param>
    public CsvCatalogCache(
        IConfigurationProviderService configurationProvider,
        ILogger<CsvCatalogCache> logger)
    {
        _cacheDirectory = Path.Combine(
            configurationProvider.GetApplicationDataPath(),
            DirectoryNames.Cache,
            CsvConstants.CacheDirectoryName);
        Logger = logger;
        PruneExpiredEntries();
    }

    private ILogger<CsvCatalogCache> Logger { get; }

    /// <summary>
    /// Reads cached content for a remote source when available.
    /// </summary>
    /// <param name="sourceUrl">Remote source URL.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The cached content and its freshness, or <see langword="null"/> when unavailable.</returns>
    internal async Task<CsvCatalogCacheEntry?> ReadAsync(
        string sourceUrl,
        CancellationToken cancellationToken)
    {
        var cachePath = GetCachePath(sourceUrl);
        try
        {
            var fileInfo = new FileInfo(cachePath);
            if (!fileInfo.Exists || fileInfo.Length > CatalogConstants.MaxCatalogSizeBytes)
            {
                return null;
            }

            var content = await File.ReadAllTextAsync(cachePath, cancellationToken);
            var isFresh = DateTime.UtcNow - fileInfo.LastWriteTimeUtc <= Freshness;
            return new CsvCatalogCacheEntry(content, isFresh);
        }
        catch (IOException ex)
        {
            Logger.LogWarning(ex, "Failed to read cached CSV catalog {CachePath}", cachePath);
            return null;
        }
        catch (UnauthorizedAccessException ex)
        {
            Logger.LogWarning(ex, "Access denied reading cached CSV catalog {CachePath}", cachePath);
            return null;
        }
    }

    /// <summary>
    /// Stores content for a remote source using an atomic file replacement.
    /// </summary>
    /// <param name="sourceUrl">Remote source URL.</param>
    /// <param name="content">Downloaded content.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    internal async Task StoreAsync(
        string sourceUrl,
        string content,
        CancellationToken cancellationToken)
    {
        if (Encoding.UTF8.GetByteCount(content) > CatalogConstants.MaxCatalogSizeBytes)
        {
            Logger.LogWarning("CSV catalog from {SourceUrl} exceeds the cache size limit", sourceUrl);
            return;
        }

        var cachePath = GetCachePath(sourceUrl);
        var tempPath = $"{cachePath}.{Guid.NewGuid():N}{CsvConstants.TemporaryCacheFileExtension}";
        try
        {
            Directory.CreateDirectory(_cacheDirectory);
            await File.WriteAllTextAsync(tempPath, content, cancellationToken);
            File.Move(tempPath, cachePath, true);
            PruneExpiredEntries();
        }
        catch (IOException ex)
        {
            Logger.LogWarning(ex, "Failed to cache CSV catalog from {SourceUrl}", sourceUrl);
        }
        catch (UnauthorizedAccessException ex)
        {
            Logger.LogWarning(ex, "Access denied caching CSV catalog from {SourceUrl}", sourceUrl);
        }
        finally
        {
            try
            {
                File.Delete(tempPath);
            }
            catch (IOException ex)
            {
                Logger.LogDebug(ex, "Failed to remove temporary CSV cache file {TempPath}", tempPath);
            }
            catch (UnauthorizedAccessException ex)
            {
                Logger.LogDebug(ex, "Access denied removing temporary CSV cache file {TempPath}", tempPath);
            }
        }
    }

    private string GetCachePath(string sourceUrl)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sourceUrl));
        return Path.Combine(_cacheDirectory, $"{Convert.ToHexString(hash)}{CsvConstants.CacheFileExtension}");
    }

    private void PruneExpiredEntries()
    {
        try
        {
            if (!Directory.Exists(_cacheDirectory))
            {
                return;
            }

            var expirationThreshold = DateTime.UtcNow - Retention;
            foreach (var cacheFile in Directory
                .EnumerateFiles(
                    _cacheDirectory,
                    $"*{CsvConstants.CacheFileExtension}",
                    SearchOption.TopDirectoryOnly)
                .Where(cacheFile => File.GetLastWriteTimeUtc(cacheFile) < expirationThreshold))
            {
                File.Delete(cacheFile);
            }
        }
        catch (IOException ex)
        {
            Logger.LogDebug(ex, "Failed to prune expired CSV catalog cache entries");
        }
        catch (UnauthorizedAccessException ex)
        {
            Logger.LogDebug(ex, "Access denied pruning expired CSV catalog cache entries");
        }
    }
}
