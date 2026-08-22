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
/// <param name="uploadThingService">UploadThing cloud storage service.</param>
/// <param name="logger">Logger instance.</param>
/// <param name="appConfig">Application configuration service.</param>
public sealed class UploadHistoryService(
    IUploadThingService uploadThingService,
    ILogger<UploadHistoryService> logger,
    IAppConfiguration appConfig) : IUploadHistoryService
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

    private readonly string _historyFilePath = Path.Combine(appConfig.GetConfiguredDataPath(), "upload_history.json");
    private List<UploadRecord>? _cache;

    /// <inheritdoc />
    public long MaxUploadBytesPerPeriod => MapManagerConstants.MaxUploadBytesPerPeriod;

    /// <inheritdoc />
    public async Task<bool> CanUploadAsync(long fileSizeBytes)
    {
        var usage = await GetUsageInfoAsync();
        return usage.UsedBytes + fileSizeBytes <= usage.LimitBytes;
    }

    /// <inheritdoc />
    public void RecordUpload(long fileSizeBytes, string url, string fileName, string? fileKey = null, string? deleteToken = null, string? fileHash = null)
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
                    FileKey = fileKey,
                    DeleteToken = deleteToken,
                    FileHash = fileHash,
                });

                SaveHistoryInternal(history);
                _cache = history; // Update cache
                logger.LogInformation("Recorded upload of {Size} bytes. Total history: {Count} items.", fileSizeBytes, history.Count);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to record upload");
            }
        }
    }

    /// <inheritdoc />
    public Task<UploadRecord?> FindExistingUploadAsync(string fileHash)
    {
        if (string.IsNullOrWhiteSpace(fileHash))
        {
            return Task.FromResult<UploadRecord?>(null);
        }

        var history = LoadHistoryInternal();
        var existing = history.FirstOrDefault(r =>
            !string.IsNullOrEmpty(r.FileHash) &&
            string.Equals(r.FileHash, fileHash, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrEmpty(r.Url));

        return Task.FromResult(existing);
    }

    /// <inheritdoc />
    public Task<UsageInfo> GetUsageInfoAsync()
    {
        var history = LoadHistoryInternal();
        var periodStart = DateTime.UtcNow.AddDays(-RateLimitDays);

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

        var items = history.Select(r => new UploadHistoryItem(
            r.Timestamp,
            r.SizeBytes,
            r.Url ?? string.Empty,
            r.FileName ?? "Unknown File"));

        return Task.FromResult(items);
    }

    /// <inheritdoc />
    public async Task<bool> RemoveHistoryItemAsync(string url, bool deleteFromCloud = false)
    {
        UploadRecord? matchingRecord = null;
        lock (FileLock)
        {
            var history = LoadHistoryInternal();
            matchingRecord = history.FirstOrDefault(r => r.Url == url);
        }

        if (matchingRecord == null)
        {
            return true;
        }

        if (deleteFromCloud && !string.IsNullOrEmpty(matchingRecord.FileKey) && !string.IsNullOrEmpty(matchingRecord.DeleteToken))
        {
            try
            {
                var cloudDeleted = await uploadThingService.DeleteFileAsync(matchingRecord.FileKey, matchingRecord.DeleteToken);
                if (!cloudDeleted)
                {
                    logger.LogWarning(
                        "Failed to delete file {Key} from cloud storage for {Url}. Preserving local history item for retry.",
                        matchingRecord.FileKey,
                        url);
                    return false;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Exception occurred while deleting file from cloud storage for {Url}", url);
                return false;
            }
        }

        lock (FileLock)
        {
            var history = LoadHistoryInternal();
            var removed = history.RemoveAll(r => r.Url == url);
            if (removed > 0)
            {
                SaveHistoryInternal(history);
                _cache = history;
                logger.LogInformation(
                    "Removed {Count} item(s) for {Url} from local upload history.",
                    removed,
                    url);
            }
        }

        return true;
    }

    /// <inheritdoc />
    public Task ClearHistoryAsync()
    {
        lock (FileLock)
        {
            var history = LoadHistoryInternal();
            if (history.Count > 0)
            {
                history.Clear();
                SaveHistoryInternal(history);
                _cache = history;
                logger.LogInformation("Cleared local upload history without deleting hosted files.");
            }
        }

        return Task.CompletedTask;
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
                var hasPendingDeletionRecords = history.Any(r => r.IsPendingDeletion);

                var migratedHistory = history
                    .Where(r => !r.IsPendingDeletion && r.Timestamp >= retentionCutoff)
                    .OrderByDescending(r => r.Timestamp)
                    .ToList();
                _cache = migratedHistory;

                if (hasPendingDeletionRecords)
                {
                    SaveHistoryInternal(migratedHistory);
                }

                return new List<UploadRecord>(migratedHistory);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to load upload history.");
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
                logger.LogError(ex, "Failed to save upload history");
            }
        }
    }
}
