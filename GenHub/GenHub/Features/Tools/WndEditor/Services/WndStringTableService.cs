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
using System.Text;
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
        _tableLock.Wait();
        try
        {
            _tables.Clear();
        }
        finally
        {
            _tableLock.Release();
        }

        logger.LogDebug("Invalidated string table caches");
    }

    /// <inheritdoc />
    public async Task<OperationResult<IReadOnlyDictionary<string, string>>> GetStringsAsync(
        IReadOnlyCollection<string> labels,
        string baseRoot,
        string? overrideRoot,
        string? projectDirectory,
        IReadOnlyCollection<string>? additionalBigFiles = null,
        bool isZeroHour = false,
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
            var table = await GetOrLoadTableAsync(baseRoot, overrideRoot, projectDirectory, additionalBigFiles, isZeroHour, cancellationToken).ConfigureAwait(false);
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

    /// <summary>
    /// Parses a SAGE .str plain-text string table and inserts entries into the target dictionary.
    /// </summary>
    /// <param name="strText">The plain text content of the .str file.</param>
    /// <param name="target">The target dictionary into which to insert string mappings.</param>
    internal static void ParseStrFile(string strText, Dictionary<string, string> target)
    {
        if (string.IsNullOrWhiteSpace(strText))
        {
            return;
        }

        var lines = strText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        string? currentLabel = null;
        var valueBuilder = new StringBuilder();
        var readingValue = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith("//", StringComparison.Ordinal) || line.StartsWith(';'))
            {
                continue;
            }

            if (!readingValue)
            {
                if (line.Equals("End", StringComparison.OrdinalIgnoreCase))
                {
                    currentLabel = null;
                    continue;
                }

                if (TryParseSingleLineEntry(line, target))
                {
                    continue;
                }

                currentLabel = line;
                readingValue = true;
                valueBuilder.Clear();
            }
            else if (line.Equals("End", StringComparison.OrdinalIgnoreCase))
            {
                CompleteValueEntry(ref currentLabel, valueBuilder, target);
                readingValue = false;
            }
            else
            {
                AppendValueLine(valueBuilder, line);
            }
        }
    }

    private static bool TryParseSingleLineEntry(string line, Dictionary<string, string> target)
    {
        // Check for single-line format: LABEL "Value"
        var firstQuote = line.IndexOf('"');
        var lastQuote = line.LastIndexOf('"');
        if (firstQuote <= 0 || lastQuote <= firstQuote)
        {
            return false;
        }

        var label = line[..firstQuote].Trim();
        var val = line[(firstQuote + 1)..lastQuote];
        target[label] = val;
        return true;
    }

    private static void CompleteValueEntry(ref string? currentLabel, StringBuilder valueBuilder, Dictionary<string, string> target)
    {
        if (!string.IsNullOrEmpty(currentLabel))
        {
            var val = valueBuilder.ToString().Trim();
            if (val.Length >= 2 && val.StartsWith('"') && val.EndsWith('"'))
            {
                val = val[1..^1];
            }

            target[currentLabel] = val;
        }

        currentLabel = null;
    }

    private static void AppendValueLine(StringBuilder valueBuilder, string line)
    {
        if (valueBuilder.Length > 0)
        {
            valueBuilder.Append('\n');
        }

        valueBuilder.Append(line);
    }

    private static string TableKey(
        string baseRoot,
        string? overrideRoot,
        string? projectDirectory,
        IReadOnlyCollection<string>? additionalBigFiles = null,
        bool isZeroHour = false)
    {
        return WndGameFileSystem.BuildAssetCacheKey(baseRoot, overrideRoot, projectDirectory, additionalBigFiles, isZeroHour);
    }

    private async Task<IReadOnlyDictionary<string, string>> GetOrLoadTableAsync(
        string baseRoot,
        string? overrideRoot,
        string? projectDirectory,
        IReadOnlyCollection<string>? additionalBigFiles,
        bool isZeroHour,
        CancellationToken cancellationToken)
    {
        var key = TableKey(baseRoot, overrideRoot, projectDirectory, additionalBigFiles, isZeroHour);
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

            var loaded = await Task.Run(() => LoadTable(baseRoot, overrideRoot, projectDirectory, additionalBigFiles, isZeroHour, cancellationToken), cancellationToken).ConfigureAwait(false);
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

    /// <summary>
    /// Probes game file systems for localized string tables (.csf and .str) in language order of preference.
    /// In multilingual installations, the first successfully loaded table in <see cref="WndConstants.StringTables.Languages"/> wins.
    /// Mod .str files supplement or override base game strings.
    /// </summary>
    private IReadOnlyDictionary<string, string> LoadTable(
        string baseRoot,
        string? overrideRoot,
        string? projectDirectory,
        IReadOnlyCollection<string>? additionalBigFiles,
        bool isZeroHour,
        CancellationToken cancellationToken)
    {
        var fileSystem = WndGameFileSystem.Open(baseRoot, overrideRoot, projectDirectory, logger, additionalBigFiles, isZeroHour, cancellationToken);
        var table = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // 1. Load base CSF string table
        foreach (var language in WndConstants.StringTables.Languages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = Path.Combine(
                WndConstants.StringTables.DataDirectory,
                language,
                WndConstants.StringTables.FileName);
            var bytes = TryRead(fileSystem, path);
            if (bytes == null)
            {
                continue;
            }

            try
            {
                using var stream = new MemoryStream(bytes);
                var csf = CsfFile.Load(stream);
                logger.LogInformation(
                    "Loaded {Count} strings from {Path} (archive: {Archive})",
                    csf.Count,
                    path,
                    fileSystem.GetSourceArchiveName(path) ?? "loose");
                foreach (var (k, v) in csf.Strings)
                {
                    table[k] = v;
                }

                break;
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

        // 2. Load and overlay .str string tables (plain-text string tables)
        foreach (var language in WndConstants.StringTables.Languages)
        {
            var strPath = Path.Combine(
                WndConstants.StringTables.DataDirectory,
                language,
                "generals.str");
            var strBytes = TryRead(fileSystem, strPath);
            if (strBytes != null && strBytes.Length > 0)
            {
                ParseStrFile(Encoding.UTF8.GetString(strBytes), table);
            }
        }

        var rootStrBytes = TryRead(fileSystem, "generals.str") ?? TryRead(fileSystem, "Data\\generals.str");
        if (rootStrBytes != null && rootStrBytes.Length > 0)
        {
            ParseStrFile(Encoding.UTF8.GetString(rootStrBytes), table);
        }

        // Also check if any loose .str file exists in the mod
        var modStr = fileSystem.TryReadModLooseFileByName("generals.str");
        if (modStr != null && modStr.Length > 0)
        {
            ParseStrFile(Encoding.UTF8.GetString(modStr), table);
        }

        return table;
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
