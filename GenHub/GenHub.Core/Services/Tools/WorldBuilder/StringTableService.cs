// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Tools.WorldBuilder;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.WorldBuilder;
using GenHub.Core.Services.Tools.GenHotkeys;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text;

namespace GenHub.Core.Services.Tools.WorldBuilder;

/// <summary>
/// String-table service (TheGameText): compiled CSF plus text STR sources backing
/// DisplayName resolution.
/// </summary>
public sealed class StringTableService(ILogger<StringTableService> logger) : IStringTableService
{
    private readonly object _syncLock = new();
    private readonly Dictionary<string, string> _gameStrings = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _mapStrings = new(StringComparer.OrdinalIgnoreCase);
    private string _language = WorldBuilderCatalogConstants.StrFile.DefaultLanguage;

    /// <inheritdoc />
    public int Count
    {
        get
        {
            lock (_syncLock)
            {
                return _gameStrings.Keys.Concat(_mapStrings.Keys).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            }
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetLabels()
    {
        lock (_syncLock)
        {
            return _gameStrings.Keys.Concat(_mapStrings.Keys).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(label => label, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<StringTableLoadReport>> LoadAsync(IGameAssetFileSystem fileSystem, string? language = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        var started = Stopwatch.GetTimestamp();
        var resolvedLanguage = string.IsNullOrWhiteSpace(language) ? WorldBuilderCatalogConstants.StrFile.DefaultLanguage : language.Trim();

        var csfLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var csfPath = WorldBuilderDataConstants.StringTables.FormatCsfPath(resolvedLanguage);
        if (fileSystem.FileExists(csfPath))
        {
            var loaded = await LoadCsfAsync(fileSystem, csfPath, cancellationToken).ConfigureAwait(false);
            if (!loaded.Success || loaded.Data is null)
            {
                return OperationResult<StringTableLoadReport>.CreateFailure(loaded, Stopwatch.GetElapsedTime(started));
            }

            csfLabels = loaded.Data;
        }

        var strLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (fileSystem.FileExists(WorldBuilderDataConstants.StringTables.StrPath))
        {
            var loaded = await LoadStrAsync(fileSystem, cancellationToken).ConfigureAwait(false);
            if (!loaded.Success || loaded.Data is null)
            {
                return OperationResult<StringTableLoadReport>.CreateFailure(loaded, Stopwatch.GetElapsedTime(started));
            }

            strLabels = loaded.Data;
        }

        lock (_syncLock)
        {
            _gameStrings.Clear();
            foreach (var (label, value) in csfLabels)
            {
                _gameStrings[label] = value;
            }

            foreach (var (label, value) in strLabels)
            {
                _gameStrings[label] = value;
            }

            _language = resolvedLanguage;
        }

        var report = new StringTableLoadReport(resolvedLanguage, csfLabels.Count, strLabels.Count, MapStrCount());
        logger.LogInformation("Loaded string table ({Language}): {Csf} CSF and {Str} STR labels", resolvedLanguage, csfLabels.Count, strLabels.Count);
        return OperationResult<StringTableLoadReport>.CreateSuccess(report, Stopwatch.GetElapsedTime(started));
    }

    /// <inheritdoc />
    public async Task<OperationResult<StringTableLoadReport>> LoadMapStringsAsync(string mapStrPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mapStrPath);
        var started = Stopwatch.GetTimestamp();
        if (!File.Exists(mapStrPath))
        {
            return OperationResult<StringTableLoadReport>.CreateSuccess(new StringTableLoadReport(MapLanguage(), 0, 0, 0), Stopwatch.GetElapsedTime(started));
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bytes = await File.ReadAllBytesAsync(mapStrPath, cancellationToken).ConfigureAwait(false);
            var parsed = StrFile.LoadText(Encoding.Latin1.GetString(bytes));
            lock (_syncLock)
            {
                _mapStrings.Clear();
                foreach (var (label, value) in parsed.Strings)
                {
                    _mapStrings[label] = value;
                }
            }

            var report = new StringTableLoadReport(MapLanguage(), 0, 0, parsed.Count);
            logger.LogInformation("Loaded {Count} map strings from {Path}", parsed.Count, mapStrPath);
            return OperationResult<StringTableLoadReport>.CreateSuccess(report, Stopwatch.GetElapsedTime(started));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or InvalidDataException)
        {
            logger.LogWarning(ex, "Could not load map strings at {Path}", mapStrPath);
            return OperationResult<StringTableLoadReport>.CreateFailure($"Could not load map strings at {mapStrPath}.", Stopwatch.GetElapsedTime(started));
        }
    }

    /// <inheritdoc />
    public string GetString(string label)
    {
        return TryGetString(label, out var value) ? value : string.Empty;
    }

    /// <inheritdoc />
    public bool TryGetString(string label, out string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        lock (_syncLock)
        {
            if (_mapStrings.TryGetValue(label, out var mapped))
            {
                value = mapped;
                return true;
            }

            if (_gameStrings.TryGetValue(label, out var game))
            {
                value = game;
                return true;
            }
        }

        value = string.Empty;
        return false;
    }

    /// <inheritdoc />
    public string ResolveLabel(string? rawDisplayName, string fallbackName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fallbackName);
        if (string.IsNullOrWhiteSpace(rawDisplayName))
        {
            return fallbackName;
        }

        var trimmed = rawDisplayName.Trim();
        var key = trimmed.StartsWith(WorldBuilderCatalogConstants.StringLabels.LabelPrefix, StringComparison.OrdinalIgnoreCase)
            ? trimmed[WorldBuilderCatalogConstants.StringLabels.LabelPrefix.Length..]
            : trimmed;
        if (TryGetString(key, out var resolved) && resolved.Length > 0)
        {
            return resolved;
        }

        if (!key.Equals(trimmed, StringComparison.Ordinal) && TryGetString(trimmed, out var literal) && literal.Length > 0)
        {
            return literal;
        }

        return fallbackName;
    }

    private static async Task<OperationResult<Dictionary<string, string>>> LoadCsfAsync(IGameAssetFileSystem fileSystem, string csfPath, CancellationToken cancellationToken)
    {
        var read = await fileSystem.ReadAllBytesAsync(csfPath, cancellationToken).ConfigureAwait(false);
        if (!read.Success || read.Data is null)
        {
            return OperationResult<Dictionary<string, string>>.CreateFailure(read);
        }

        try
        {
            using var stream = new MemoryStream(read.Data, writable: false);
            var csf = CsfFile.Load(stream);
            return OperationResult<Dictionary<string, string>>.CreateSuccess(new Dictionary<string, string>(csf.Strings, StringComparer.OrdinalIgnoreCase));
        }
        catch (InvalidDataException ex)
        {
            return OperationResult<Dictionary<string, string>>.CreateFailure($"The CSF table at {csfPath} is malformed: {ex.Message}.");
        }
        catch (ArgumentException ex)
        {
            return OperationResult<Dictionary<string, string>>.CreateFailure($"The CSF table at {csfPath} is malformed: {ex.Message}.");
        }
    }

    private static async Task<OperationResult<Dictionary<string, string>>> LoadStrAsync(IGameAssetFileSystem fileSystem, CancellationToken cancellationToken)
    {
        var read = await fileSystem.ReadAllBytesAsync(WorldBuilderDataConstants.StringTables.StrPath, cancellationToken).ConfigureAwait(false);
        if (!read.Success || read.Data is null)
        {
            return OperationResult<Dictionary<string, string>>.CreateFailure(read);
        }

        try
        {
            var str = StrFile.LoadText(Encoding.Latin1.GetString(read.Data));
            return OperationResult<Dictionary<string, string>>.CreateSuccess(new Dictionary<string, string>(str.Strings, StringComparer.OrdinalIgnoreCase));
        }
        catch (InvalidDataException ex)
        {
            return OperationResult<Dictionary<string, string>>.CreateFailure($"The STR table is malformed: {ex.Message}.");
        }
        catch (ArgumentException ex)
        {
            return OperationResult<Dictionary<string, string>>.CreateFailure($"The STR table is malformed: {ex.Message}.");
        }
    }

    private int MapStrCount()
    {
        lock (_syncLock)
        {
            return _mapStrings.Count;
        }
    }

    private string MapLanguage()
    {
        lock (_syncLock)
        {
            return _language;
        }
    }
}
