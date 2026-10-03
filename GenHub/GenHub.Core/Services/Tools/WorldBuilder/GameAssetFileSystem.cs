// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.Tools.WorldBuilder;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.WorldBuilder;
using GenHub.Core.Services.Tools.Checksum;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace GenHub.Core.Services.Tools.WorldBuilder;

/// <summary>
/// Virtual file system over workspace loose files plus .BIG archives, mirroring the SAGE
/// engine resolution rules (FileSystem::openFile checks the local file system before the
/// archive file system; ArchiveFileSystem::loadIntoDirectoryTree inserts overwriting
/// entries at the front). Layers apply lowest precedence first with overwrite, so the
/// explicit mod path wins over workspace files, which win over Zero Hour archives,
/// which win over Generals archives, which win over the bundled base. Within a layer,
/// loose files win over archives, and archives apply in ascending BIG load priority so
/// INIZH.big wins over INI.big (generalized from MappedImageRegistry).
/// </summary>
public sealed class GameAssetFileSystem(IGameInstallationService installations, ILogger<GameAssetFileSystem> logger)
    : IGameAssetFileSystem, IDisposable
{
    private sealed record AssetSource(string DisplayPath, string? LoosePath, BigArchiveEntry? ArchiveEntry);

    private sealed record MountLayer(string Name, string? Directory, string? ArchiveFile);

    private sealed record MountIndex(Dictionary<string, AssetSource> Files, int LooseCount, int ArchiveEntryCount, int ArchiveCount, int LayersIndexed);

    private readonly object _syncLock = new();
    private readonly SemaphoreSlim _mountGate = new(1, 1);
    private bool _disposed;
    private Dictionary<string, AssetSource> _files = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public async Task<OperationResult<bool>> MountAsync(GameAssetMountSpec spec, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _mountGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return await MountCoreAsync(spec, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (!_disposed)
            {
                _mountGate.Release();
            }
        }
    }

    /// <inheritdoc />
    public bool FileExists(string virtualPath)
    {
        if (string.IsNullOrWhiteSpace(virtualPath))
        {
            return false;
        }

        lock (_syncLock)
        {
            return _files.ContainsKey(NormalizeVirtualPath(virtualPath));
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<byte[]>> ReadAllBytesAsync(string virtualPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(virtualPath);
        AssetSource? source = null;
        lock (_syncLock)
        {
            _files.TryGetValue(NormalizeVirtualPath(virtualPath), out source);
        }

        if (source is null)
        {
            return OperationResult<byte[]>.CreateFailure($"File not found in mounted layers: {virtualPath}");
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (source.LoosePath is not null)
            {
                var looseBytes = await File.ReadAllBytesAsync(source.LoosePath, cancellationToken).ConfigureAwait(false);
                return OperationResult<byte[]>.CreateSuccess(looseBytes);
            }

            var archiveBytes = await Task.Run(() => BigArchiveReader.ReadEntryData(source.ArchiveEntry!), cancellationToken).ConfigureAwait(false);
            return OperationResult<byte[]>.CreateSuccess(archiveBytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
        {
            logger.LogWarning(ex, "Failed to read mounted file {VirtualPath}", virtualPath);
            return OperationResult<byte[]>.CreateFailure($"Failed to read mounted file: {virtualPath}");
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<string> ListFiles(string virtualDir, string pattern, bool recurse)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        var prefix = NormalizeDirectoryPrefix(virtualDir);
        var loweredPattern = pattern.ToLowerInvariant();
        List<string> matches = [];
        lock (_syncLock)
        {
            foreach (var (key, source) in _files)
            {
                if (IsListMatch(key, prefix, loweredPattern, recurse))
                {
                    matches.Add(source.DisplayPath);
                }
            }
        }

        matches.Sort(StringComparer.OrdinalIgnoreCase);
        return matches;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_syncLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _mountGate.Dispose();
    }

    private static int CompareArchiveOrder(string left, string right)
    {
        var priority = BigArchiveLoadPriority(left).CompareTo(BigArchiveLoadPriority(right));
        return priority != 0 ? priority : StringComparer.OrdinalIgnoreCase.Compare(left, right);
    }

    private static int BigArchiveLoadPriority(string path)
    {
        var name = Path.GetFileName(path);
        if (name.Equals(WorldBuilderDataConstants.Big.IniBigFileName, StringComparison.OrdinalIgnoreCase))
        {
            return WorldBuilderDataConstants.Big.IniBigLoadPriority;
        }

        if (name.Equals(WorldBuilderDataConstants.Big.IniZhBigFileName, StringComparison.OrdinalIgnoreCase))
        {
            return WorldBuilderDataConstants.Big.IniZhBigLoadPriority;
        }

        return WorldBuilderDataConstants.Big.DefaultLoadPriority;
    }

    private static string NormalizeVirtualPath(string virtualPath)
    {
        return virtualPath.Trim()
            .TrimStart(WorldBuilderDataConstants.Separators.Virtual, WorldBuilderDataConstants.Separators.Alternate)
            .Replace(WorldBuilderDataConstants.Separators.Alternate, WorldBuilderDataConstants.Separators.Virtual)
            .ToLowerInvariant();
    }

    private static (int Textures, int Models) CountArtEntries(Dictionary<string, AssetSource> files)
    {
        var texturePrefix = string.Concat(
            WorldBuilderDataConstants.Art.Textures,
            WorldBuilderDataConstants.Separators.Virtual).ToLowerInvariant();
        var modelPrefix = string.Concat(
            WorldBuilderDataConstants.Art.W3D,
            WorldBuilderDataConstants.Separators.Virtual).ToLowerInvariant();
        var textures = 0;
        var models = 0;
        foreach (var key in files.Keys)
        {
            if (key.StartsWith(texturePrefix, StringComparison.Ordinal))
            {
                textures++;
            }
            else if (key.StartsWith(modelPrefix, StringComparison.Ordinal))
            {
                models++;
            }
        }

        return (textures, models);
    }

    private static string NormalizeDirectoryPrefix(string? virtualDir)
    {
        if (string.IsNullOrWhiteSpace(virtualDir))
        {
            return string.Empty;
        }

        var normalized = NormalizeVirtualPath(virtualDir).TrimEnd(WorldBuilderDataConstants.Separators.Virtual);
        return string.IsNullOrEmpty(normalized)
            ? string.Empty
            : string.Concat(normalized, WorldBuilderDataConstants.Separators.Virtual);
    }

    private static bool IsListMatch(string key, string prefix, string loweredPattern, bool recurse)
    {
        if (!key.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var rest = key[prefix.Length..];
        if (rest.Length == 0)
        {
            return false;
        }

        var separator = rest.LastIndexOf(WorldBuilderDataConstants.Separators.Virtual);
        if (!recurse && separator >= 0)
        {
            return false;
        }

        var fileName = separator >= 0 ? rest[(separator + 1)..] : rest;
        return WildcardMatch(fileName, loweredPattern);
    }

    private static bool WildcardMatch(ReadOnlySpan<char> text, ReadOnlySpan<char> pattern)
    {
        var textIndex = 0;
        var patternIndex = 0;
        var starIndex = -1;
        var markIndex = 0;
        while (textIndex < text.Length)
        {
            if (patternIndex < pattern.Length && (pattern[patternIndex] == '?' || pattern[patternIndex] == text[textIndex]))
            {
                textIndex++;
                patternIndex++;
            }
            else if (patternIndex < pattern.Length && pattern[patternIndex] == '*')
            {
                starIndex = patternIndex++;
                markIndex = textIndex;
            }
            else if (starIndex != -1)
            {
                patternIndex = starIndex + 1;
                textIndex = ++markIndex;
            }
            else
            {
                return false;
            }
        }

        while (patternIndex < pattern.Length && pattern[patternIndex] == '*')
        {
            patternIndex++;
        }

        return patternIndex == pattern.Length;
    }

    private static string? FirstNonEmpty(string? preferred, string? fallback)
    {
        if (!string.IsNullOrWhiteSpace(preferred))
        {
            return preferred;
        }

        return !string.IsNullOrWhiteSpace(fallback) ? fallback : null;
    }

    private static List<MountLayer> CollectLayers(GameAssetMountSpec spec)
    {
        var layers = new List<MountLayer>();
        AddDirectoryLayer(layers, "bundled", spec.BundledGeneralsRoot);
        AddDirectoryLayer(layers, "generals", spec.GeneralsRoot);
        AddDirectoryLayer(layers, "zerohour", spec.ZeroHourRoot);
        AddDirectoryLayer(layers, "workspace", spec.WorkspaceRoot);
        AddModLayer(layers, spec.ModPath);
        return layers;
    }

    private static void AddDirectoryLayer(List<MountLayer> layers, string name, string? directory)
    {
        if (!string.IsNullOrWhiteSpace(directory))
        {
            layers.Add(new MountLayer(name, directory, null));
        }
    }

    private static void AddModLayer(List<MountLayer> layers, string? modPath)
    {
        if (string.IsNullOrWhiteSpace(modPath))
        {
            return;
        }

        if (Directory.Exists(modPath))
        {
            layers.Add(new MountLayer("mod", modPath, null));
        }
        else
        {
            layers.Add(new MountLayer("mod", null, modPath));
        }
    }

    private async Task<OperationResult<bool>> MountCoreAsync(GameAssetMountSpec spec, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var resolved = await ResolveRootsAsync(spec, cancellationToken).ConfigureAwait(false);
            if (!resolved.Success || resolved.Data is null)
            {
                return OperationResult<bool>.CreateFailure(resolved, Stopwatch.GetElapsedTime(started));
            }

            var layers = CollectLayers(resolved.Data);
            if (layers.Count == 0)
            {
                return OperationResult<bool>.CreateFailure("No mount roots were provided.", Stopwatch.GetElapsedTime(started));
            }

            var index = await Task.Run(() => BuildIndex(layers, cancellationToken), cancellationToken).ConfigureAwait(false);
            if (index.LayersIndexed == 0)
            {
                return OperationResult<bool>.CreateFailure("None of the mount roots exist on disk.", Stopwatch.GetElapsedTime(started));
            }

            lock (_syncLock)
            {
                _files = index.Files;
            }

            var (textureCount, modelCount) = CountArtEntries(index.Files);
            var elapsed = Stopwatch.GetElapsedTime(started);
            logger.LogInformation(
                "Mounted {Files} game assets ({Loose} loose, {Entries} archive entries across {Archives} archives, {Layers} layers, {Textures} art textures, {Models} W3D models)",
                index.Files.Count,
                index.LooseCount,
                index.ArchiveEntryCount,
                index.ArchiveCount,
                index.LayersIndexed,
                textureCount,
                modelCount);
            return OperationResult<bool>.CreateSuccess(true, elapsed);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Failed to mount game asset layers");
            return OperationResult<bool>.CreateFailure("Failed to mount game asset layers.", Stopwatch.GetElapsedTime(started));
        }
    }

    private async Task<OperationResult<GameAssetMountSpec>> ResolveRootsAsync(GameAssetMountSpec spec, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(spec.InstallationId))
        {
            return OperationResult<GameAssetMountSpec>.CreateSuccess(spec);
        }

        var installation = await installations.GetInstallationAsync(spec.InstallationId, cancellationToken).ConfigureAwait(false);
        if (!installation.Success || installation.Data is null)
        {
            return OperationResult<GameAssetMountSpec>.CreateFailure(installation);
        }

        var data = installation.Data;
        var resolved = spec with
        {
            ZeroHourRoot = FirstNonEmpty(spec.ZeroHourRoot, data.ZeroHourPath),
            GeneralsRoot = FirstNonEmpty(spec.GeneralsRoot, data.EffectiveGeneralsArchivePath),
            BundledGeneralsRoot = FirstNonEmpty(spec.BundledGeneralsRoot, data.BundledGeneralsPath),
        };
        return OperationResult<GameAssetMountSpec>.CreateSuccess(resolved);
    }

    private MountIndex BuildIndex(List<MountLayer> layers, CancellationToken cancellationToken)
    {
        var files = new Dictionary<string, AssetSource>(StringComparer.OrdinalIgnoreCase);
        var looseCount = 0;
        var archiveEntryCount = 0;
        var archiveCount = 0;
        var layersIndexed = 0;
        foreach (var layer in layers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IndexLayer(layer, files, cancellationToken, ref looseCount, ref archiveEntryCount, ref archiveCount))
            {
                layersIndexed++;
            }
        }

        return new MountIndex(files, looseCount, archiveEntryCount, archiveCount, layersIndexed);
    }

    private bool IndexLayer(
        MountLayer layer,
        Dictionary<string, AssetSource> files,
        CancellationToken cancellationToken,
        ref int looseCount,
        ref int archiveEntryCount,
        ref int archiveCount)
    {
        if (layer.Directory is not null)
        {
            return IndexDirectoryLayer(layer, files, cancellationToken, ref looseCount, ref archiveEntryCount, ref archiveCount);
        }

        return IndexSingleArchiveLayer(layer, files, cancellationToken, ref archiveEntryCount, ref archiveCount);
    }

    private bool IndexDirectoryLayer(
        MountLayer layer,
        Dictionary<string, AssetSource> files,
        CancellationToken cancellationToken,
        ref int looseCount,
        ref int archiveEntryCount,
        ref int archiveCount)
    {
        var root = layer.Directory!;
        if (!Directory.Exists(root))
        {
            logger.LogWarning("Skipping missing {Layer} mount root: {Root}", layer.Name, root);
            return false;
        }

        foreach (var archive in EnumerateArchives(root))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IndexArchiveFile(archive, files, cancellationToken, ref archiveEntryCount))
            {
                archiveCount++;
            }
        }

        foreach (var loose in EnumerateLooseFiles(root))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(root, loose)
                .Replace(WorldBuilderDataConstants.Separators.Alternate, WorldBuilderDataConstants.Separators.Virtual);
            files[NormalizeVirtualPath(relative)] = new AssetSource(relative, loose, null);
            looseCount++;
        }

        return true;
    }

    private bool IndexSingleArchiveLayer(
        MountLayer layer,
        Dictionary<string, AssetSource> files,
        CancellationToken cancellationToken,
        ref int archiveEntryCount,
        ref int archiveCount)
    {
        var archive = layer.ArchiveFile!;
        if (!File.Exists(archive))
        {
            logger.LogWarning("Skipping missing {Layer} mount archive: {Archive}", layer.Name, archive);
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (IndexArchiveFile(archive, files, cancellationToken, ref archiveEntryCount))
        {
            archiveCount++;
        }

        return true;
    }

    private bool IndexArchiveFile(
        string archive,
        Dictionary<string, AssetSource> files,
        CancellationToken cancellationToken,
        ref int archiveEntryCount)
    {
        if (!BigArchiveReader.TryReadIndex(archive, out var entries))
        {
            logger.LogWarning("Skipping invalid or unreadable archive at {Archive}", archive);
            return false;
        }

        foreach (var entry in entries.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            files[NormalizeVirtualPath(entry.Path)] = new AssetSource(entry.Path, null, entry);
            archiveEntryCount++;
        }

        return true;
    }

    private IReadOnlyList<string> EnumerateArchives(string root)
    {
        try
        {
            var archives = Directory.EnumerateFiles(root, WorldBuilderDataConstants.Big.SearchPattern, SearchOption.AllDirectories).ToList();
            archives.Sort(CompareArchiveOrder);
            return archives;
        }
        catch (IOException ex)
        {
            logger.LogDebug(ex, "Could not enumerate archives under {Root}", root);
            return [];
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogDebug(ex, "Could not enumerate archives under {Root}", root);
            return [];
        }
    }

    private IReadOnlyList<string> EnumerateLooseFiles(string root)
    {
        try
        {
            return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).ToList();
        }
        catch (IOException ex)
        {
            logger.LogDebug(ex, "Could not enumerate loose files under {Root}", root);
            return [];
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogDebug(ex, "Could not enumerate loose files under {Root}", root);
            return [];
        }
    }
}
