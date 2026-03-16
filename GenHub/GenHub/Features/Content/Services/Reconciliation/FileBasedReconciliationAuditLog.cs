using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Models.Content;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Content.Services.Reconciliation;

/// <summary>
/// File-based implementation of reconciliation audit logging.
/// Stores entries in rolling JSON files organized by date.
/// </summary>
public class FileBasedReconciliationAuditLog : IReconciliationAuditLog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _auditDirectory;
    private readonly ILogger<FileBasedReconciliationAuditLog> _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="FileBasedReconciliationAuditLog"/> class.
    /// </summary>
    /// <param name="applicationDataPath">The application data directory path.</param>
    /// <param name="logger">The logger instance.</param>
    public FileBasedReconciliationAuditLog(
        string applicationDataPath,
        ILogger<FileBasedReconciliationAuditLog> logger)
    {
        _auditDirectory = Path.Combine(applicationDataPath, "audit", "reconciliation");
        _logger = logger;
        Directory.CreateDirectory(_auditDirectory);
    }

    /// <inheritdoc/>
    public async Task LogOperationAsync(ReconciliationAuditEntry entry, CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var fileName = GetAuditFileName(entry.Timestamp);
            var filePath = Path.Combine(_auditDirectory, fileName);

            var entries = await LoadEntriesFromFileAsync(filePath, cancellationToken);
            entries.Add(entry);

            var json = JsonSerializer.Serialize(entries, JsonOptions);
            await File.WriteAllTextAsync(filePath, json, cancellationToken);

            _logger.LogDebug("Logged audit entry: {OperationId} ({Type})", entry.OperationId, entry.OperationType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to log audit entry: {OperationId}", entry.OperationId);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ReconciliationAuditEntry>> GetRecentHistoryAsync(
        int count = ReconciliationConstants.DefaultMaxAuditHistoryEntries,
        CancellationToken cancellationToken = default)
    {
        var allEntries = new List<ReconciliationAuditEntry>();

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var files = Directory.GetFiles(_auditDirectory, "audit-*.json")
                .OrderByDescending(f => f)
                .Take(ReconciliationConstants.DefaultAuditLookbackDays);

            foreach (var file in files)
            {
                var entries = await LoadEntriesFromFileAsync(file, cancellationToken);
                allEntries.AddRange(entries);

                if (allEntries.Count >= count)
                {
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get recent audit history");
        }
        finally
        {
            _writeLock.Release();
        }

        return allEntries
            .OrderByDescending(e => e.Timestamp)
            .Take(count)
            .ToList();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ReconciliationAuditEntry>> GetProfileHistoryAsync(
        string profileId,
        int count = ReconciliationConstants.DefaultMaxProfileHistoryEntries,
        CancellationToken cancellationToken = default)
    {
        var recent = await GetRecentHistoryAsync(ReconciliationConstants.MaxFilterEntries, cancellationToken);
        return recent
            .Where(e => e.AffectedProfileIds.Contains(profileId, StringComparer.OrdinalIgnoreCase))
            .Take(count)
            .ToList();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ReconciliationAuditEntry>> GetManifestHistoryAsync(
        string manifestId,
        int count = ReconciliationConstants.DefaultMaxManifestHistoryEntries,
        CancellationToken cancellationToken = default)
    {
        var recent = await GetRecentHistoryAsync(ReconciliationConstants.MaxFilterEntries, cancellationToken);
        return recent
            .Where(e => e.AffectedManifestIds.Contains(manifestId, StringComparer.OrdinalIgnoreCase) ||
                       (e.ManifestMapping?.ContainsKey(manifestId) == true) ||
                       (e.ManifestMapping?.Values.Contains(manifestId, StringComparer.OrdinalIgnoreCase) == true))
            .Take(count)
            .ToList();
    }

    /// <inheritdoc/>
    public async Task<int> PurgeOldEntriesAsync(int retentionDays = ReconciliationConstants.DefaultAuditRetentionDays, CancellationToken cancellationToken = default)
    {
        var cutoffDate = DateTime.UtcNow.AddDays(-retentionDays);
        var cutoffFileName = $"audit-{cutoffDate:yyyy-MM-dd}.json";
        int deletedCount = 0;

        try
        {
            var files = Directory.GetFiles(_auditDirectory, "audit-*.json")
                .Where(f => string.Compare(Path.GetFileName(f), cutoffFileName, StringComparison.Ordinal) < 0);

            foreach (var file in files)
            {
                try
                {
                    File.Delete(file);
                    deletedCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete old audit file: {File}", file);
                }
            }

            _logger.LogInformation("Purged {Count} old audit files", deletedCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to purge old audit entries");
        }

        return await Task.FromResult(deletedCount);
    }

    private static string GetAuditFileName(DateTime timestamp)
    {
        return $"audit-{timestamp:yyyy-MM-dd}.json";
    }

    private async Task<List<ReconciliationAuditEntry>> LoadEntriesFromFileAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
        {
            return [];
        }

        try
        {
            var json = await File.ReadAllTextAsync(filePath, cancellationToken);
            return JsonSerializer.Deserialize<List<ReconciliationAuditEntry>>(json, JsonOptions) ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load audit entries from {File}", filePath);
            return [];
        }
    }
}
