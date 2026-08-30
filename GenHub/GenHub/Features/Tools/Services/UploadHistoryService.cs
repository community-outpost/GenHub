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
    public event EventHandler? UploadHistoryChanged;

    /// <inheritdoc />
    public long MaxUploadBytesPerPeriod => ReplayManagerConstants.MaxUploadBytesPerPeriod;

    /// <inheritdoc />
    public async Task<bool> CanUploadAsync(long fileSizeBytes, string? category = null)
    {
        var usage = await GetUsageInfoAsync(category);
        return usage.UsedBytes + fileSizeBytes <= usage.LimitBytes;
    }

    /// <inheritdoc />
    public void RecordUpload(
        long fileSizeBytes,
        string url,
        string fileName,
        string? fileKey = null,
        string? deleteToken = null,
        string? fileHash = null,
        string? category = null)
    {
        lock (FileLock)
        {
            try
            {
                var history = LoadHistoryInternal();
                var resolvedCategory = string.IsNullOrEmpty(category) ? InferCategory(fileName) : category;

                history.Add(new UploadRecord
                {
                    Timestamp = DateTime.UtcNow,
                    SizeBytes = fileSizeBytes,
                    Url = url,
                    FileName = fileName,
                    FileKey = fileKey,
                    DeleteToken = deleteToken,
                    FileHash = fileHash,
                    Category = resolvedCategory,
                });

                SaveHistoryInternal(history);
                _cache = history; // Update cache
                logger.LogInformation("Recorded upload of {Size} bytes for category '{Category}'. Total history: {Count} items.", fileSizeBytes, resolvedCategory, history.Count);
                UploadHistoryChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (IOException ex)
            {
                logger.LogError(ex, "Failed to record upload");
            }
            catch (UnauthorizedAccessException ex)
            {
                logger.LogError(ex, "Failed to record upload");
            }
            catch (JsonException ex)
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
    public Task<UsageInfo> GetUsageInfoAsync(string? category = null)
    {
        var history = LoadHistoryInternal();
        var periodStart = DateTime.UtcNow.AddDays(-RateLimitDays);

        var recentUploads = history
            .Where(r => r.Timestamp >= periodStart && MatchesCategory(r, category))
            .ToList();
        var usedBytes = recentUploads.Sum(r => r.SizeBytes);
        var limitBytes = GetLimitForCategory(category);

        // Reset date is when the oldest upload in the current window expires
        var oldestInWindow = recentUploads.OrderBy(r => r.Timestamp).FirstOrDefault();
        var resetDate = oldestInWindow != null
            ? oldestInWindow.Timestamp.AddDays(RateLimitDays)
            : DateTime.UtcNow;

        return Task.FromResult(new UsageInfo(usedBytes, limitBytes, resetDate));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<UploadHistoryItem>> GetUploadHistoryAsync(string? category = null)
    {
        var history = LoadHistoryInternal();

        var filtered = history.Where(r => MatchesCategory(r, category));

        var items = filtered.Select(r => new UploadHistoryItem(
            r.Timestamp,
            r.SizeBytes,
            r.Url ?? string.Empty,
            r.FileName ?? "Unknown File",
            r.Category ?? InferCategory(r))).ToList();

        return Task.FromResult<IReadOnlyList<UploadHistoryItem>>(items);
    }

    /// <inheritdoc />
    public async Task<bool> RemoveHistoryItemAsync(string url, bool deleteFromCloud = true)
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
                var deleteResult = await uploadThingService.DeleteFileAsync(matchingRecord.FileKey, matchingRecord.DeleteToken);
                if (!deleteResult.Success || !deleteResult.Data)
                {
                    logger.LogWarning(
                        "Failed to delete file {Key} from cloud storage for {Url}. Preserving local history item for retry.",
                        matchingRecord.FileKey,
                        url);
                    return false;
                }
            }
            catch (OperationCanceledException ex)
            {
                logger.LogWarning(ex, "Timeout or cancellation occurred while deleting file from cloud storage for {Url}", url);
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
                    "Removed {Count} item(s) for {Url} from upload history.",
                    removed,
                    url);
                UploadHistoryChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        return true;
    }

    /// <inheritdoc />
    public async Task<(int Deleted, int Failed)> ClearHistoryAsync(bool deleteFromCloud = true, string? category = null)
    {
        List<UploadRecord> candidateRecords = [];
        lock (FileLock)
        {
            var history = LoadHistoryInternal();
            candidateRecords = history.Where(r => MatchesCategory(r, category)).ToList();
        }

        var (successfullyDeleted, failedDeletions) = deleteFromCloud
            ? await DeleteRecordsFromCloudAsync(candidateRecords)
            : (candidateRecords.ToHashSet(), new HashSet<UploadRecord>());

        int removed = 0;
        lock (FileLock)
        {
            var history = LoadHistoryInternal();
            var targetUrls = successfullyDeleted.Select(r => r.Url).Where(u => !string.IsNullOrEmpty(u)).OfType<string>().ToHashSet();
            removed = history.RemoveAll(r => (r.Url != null && targetUrls.Contains(r.Url)) || successfullyDeleted.Contains(r));
            if (removed > 0)
            {
                SaveHistoryInternal(history);
                _cache = history;
                logger.LogInformation("Cleared {RemovedCount} upload history items for category '{Category}'. Failed cloud deletions: {FailedCount}.", removed, category ?? "all", failedDeletions.Count);
                UploadHistoryChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        return (removed, failedDeletions.Count);
    }

    private static long GetLimitForCategory(string? category) =>
        ReplayManagerConstants.MaxUploadBytesPerPeriod;

    private static string InferCategory(string? fileName)
    {
        if (string.IsNullOrEmpty(fileName))
        {
            return MapManagerConstants.UploadCategory;
        }

        if (fileName.EndsWith(".rep", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals($"{ReplayManagerConstants.DefaultZipName}{Path.GetExtension(ReplayManagerConstants.ZipFilePattern)}", StringComparison.OrdinalIgnoreCase))
        {
            return ReplayManagerConstants.UploadCategory;
        }

        return MapManagerConstants.UploadCategory;
    }

    private static string InferCategory(UploadRecord record)
    {
        if (!string.IsNullOrEmpty(record.Category))
        {
            return record.Category;
        }

        return InferCategory(record.FileName);
    }

    private static bool MatchesCategory(UploadRecord record, string? category)
    {
        if (string.IsNullOrEmpty(category))
        {
            return true;
        }

        var inferred = InferCategory(record);
        return string.Equals(inferred, category, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<(HashSet<UploadRecord> Succeeded, HashSet<UploadRecord> Failed)> DeleteRecordsFromCloudAsync(IEnumerable<UploadRecord> records)
    {
        var successfullyDeleted = new HashSet<UploadRecord>();
        var failedDeletions = new HashSet<UploadRecord>();

        foreach (var record in records)
        {
            if (record.FileKey is not { Length: > 0 } fileKey || record.DeleteToken is not { Length: > 0 } deleteToken)
            {
                successfullyDeleted.Add(record);
                continue;
            }

            try
            {
                var deleteResult = await uploadThingService.DeleteFileAsync(fileKey, deleteToken);
                if (deleteResult.Success && deleteResult.Data)
                {
                    successfullyDeleted.Add(record);
                }
                else
                {
                    failedDeletions.Add(record);
                    logger.LogWarning(
                        "Failed to delete file {Key} from cloud storage during clear history.",
                        fileKey);
                }
            }
            catch (OperationCanceledException ex)
            {
                failedDeletions.Add(record);
                logger.LogWarning(ex, "Timeout or cancellation occurred while deleting file {Key} from cloud during clear history", fileKey);
            }
        }

        return (successfullyDeleted, failedDeletions);
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
                    _cache = [];
                    return [];
                }

                var json = File.ReadAllText(_historyFilePath);
                if (string.IsNullOrWhiteSpace(json))
                {
                    _cache = [];
                    return [];
                }

                var history = JsonSerializer.Deserialize<List<UploadRecord>>(json, JsonOptions) ?? [];

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
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                logger.LogError(ex, "Failed to load upload history.");

                // If loading from disk failed, don't overwrite with an empty cache if we had one
                if (_cache != null)
                {
                    return new List<UploadRecord>(_cache);
                }

                // Quarantine unparseable file to avoid data loss on future writes
                QuarantineCorruptHistoryFile();

                _cache = [];
                return [];
            }
        }
    }

    private void QuarantineCorruptHistoryFile()
    {
        try
        {
            if (File.Exists(_historyFilePath))
            {
                var backupPath = $"{_historyFilePath}.corrupt.{DateTime.UtcNow:yyyyMMddHHmmss}.bak";
                File.Copy(_historyFilePath, backupPath, overwrite: true);
                logger.LogWarning("Quarantined corrupt upload history file to {Path}", backupPath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Failed to quarantine corrupt upload history file.");
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
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                logger.LogError(ex, "Failed to save upload history");
            }
        }
    }
}
