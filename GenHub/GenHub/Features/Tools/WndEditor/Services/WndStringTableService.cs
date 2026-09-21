using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Tools.WndEditor;
using GenHub.Core.Models.Results;
using GenHub.Core.Services.Tools.Checksum;
using GenHub.Core.Services.Tools.GenHotkeys;
using GenHub.Core.Services.Tools.WndEditor;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.WndEditor.Services;

/// <summary>
/// Resolves TEXT string-table labels to localized values from game string tables.
/// </summary>
public sealed class WndStringTableService(ILogger<WndStringTableService> logger) : IWndStringTableService
{
    private const int MaxCachedTables = 8;

    private readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, string>> _tables = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _tableLock = new(1, 1);

    /// <inheritdoc />
    public void InvalidateCache()
    {
        _tables.Clear();
        logger.LogDebug("Invalidated string table caches");
    }

    /// <inheritdoc />
    public async Task<OperationResult<IReadOnlyDictionary<string, string>>> GetStringsAsync(
        IReadOnlyCollection<string> labels,
        string baseRoot,
        string? overrideRoot,
        string? projectDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(labels);
        ArgumentNullException.ThrowIfNull(baseRoot);
        var stopwatch = Stopwatch.StartNew();
        var empty = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (labels.Count == 0)
        {
            return OperationResult<IReadOnlyDictionary<string, string>>.CreateSuccess(empty, stopwatch.Elapsed);
        }

        if (!Directory.Exists(baseRoot))
        {
            logger.LogDebug("Game root {Root} does not exist; skipping string tables", baseRoot);
            return OperationResult<IReadOnlyDictionary<string, string>>.CreateFailure(
                $"Game root directory was not found: {baseRoot}",
                empty,
                stopwatch.Elapsed);
        }

        try
        {
            var table = await GetOrLoadTableAsync(baseRoot, overrideRoot, projectDirectory, cancellationToken).ConfigureAwait(false);
            var resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var label in labels)
            {
                if (!string.IsNullOrWhiteSpace(label) && table.TryGetValue(label.Trim(), out var value))
                {
                    resolved[label.Trim()] = value;
                }
            }

            return OperationResult<IReadOnlyDictionary<string, string>>.CreateSuccess(resolved, stopwatch.Elapsed);
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Failed to load string tables from {Root}", baseRoot);
            return OperationResult<IReadOnlyDictionary<string, string>>.CreateFailure(
                $"Failed to load string tables: {ex.Message}",
                empty,
                stopwatch.Elapsed);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Access denied loading string tables from {Root}", baseRoot);
            return OperationResult<IReadOnlyDictionary<string, string>>.CreateFailure(
                $"Access denied loading string tables: {ex.Message}",
                empty,
                stopwatch.Elapsed);
        }
    }

    private static string TableKey(string baseRoot, string? overrideRoot, string? projectDirectory)
    {
        return string.Concat(baseRoot, "|", overrideRoot ?? string.Empty, "|", projectDirectory ?? string.Empty);
    }

    private async Task<IReadOnlyDictionary<string, string>> GetOrLoadTableAsync(
        string baseRoot,
        string? overrideRoot,
        string? projectDirectory,
        CancellationToken cancellationToken)
    {
        var key = TableKey(baseRoot, overrideRoot, projectDirectory);
        if (_tables.TryGetValue(key, out var cached))
        {
            return cached;
        }

        await _tableLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_tables.TryGetValue(key, out cached))
            {
                return cached;
            }

            var loaded = await Task.Run(() => LoadTable(key, baseRoot, overrideRoot, projectDirectory, cancellationToken), cancellationToken).ConfigureAwait(false);
            if (_tables.Count >= MaxCachedTables)
            {
                _tables.Clear();
            }

            _tables[key] = loaded;
            return loaded;
        }
        finally
        {
            _tableLock.Release();
        }
    }

    private IReadOnlyDictionary<string, string> LoadTable(
        string key,
        string baseRoot,
        string? overrideRoot,
        string? projectDirectory,
        CancellationToken cancellationToken)
    {
        _ = key;
        var fileSystem = WndGameFileSystem.Open(baseRoot, overrideRoot, projectDirectory, logger, cancellationToken);
        foreach (var language in WndConstants.StringTables.Languages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = string.Concat(
                WndConstants.StringTables.DataDirectory,
                "\\",
                language,
                "\\",
                WndConstants.StringTables.FileName);
            var bytes = TryRead(fileSystem, path);
            if (bytes == null)
            {
                continue;
            }

            try
            {
                using var stream = new MemoryStream(bytes);
                var table = CsfFile.Load(stream);
                logger.LogInformation("Loaded {Count} strings from {Path}", table.Count, path);
                return table.Strings;
            }
            catch (InvalidDataException ex)
            {
                logger.LogDebug(ex, "Ignoring invalid string table {Path}", path);
            }
            catch (EndOfStreamException ex)
            {
                logger.LogDebug(ex, "Ignoring truncated string table {Path}", path);
            }
        }

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    private byte[]? TryRead(SageVirtualFileSystem fileSystem, string path)
    {
        try
        {
            return fileSystem.Read(path);
        }
        catch (IOException ex)
        {
            logger.LogDebug(ex, "Failed to read string table {Path}", path);
            return null;
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogDebug(ex, "Access denied reading string table {Path}", path);
            return null;
        }
    }
}
