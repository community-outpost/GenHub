// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Tools.WorldBuilder;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.WorldBuilder;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text;

namespace GenHub.Core.Services.Tools.WorldBuilder;

/// <summary>
/// Typed SAGE INI database. Loads the WorldBuilder subsystem boot table from the game
/// asset file system with Default-to-override directory semantics (INI::loadFileDirectory
/// loads &lt;name&gt;.ini plus every *.ini under &lt;name&gt;\, recursively), and applies
/// per-map map.ini overrides with INI::loadWB skip tolerance.
/// </summary>
public sealed class SageIniDatabase(SageIniParser parser, ILogger<SageIniDatabase> logger) : ISageIniDatabase
{
    private sealed record SubsystemRow(string Name, string? FallbackDir, string OverrideDir);

    private static readonly SubsystemRow[] BootOrder =
    [
        new(SageIniConstants.Subsystems.GameData, DefaultDir(SageIniConstants.Subsystems.GameData), WorldBuilderDataConstants.Ini.GameData),
        new(SageIniConstants.Subsystems.Water, DefaultDir(SageIniConstants.Subsystems.Water), WorldBuilderDataConstants.Ini.Water),
        new(SageIniConstants.Subsystems.Science, DefaultDir(SageIniConstants.Subsystems.Science), WorldBuilderDataConstants.Ini.Science),
        new(SageIniConstants.Subsystems.Multiplayer, DefaultDir(SageIniConstants.Subsystems.Multiplayer), WorldBuilderDataConstants.Ini.Multiplayer),
        new(SageIniConstants.Subsystems.Terrain, DefaultDir(SageIniConstants.Subsystems.Terrain), WorldBuilderDataConstants.Ini.Terrain),
        new(SageIniConstants.Subsystems.Roads, DefaultDir(SageIniConstants.Subsystems.Roads), WorldBuilderDataConstants.Ini.Roads),
        new(SageIniConstants.Subsystems.Scripts, null, WorldBuilderDataConstants.Ini.Scripts),
        new(SageIniConstants.Subsystems.AudioEvents, DefaultDir(SageIniConstants.Subsystems.AudioEvents), WorldBuilderDataConstants.Ini.AudioEvents),
        new(SageIniConstants.Subsystems.Rank, null, WorldBuilderDataConstants.Ini.Rank),
        new(SageIniConstants.Subsystems.PlayerTemplate, DefaultDir(SageIniConstants.Subsystems.PlayerTemplate), WorldBuilderDataConstants.Ini.PlayerTemplate),
        new(SageIniConstants.Subsystems.SpecialPower, DefaultDir(SageIniConstants.Subsystems.SpecialPower), WorldBuilderDataConstants.Ini.SpecialPower),
        new(SageIniConstants.Subsystems.FXList, DefaultDir(SageIniConstants.Subsystems.FXList), WorldBuilderDataConstants.Ini.FXList),
        new(SageIniConstants.Subsystems.Weapon, null, WorldBuilderDataConstants.Ini.Weapon),
        new(SageIniConstants.Subsystems.ObjectCreationList, DefaultDir(SageIniConstants.Subsystems.ObjectCreationList), WorldBuilderDataConstants.Ini.ObjectCreationList),
        new(SageIniConstants.Subsystems.Locomotor, null, WorldBuilderDataConstants.Ini.Locomotor),
        new(SageIniConstants.Subsystems.DamageFX, null, WorldBuilderDataConstants.Ini.DamageFX),
        new(SageIniConstants.Subsystems.Armor, null, WorldBuilderDataConstants.Ini.Armor),
        new(SageIniConstants.Subsystems.Object, DefaultDir(SageIniConstants.Subsystems.Object), WorldBuilderDataConstants.Ini.Object),
        new(SageIniConstants.Subsystems.Crate, DefaultDir(SageIniConstants.Subsystems.Crate), WorldBuilderDataConstants.Ini.Crate),
        new(SageIniConstants.Subsystems.Upgrade, DefaultDir(SageIniConstants.Subsystems.Upgrade), WorldBuilderDataConstants.Ini.Upgrade),
    ];

    private readonly object _syncLock = new();
    private Dictionary<string, Dictionary<string, SageIniBlock>> _subsystemBlocks = CreateStore();
    private Dictionary<string, Dictionary<string, SageIniBlock>> _blocks = CreateStore();

    /// <inheritdoc />
    public async Task<OperationResult<SageIniLoadReport>> LoadSubsystemsAsync(IGameAssetFileSystem fileSystem, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        var started = Stopwatch.GetTimestamp();
        var merged = CreateStore();
        var entries = new List<SageIniLoadReport.SubsystemLoadEntry>();
        var diagnostics = new List<SageIniDiagnostic>();
        var filesRead = 0;
        var blocksLoaded = 0;
        foreach (var row in BootOrder)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = await LoadSubsystemAsync(fileSystem, row, merged, diagnostics, cancellationToken).ConfigureAwait(false);
            entries.Add(entry);
            filesRead += entry.FilesRead.Count;
            blocksLoaded += entry.BlocksLoaded;
        }

        lock (_syncLock)
        {
            _subsystemBlocks = merged;
            _blocks = CopyStore(merged);
        }

        var report = new SageIniLoadReport(entries, filesRead, blocksLoaded, diagnostics);
        logger.LogInformation("Loaded SAGE INI subsystems: {Blocks} blocks from {Files} files, {Skipped} subsystems skipped", blocksLoaded, filesRead, entries.Count(e => e.Skipped));
        return OperationResult<SageIniLoadReport>.CreateSuccess(report, Stopwatch.GetElapsedTime(started));
    }

    /// <inheritdoc />
    public async Task<OperationResult<MapIniLoadReport>> LoadWorldBuilderIniAsync(string mapIniPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mapIniPath);
        var started = Stopwatch.GetTimestamp();
        var bytes = await TryReadMapIniBytesAsync(mapIniPath, cancellationToken).ConfigureAwait(false);
        if (bytes == null)
        {
            return OperationResult<MapIniLoadReport>.CreateFailure($"Could not read map.ini at {mapIniPath}.", Stopwatch.GetElapsedTime(started));
        }

        Dictionary<string, SageIniBlock> known = [];
        Dictionary<string, Dictionary<string, SageIniBlock>> merged = [];
        lock (_syncLock)
        {
            merged = CopyStore(_subsystemBlocks);
            known = BuildKnownBlocks(merged);
        }

        var options = new SageIniParseOptions(
            BlockTable: SageIniConstants.BlockTables.WorldBuilder,
            TolerateBlockFailures: true,
            KnownBlocks: known,
            IncludeReader: CreateDiskIncludeReader(mapIniPath));
        var parsed = await parser.ParseAsync(DecodeIniText(bytes), mapIniPath, options, cancellationToken).ConfigureAwait(false);
        if (!parsed.Success || parsed.Data is null)
        {
            return OperationResult<MapIniLoadReport>.CreateFailure(parsed, Stopwatch.GetElapsedTime(started));
        }

        var document = parsed.Data;
        foreach (var block in document.Blocks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            MergeInto(merged, block);
        }

        lock (_syncLock)
        {
            _blocks = merged;
        }

        var report = new MapIniLoadReport(mapIniPath, document.Blocks.Count, document.SkippedBlocks, document.UnrecognizedBlocks, document.Diagnostics);
        logger.LogInformation("Loaded map.ini {Path}: {Blocks} blocks, {Skipped} skipped, {Unrecognized} unrecognized", mapIniPath, document.Blocks.Count, document.SkippedBlocks.Count, document.UnrecognizedBlocks.Count);
        return OperationResult<MapIniLoadReport>.CreateSuccess(report, Stopwatch.GetElapsedTime(started));
    }

    /// <inheritdoc />
    public IReadOnlyList<SageIniBlock> GetBlocks(string blockToken)
    {
        ArgumentNullException.ThrowIfNull(blockToken);
        lock (_syncLock)
        {
            return _blocks.TryGetValue(blockToken, out var byName) ? byName.Values.ToList() : [];
        }
    }

    /// <inheritdoc />
    public SageIniBlock? FindBlock(string blockToken, string name)
    {
        ArgumentNullException.ThrowIfNull(blockToken);
        ArgumentNullException.ThrowIfNull(name);
        lock (_syncLock)
        {
            return _blocks.TryGetValue(blockToken, out var byName) && byName.TryGetValue(name, out var block) ? block : null;
        }
    }

    private static Dictionary<string, Dictionary<string, SageIniBlock>> CreateStore()
    {
        return new Dictionary<string, Dictionary<string, SageIniBlock>>(StringComparer.Ordinal);
    }

    private static Dictionary<string, Dictionary<string, SageIniBlock>> CopyStore(Dictionary<string, Dictionary<string, SageIniBlock>> store)
    {
        var copy = CreateStore();
        foreach (var (token, byName) in store)
        {
            copy[token] = new Dictionary<string, SageIniBlock>(byName, StringComparer.OrdinalIgnoreCase);
        }

        return copy;
    }

    private static string DefaultDir(string subsystem)
    {
        return string.Concat(WorldBuilderDataConstants.Ini.DefaultPrefix, subsystem);
    }

    private static string DecodeIniText(byte[] bytes)
    {
        return Encoding.Latin1.GetString(bytes);
    }

    private static void MergeInto(Dictionary<string, Dictionary<string, SageIniBlock>> store, SageIniBlock block)
    {
        if (!store.TryGetValue(block.BlockToken, out var byName))
        {
            byName = new Dictionary<string, SageIniBlock>(StringComparer.OrdinalIgnoreCase);
            store[block.BlockToken] = byName;
        }

        byName[block.Name] = byName.TryGetValue(block.Name, out var existing)
            ? SageIniParser.MergeOverride(existing, block)
            : block;
    }

    private static Dictionary<string, SageIniBlock> BuildKnownBlocks(Dictionary<string, Dictionary<string, SageIniBlock>> store)
    {
        var known = new Dictionary<string, SageIniBlock>(StringComparer.OrdinalIgnoreCase);
        foreach (var byName in store.Values)
        {
            foreach (var (name, block) in byName)
            {
                if (!string.IsNullOrEmpty(name))
                {
                    known[name] = block;
                }
            }
        }

        return known;
    }

    private static List<string> CollectSubsystemFiles(IGameAssetFileSystem fileSystem, SubsystemRow row)
    {
        var files = new List<string>();
        if (row.FallbackDir is not null)
        {
            AddDirectoryLoad(files, fileSystem, row.FallbackDir);
        }

        AddDirectoryLoad(files, fileSystem, row.OverrideDir);
        return files;
    }

    private static void AddDirectoryLoad(List<string> files, IGameAssetFileSystem fileSystem, string directory)
    {
        var single = string.Concat(directory, ".ini");
        if (fileSystem.FileExists(single))
        {
            files.Add(single);
        }

        var listed = fileSystem.ListFiles(directory, "*.ini", recurse: true);
        var topLevel = new List<string>();
        var nested = new List<string>();
        foreach (var file in listed)
        {
            var relative = file.Length > directory.Length ? file[directory.Length..].TrimStart('\\', '/') : file;
            if (relative.Contains('\\') || relative.Contains('/'))
            {
                nested.Add(file);
            }
            else
            {
                topLevel.Add(file);
            }
        }

        files.AddRange(topLevel);
        files.AddRange(nested);
    }

    private static Func<string, string, CancellationToken, Task<OperationResult<(string ResolvedPath, string Text)>>> CreateVfsIncludeReader(IGameAssetFileSystem fileSystem)
    {
        return async (includingFile, includePath, cancellationToken) =>
        {
            List<string> candidates = [ResolveVirtualPath(includingFile, includePath), NormalizeVirtualPath(includePath)];
            foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!fileSystem.FileExists(candidate))
                {
                    continue;
                }

                var read = await fileSystem.ReadAllBytesAsync(candidate, cancellationToken).ConfigureAwait(false);
                if (!read.Success || read.Data is null)
                {
                    return OperationResult<(string ResolvedPath, string Text)>.CreateFailure(read);
                }

                return OperationResult<(string ResolvedPath, string Text)>.CreateSuccess((candidate, DecodeIniText(read.Data)));
            }

            return OperationResult<(string ResolvedPath, string Text)>.CreateFailure($"Include '{includePath}' was not found on the mounted layers.");
        };
    }

    private static Func<string, string, CancellationToken, Task<OperationResult<(string ResolvedPath, string Text)>>> CreateDiskIncludeReader(string mapIniPath)
    {
        var baseDir = Path.GetDirectoryName(Path.GetFullPath(mapIniPath)) ?? string.Empty;
        return async (includingFile, includePath, cancellationToken) =>
        {
            var nestDir = Path.GetDirectoryName(includingFile);
            var anchor = ResolveAnchorDirectory(baseDir, nestDir);
            var resolved = Path.IsPathFullyQualified(includePath) ? includePath : Path.GetFullPath(Path.Combine(anchor, includePath));
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var bytes = await File.ReadAllBytesAsync(resolved, cancellationToken).ConfigureAwait(false);
                return OperationResult<(string ResolvedPath, string Text)>.CreateSuccess((resolved, DecodeIniText(bytes)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                return OperationResult<(string ResolvedPath, string Text)>.CreateFailure($"Include '{includePath}' could not be read: {ex.Message}.");
            }
        };
    }

    private static string ResolveAnchorDirectory(string baseDir, string? nestDir)
    {
        if (string.IsNullOrEmpty(nestDir))
        {
            return baseDir;
        }

        return Path.IsPathFullyQualified(nestDir) ? nestDir : Path.GetFullPath(Path.Combine(baseDir, nestDir));
    }

    private static string ResolveVirtualPath(string includingFile, string includePath)
    {
        var normalized = NormalizeVirtualPath(includePath);
        if (normalized.StartsWith(WorldBuilderDataConstants.Separators.Virtual))
        {
            return normalized.TrimStart(WorldBuilderDataConstants.Separators.Virtual);
        }

        var directory = NormalizeVirtualPath(includingFile);
        var separator = directory.LastIndexOf(WorldBuilderDataConstants.Separators.Virtual);
        var baseDir = separator >= 0 ? directory[..separator] : string.Empty;
        return CollapseVirtualPath(string.IsNullOrEmpty(baseDir) ? normalized : string.Concat(baseDir, WorldBuilderDataConstants.Separators.Virtual, normalized));
    }

    private static string NormalizeVirtualPath(string virtualPath)
    {
        return virtualPath.Trim().Replace(WorldBuilderDataConstants.Separators.Alternate, WorldBuilderDataConstants.Separators.Virtual);
    }

    private static string CollapseVirtualPath(string virtualPath)
    {
        var segments = new List<string>();
        foreach (var segment in virtualPath.Split(WorldBuilderDataConstants.Separators.Virtual, StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                if (segments.Count > 0)
                {
                    segments.RemoveAt(segments.Count - 1);
                }

                continue;
            }

            segments.Add(segment);
        }

        return string.Join(WorldBuilderDataConstants.Separators.Virtual, segments);
    }

    private static string AttemptedPaths(SubsystemRow row)
    {
        return row.FallbackDir is null ? row.OverrideDir : string.Concat(row.FallbackDir, ", ", row.OverrideDir);
    }

    private static void AddSkipDiagnostics(List<SageIniDiagnostic> diagnostics, SageIniDocument document)
    {
        foreach (var skipped in document.SkippedBlocks)
        {
            diagnostics.Add(new SageIniDiagnostic(SageIniDiagnosticLevel.Warning, skipped.SourceFile, skipped.LineNumber, $"Skipped block '{skipped.HeaderLine}': {skipped.Reason}."));
        }

        foreach (var unrecognized in document.UnrecognizedBlocks)
        {
            diagnostics.Add(new SageIniDiagnostic(SageIniDiagnosticLevel.Warning, unrecognized.SourceFile, unrecognized.LineNumber, $"Unknown block '{unrecognized.HeaderLine}': {unrecognized.Reason}."));
        }
    }

    private async Task<SageIniLoadReport.SubsystemLoadEntry> LoadSubsystemAsync(
        IGameAssetFileSystem fileSystem,
        SubsystemRow row,
        Dictionary<string, Dictionary<string, SageIniBlock>> merged,
        List<SageIniDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        var files = CollectSubsystemFiles(fileSystem, row);
        if (files.Count == 0)
        {
            var reason = $"No INI files for {row.Name} are present on the mounted layers ({AttemptedPaths(row)}).";
            logger.LogDebug("{Reason}", reason);
            return new SageIniLoadReport.SubsystemLoadEntry(row.Name, [], 0, Skipped: true, reason);
        }

        var read = new List<string>();
        var contributed = 0;
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var loaded = await ParseAndMergeFileAsync(fileSystem, file, merged, diagnostics, cancellationToken).ConfigureAwait(false);
            if (loaded >= 0)
            {
                read.Add(file);
                contributed += loaded;
            }
        }

        return new SageIniLoadReport.SubsystemLoadEntry(row.Name, read, contributed, Skipped: false, SkipReason: null);
    }

    private async Task<int> ParseAndMergeFileAsync(
        IGameAssetFileSystem fileSystem,
        string virtualPath,
        Dictionary<string, Dictionary<string, SageIniBlock>> merged,
        List<SageIniDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        var bytes = await fileSystem.ReadAllBytesAsync(virtualPath, cancellationToken).ConfigureAwait(false);
        if (!bytes.Success || bytes.Data is null)
        {
            diagnostics.Add(new SageIniDiagnostic(SageIniDiagnosticLevel.Error, virtualPath, 0, $"Could not read file: {bytes.FirstError}."));
            logger.LogWarning("Skipping unreadable SAGE INI file {File}: {Error}", virtualPath, bytes.FirstError);
            return -1;
        }

        var options = new SageIniParseOptions(KnownBlocks: BuildKnownBlocks(merged), TolerateBlockFailures: true, IncludeReader: CreateVfsIncludeReader(fileSystem));
        var parsed = await parser.ParseAsync(DecodeIniText(bytes.Data), virtualPath, options, cancellationToken).ConfigureAwait(false);
        if (!parsed.Success || parsed.Data is null)
        {
            diagnostics.Add(new SageIniDiagnostic(SageIniDiagnosticLevel.Error, virtualPath, 0, parsed.FirstError ?? "Parse failed."));
            logger.LogWarning("Skipping unparsable SAGE INI file {File}: {Error}", virtualPath, parsed.FirstError);
            return 0;
        }

        diagnostics.AddRange(parsed.Data.Diagnostics);
        AddSkipDiagnostics(diagnostics, parsed.Data);
        foreach (var block in parsed.Data.Blocks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            MergeInto(merged, block);
        }

        return parsed.Data.Blocks.Count;
    }

    private async Task<byte[]?> TryReadMapIniBytesAsync(string mapIniPath, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await File.ReadAllBytesAsync(mapIniPath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            logger.LogWarning(ex, "Could not read map.ini at {Path}", mapIniPath);
            return null;
        }
    }
}
