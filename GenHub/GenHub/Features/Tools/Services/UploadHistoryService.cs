using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Services;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Tools;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Tools.Services;

/// <summary>
/// Implementation of <see cref="IUploadHistoryService"/> for tracking upload quotas.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="UploadHistoryService"/> class.
/// </remarks>
/// <param name="logger">Logger instance.</param>
/// <param name="appConfig">Application configuration service.</param>
/// <param name="uploadThing">UploadThing service.</param>
public sealed class UploadHistoryService(
    ILogger<UploadHistoryService> logger,
    IAppConfiguration appConfig,
    IUploadThingService uploadThing) : IUploadHistoryService
{
    private const int RateLimitDays = 3;
    private const int HistoryRetentionDays = 30;

    private static readonly object FileLock = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly ILogger<UploadHistoryService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly string _historyFilePath = Path.Combine(appConfig.GetConfiguredDataPath(), "upload_history.json");

    // Trigger cleanup on service instantiation (fire-and-forget)
    private readonly Task _cleanupTask = Task.Run(async () => await ProcessPendingDeletionsAsync());
    private List<UploadRecord>? _cache;

    private static Task ProcessPendingDeletionsAsync()
    {
        return Task.CompletedTask;
    }

    private static string? ExtractKeyFromUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        try
        {
            var uri = new Uri(url);
            return uri.Segments.Last();
        }
        catch
        {
            // Fallback for simple string logic if Uri fails
            var lastSlash = url.LastIndexOf('/');
            return lastSlash >= 0 && lastSlash < url.Length - 1 ? url[(lastSlash + 1)..] : null;
        }
    }

    /// <inheritdoc />
    public long MaxUploadBytesPerPeriod => MapManagerConstants.MaxUploadBytesPerPeriod;

    /// <inheritdoc />
    public async Task<bool> CanUploadAsync(long fileSizeBytes)
    {
        var usage = await GetUsageInfoAsync();
        return usage.UsedBytes + fileSizeBytes <= usage.LimitBytes;
    }

    /// <inheritdoc />
    public void RecordUpload(long fileSizeBytes, string url, string fileName)
    {
        lock (FileLock)
        {
            try
            {
                var history = LoadHistoryInternal();
                history.Add(new UploadRecord
                {
                    Timestamp = DateTime.UtcNow,
                    SizeBytes = fileSizeBytes,
                    Url = url,
                    FileName = fileName,
                });

                SaveHistoryInternal(history);
                _cache = history; // Update cache
                _logger.LogInformation("Recorded upload of {Size} bytes. Total history: {Count} items.", fileSizeBytes, history.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to record upload");
            }
        }
    }

    /// <inheritdoc />
    public Task<UsageInfo> GetUsageInfoAsync()
    {
        var history = LoadHistoryInternal();
        var periodStart = DateTime.UtcNow.AddDays(-RateLimitDays);

        // Include items even if pending deletion, as they still occupy quota until confirmed deleted
        var recentUploads = history.Where(r => r.Timestamp >= periodStart).ToList();
        var usedBytes = recentUploads.Sum(r => r.SizeBytes);

        // Reset date is when the oldest upload in the current window expires
        var oldestInWindow = recentUploads.OrderBy(r => r.Timestamp).FirstOrDefault();
        var resetDate = oldestInWindow != null
            ? oldestInWindow.Timestamp.AddDays(RateLimitDays)
            : DateTime.UtcNow;

        return Task.FromResult(new UsageInfo(usedBytes, MaxUploadBytesPerPeriod, resetDate));
    }

    /// <inheritdoc />
    public Task<IEnumerable<UploadHistoryItem>> GetUploadHistoryAsync()
    {
        var history = LoadHistoryInternal();

        // Return history, EXCLUDING pending deletions so user sees effective state
        var items = history
            .Where(r => !r.IsPendingDeletion)
            .Select(r => new UploadHistoryItem(
                r.Timestamp,
                r.SizeBytes,
                r.Url ?? string.Empty,
                r.FileName ?? "Unknown File"));

        return Task.FromResult(items);
    }

    /// <inheritdoc />
    public async Task RemoveHistoryItemAsync(string url)
    {
        List<UploadRecord> history;
        lock (FileLock)
        {
            history = LoadHistoryInternal();
            var item = history.FirstOrDefault(r => r.Url == url);
            if (item != null && !item.IsPendingDeletion)
            {
                item.IsPendingDeletion = true; // Mark as pending
                SaveHistoryInternal(history);  // Persist state
                _cache = history;
            }
        }

        // Attempt immediate deletion
        await TryDeleteUrlAsync(url);
    }

    /// <inheritdoc />
    public async Task ClearHistoryAsync()
    {
        List<UploadRecord> history;
        lock (FileLock)
        {
            history = LoadHistoryInternal();
            if (history.Count == 0 || history.All(x => x.IsPendingDeletion)) return;

            foreach (var item in history)
            {
                item.IsPendingDeletion = true;
            }

            SaveHistoryInternal(history);
            _cache = history;
        }

        // Attempt deletion of all pending items
        await ProcessPendingDeletionsAsync();
    }

    private async Task TryDeleteUrlAsync(string url)
    {
        var key = ExtractKeyFromUrl(url);
        if (string.IsNullOrEmpty(key))
        {
            _logger.LogError("Could not extract file key from URL: {Url}", url);

            // Even if invalid, we might want to just remove it from history?
            // For now, keep it pending to avoid quota exploit via malformed URLs if that were possible.
            // But practically, if we can't delete it, it's stuck. Let's remove it if invalid format.
            RemoveFromHistoryPermanent(url);
            return;
        }

        var success = await uploadThing.DeleteFileAsync(key);
        if (success)
        {
            RemoveFromHistoryPermanent(url);
            _logger.LogInformation("Successfully deleted and removed history for: {Url}", url);
        }
        else
        {
            _logger.LogWarning("Failed to delete {Url}. Item remains in Pending Deletion state.", url);
        }
    }

    private void RemoveFromHistoryPermanent(string url)
    {
        lock (FileLock)
        {
             var history = LoadHistoryInternal();
             var removed = history.RemoveAll(r => r.Url == url);
             if (removed > 0)
             {
                 SaveHistoryInternal(history);
                 _cache = history;
             }
        }
    }

    private async Task RunCleanupAsync()
    {
        List<UploadRecord> snapshot;
        lock (FileLock)
        {
             var history = LoadHistoryInternal();
             snapshot = history.Where(x => x.IsPendingDeletion).ToList();
        }

        foreach (var item in snapshot)
        {
            if (item.Url != null)
            {
                await TryDeleteUrlAsync(item.Url);
            }
        }
    }

    private List<UploadRecord> LoadHistoryInternal()
    {
        lock (FileLock)
        {
            if (_cache != null)
            {
                return new List<UploadRecord>(_cache);
            }

            try
            {
                if (!File.Exists(_historyFilePath))
                {
                    _cache = new List<UploadRecord>();
                    return new List<UploadRecord>();
                }

                var json = File.ReadAllText(_historyFilePath);
                if (string.IsNullOrWhiteSpace(json))
                {
                    _cache = new List<UploadRecord>();
                    return new List<UploadRecord>();
                }

                var history = JsonSerializer.Deserialize<List<UploadRecord>>(json, JsonOptions) ?? new List<UploadRecord>();

                // Clean up old entries (expired retention)
                var retentionCutoff = DateTime.UtcNow.AddDays(-HistoryRetentionDays);

                // Also remove items that were pending deletion and are very old? No, we keep trying.
                // Filter logic: Keep if New enough OR (IsPendingDeletion AND New enough?)
                // If it's pending deletion and 30 days old, maybe just give up?
                // Let's stick to standard retention. If it's old, it falls off history anyway.
                _cache = history.Where(r => r.Timestamp >= retentionCutoff).OrderByDescending(r => r.Timestamp).ToList();

                return new List<UploadRecord>(_cache);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load upload history.");
                return [];
            }
        }
    }

    private void SaveHistoryInternal(List<UploadRecord> history)
    {
        lock (FileLock)
        {
            try
            {
                var directory = Path.GetDirectoryName(_historyFilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var json = JsonSerializer.Serialize(history, JsonOptions);
                File.WriteAllText(_historyFilePath, json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save upload history");
            }
        }
    }
}