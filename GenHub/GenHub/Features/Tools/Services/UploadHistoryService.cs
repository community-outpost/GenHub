using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Services;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Tools;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

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
    public event EventHandler? UploadHistoryChanged;

    /// <inheritdoc />
    public Task<bool> CanUploadAsync(long fileSizeBytes, string? category = null) =>
        CanUploadAsync(fileSizeBytes, category, CancellationToken.None);

    /// <inheritdoc />
    public async Task<bool> CanUploadAsync(long fileSizeBytes, string? category, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var usage = await GetUsageInfoAsync(category, cancellationToken);
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
        string? category = null,
        GameType? game = null)
    {
        bool recorded = false;
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
                    Game = game,
                });

                SaveHistoryInternal(history);
                _cache = history; // Update cache
                logger.LogInformation("Recorded upload of {Size} bytes for category '{Category}'. Total history: {Count} items.", fileSizeBytes, resolvedCategory, history.Count);
                recorded = true;
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

        if (recorded)
        {
            UploadHistoryChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <inheritdoc />
    public Task<UploadRecord?> FindExistingUploadAsync(string fileHash, string? category = null, GameType? game = null) =>
        FindExistingUploadAsync(fileHash, category, game, CancellationToken.None);

    /// <inheritdoc />
    public Task<UploadRecord?> FindExistingUploadAsync(string fileHash, string? category, GameType? game, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(fileHash))
        {
            return Task.FromResult<UploadRecord?>(null);
        }

        var history = LoadHistoryInternal();
        var existing = history.FirstOrDefault(r => IsReusableUpload(r, fileHash, category, game));

        return Task.FromResult(existing);
    }

    /// <inheritdoc />
    public Task<UsageInfo> GetUsageInfoAsync(string? category = null) =>
        GetUsageInfoAsync(category, CancellationToken.None);

    /// <inheritdoc />
    public Task<UsageInfo> GetUsageInfoAsync(CancellationToken cancellationToken) =>
        GetUsageInfoAsync(null, cancellationToken);

    /// <inheritdoc />
    public Task<UsageInfo> GetUsageInfoAsync(string? category, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
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
    public Task<IReadOnlyList<UploadHistoryItem>> GetUploadHistoryAsync(string? category = null) =>
        GetUploadHistoryAsync(category, CancellationToken.None);

    /// <inheritdoc />
    public Task<IReadOnlyList<UploadHistoryItem>> GetUploadHistoryAsync(CancellationToken cancellationToken) =>
        GetUploadHistoryAsync(null, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<UploadHistoryItem>> GetUploadHistoryAsync(string? category, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var history = LoadHistoryInternal();

        var filtered = history.Where(r => MatchesCategory(r, category));

        var items = filtered.Select(r => new UploadHistoryItem(
            r.Timestamp,
            r.SizeBytes,
            r.Url ?? string.Empty,
            r.FileName ?? "Unknown File",
            r.Category ?? InferCategory(r),
            r.Game)).ToList();

        return Task.FromResult<IReadOnlyList<UploadHistoryItem>>(items);
    }

    /// <inheritdoc />
    public Task<bool> RemoveHistoryItemAsync(string url, bool deleteFromCloud = true) =>
        RemoveHistoryItemAsync(url, deleteFromCloud, CancellationToken.None);

    /// <inheritdoc />
    public Task<bool> RemoveHistoryItemAsync(string url, CancellationToken cancellationToken) =>
        RemoveHistoryItemAsync(url, true, cancellationToken);

    /// <inheritdoc />
    public async Task<bool> RemoveHistoryItemAsync(string url, bool deleteFromCloud, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
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
                var deleteResult = await uploadThingService.DeleteFileAsync(matchingRecord.FileKey, matchingRecord.DeleteToken, cancellationToken);
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

        int removed = 0;
        lock (FileLock)
        {
            var history = LoadHistoryInternal();
            removed = history.RemoveAll(r => r.Url == url);
            if (removed > 0)
            {
                SaveHistoryInternal(history);
                _cache = history;
                logger.LogInformation(
                    "Removed {Count} item(s) for {Url} from upload history.",
                    removed,
                    url);
            }
        }

        if (removed > 0)
        {
            UploadHistoryChanged?.Invoke(this, EventArgs.Empty);
        }

        return true;
    }

    /// <inheritdoc />
    public Task<(int Deleted, int Failed)> ClearHistoryAsync(bool deleteFromCloud = true, string? category = null) =>
        ClearHistoryAsync(deleteFromCloud, category, CancellationToken.None);

    /// <inheritdoc />
    public Task<(int Deleted, int Failed)> ClearHistoryAsync(CancellationToken cancellationToken) =>
        ClearHistoryAsync(true, null, cancellationToken);

    /// <inheritdoc />
    public async Task<(int Deleted, int Failed)> ClearHistoryAsync(bool deleteFromCloud, string? category, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        List<UploadRecord> candidateRecords = [];
        lock (FileLock)
        {
            var history = LoadHistoryInternal();
            candidateRecords = history.Where(r => MatchesCategory(r, category)).ToList();
        }

        var (successfullyDeleted, failedDeletions) = deleteFromCloud
            ? await DeleteRecordsFromCloudAsync(candidateRecords, cancellationToken)
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
            }
        }

        if (removed > 0)
        {
            UploadHistoryChanged?.Invoke(this, EventArgs.Empty);
        }

        return (removed, failedDeletions.Count);
    }

    private static long GetLimitForCategory(string? category)
    {
        if (string.Equals(category, ReplayManagerConstants.UploadCategory, StringComparison.OrdinalIgnoreCase))
        {
            return ReplayManagerConstants.MaxUploadBytesPerPeriod;
        }

        if (string.Equals(category, ProfileSharingConstants.UploadCategoryProfiles, StringComparison.OrdinalIgnoreCase))
        {
            return ProfileSharingConstants.MaxUploadBytesPerPeriod;
        }

        return MapManagerConstants.MaxUploadBytesPerPeriod;
    }

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

    private static bool IsReusableUpload(UploadRecord record, string fileHash, string? category, GameType? game)
    {
        return !string.IsNullOrEmpty(record.FileHash) &&
            string.Equals(record.FileHash, fileHash, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrEmpty(record.Url) &&
            (category == null || MatchesCategory(record, category)) &&
            (!game.HasValue || record.Game == game);
    }

    private async Task<(HashSet<UploadRecord> Succeeded, HashSet<UploadRecord> Failed)> DeleteRecordsFromCloudAsync(IEnumerable<UploadRecord> records, CancellationToken cancellationToken = default)
    {
        var successfullyDeleted = new HashSet<UploadRecord>();
        var failedDeletions = new HashSet<UploadRecord>();

        foreach (var record in records)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (record.FileKey is not { Length: > 0 } fileKey || record.DeleteToken is not { Length: > 0 } deleteToken)
            {
                successfullyDeleted.Add(record);
                continue;
            }

            try
            {
                var deleteResult = await uploadThingService.DeleteFileAsync(fileKey, deleteToken, cancellationToken);
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
