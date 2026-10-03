using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.Tools.IniEditor;
using GenHub.Core.Interfaces.Tools.ModBuilder;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.IniEditor;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.IniEditor.Services;

/// <summary>
/// Indexes INI blocks from the open document, the open folder, and vanilla game data.
/// Folder and vanilla files are scanned with a lightweight header pass; full parsing
/// happens only when a block is cloned.
/// </summary>
public sealed class IniReferenceService(
    IIniDocumentService iniDocumentService,
    IGameInstallationService installationService,
    IArchiveService archiveService,
    ILogger<IniReferenceService> logger) : IIniReferenceService
{
    private static readonly EnumerationOptions ScanEnumerationOptions = new()
    {
        AttributesToSkip = FileAttributes.Hidden | FileAttributes.ReparsePoint | FileAttributes.System,
        IgnoreInaccessible = true,
        RecurseSubdirectories = true,
    };

    private IReadOnlyList<IniReferenceEntry> _entries = [];
    private List<IniReferenceEntry> _folderEntries = [];
    private List<IniReferenceEntry> _vanillaEntries = [];
    private Dictionary<string, HashSet<string>> _folderTokens = new(PathHelper.PathComparer);
    private Dictionary<string, HashSet<string>> _vanillaTokens = new(PathHelper.PathComparer);
    private string? _indexedFolderPath;
    private bool _vanillaIndexed;
    private IniDocument? _document;

    /// <inheritdoc />
    public IReadOnlyList<IniReferenceEntry> Entries => _entries;

    /// <inheritdoc />
    public bool IsIndexed { get; private set; }

    /// <inheritdoc />
    public async Task<OperationResult<int>> RebuildIndexAsync(IniDocument? document, string? folderPath, bool forceRescan = false, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var entries = new List<IniReferenceEntry>();
        _document = document;

        try
        {
            if (document != null)
            {
                AddDocumentEntries(document, entries);
            }

            cancellationToken.ThrowIfCancellationRequested();
            await AddCachedFolderEntriesAsync(folderPath, forceRescan, entries, cancellationToken).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            await AddCachedVanillaEntriesAsync(forceRescan, entries, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Failed to rebuild the INI reference index");
            return OperationResult<int>.CreateFailure($"Failed to rebuild the reference index: {ex.Message}", stopwatch.Elapsed);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Failed to rebuild the INI reference index");
            return OperationResult<int>.CreateFailure($"Failed to rebuild the reference index: {ex.Message}", stopwatch.Elapsed);
        }

        _entries = entries;
        IsIndexed = true;
        logger.LogInformation("Indexed {Count} INI reference entries", entries.Count);
        return OperationResult<int>.CreateSuccess(entries.Count, stopwatch.Elapsed);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetNames(string blockType)
    {
        ArgumentNullException.ThrowIfNull(blockType);
        return _entries
            .Where(entry => string.Equals(entry.BlockType, blockType, StringComparison.OrdinalIgnoreCase))
            .Select(entry => entry.Name)
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<OperationResult<IniBlock?>> CloneBlockAsync(IniReferenceEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var stopwatch = Stopwatch.StartNew();

        if (entry.Source == IniReferenceSource.Document && _document != null)
        {
            var local = FindBlock(_document.Blocks, entry);
            return OperationResult<IniBlock?>.CreateSuccess(local == null ? null : CopyBlock(local), stopwatch.Elapsed);
        }

        if (string.IsNullOrEmpty(entry.FilePath) || !File.Exists(entry.FilePath))
        {
            return OperationResult<IniBlock?>.CreateSuccess(null, stopwatch.Elapsed);
        }

        var parsed = await iniDocumentService.ParseFileAsync(entry.FilePath, cancellationToken).ConfigureAwait(false);
        if (!parsed.Success || parsed.Data == null)
        {
            return OperationResult<IniBlock?>.CreateFailure(parsed.Errors, stopwatch.Elapsed);
        }

        var match = FindBlock(parsed.Data.Blocks, entry);
        return OperationResult<IniBlock?>.CreateSuccess(match == null ? null : CopyBlock(match), stopwatch.Elapsed);
    }

    /// <inheritdoc />
    public async Task<OperationResult<IReadOnlyList<IniReferenceEntry>>> FindReferencersAsync(string name, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var matches = new List<IniReferenceEntry>();
        if (string.IsNullOrWhiteSpace(name))
        {
            return OperationResult<IReadOnlyList<IniReferenceEntry>>.CreateSuccess(matches, stopwatch.Elapsed);
        }

        var target = name.Trim();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CollectDocumentReferencers(target, seen, matches, cancellationToken);
        if (matches.Count < IniConstants.Editor.MaxReferencers)
        {
            await CollectFileReferencersAsync(target, seen, matches, cancellationToken).ConfigureAwait(false);
        }

        return OperationResult<IReadOnlyList<IniReferenceEntry>>.CreateSuccess(matches, stopwatch.Elapsed);
    }

    /// <summary>
    /// Scans content for top-level block headers without fully parsing the document.
    /// </summary>
    /// <param name="content">The INI text.</param>
    /// <returns>The top-level block headers.</returns>
    internal static List<(string BlockType, string Name)> ScanBlockHeaders(string content)
    {
        var headers = new List<(string BlockType, string Name)>();
        var indentStack = new Stack<int>();
        var lines = content.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);
        for (var i = 0; i < lines.Length; i++)
        {
            ProcessScanLine(lines, i, indentStack, headers);
        }

        return headers;
    }

    /// <summary>
    /// Creates a deep copy of a block including comments and children.
    /// </summary>
    /// <param name="block">The block to copy.</param>
    /// <returns>The copied block.</returns>
    internal static IniBlock CopyBlock(IniBlock block)
    {
        var copy = new IniBlock
        {
            BlockType = block.BlockType,
            Name = block.Name,
            AssignmentValue = block.AssignmentValue,
            TrailingComment = block.TrailingComment,
            LineNumber = block.LineNumber,
        };
        copy.LeadingComments.AddRange(block.LeadingComments);
        copy.TrailingComments.AddRange(block.TrailingComments);
        foreach (var field in block.Fields)
        {
            var fieldCopy = new IniField(field.Key, field.Value, field.TrailingComment) { IsBare = field.IsBare };
            fieldCopy.LeadingComments.AddRange(field.LeadingComments);
            copy.Fields.Add(fieldCopy);
        }

        foreach (var child in block.Children)
        {
            copy.Children.Add(CopyBlock(child));
        }

        return copy;
    }

    /// <summary>
    /// Scans content for field value tokens used as reverse-reference prefilter keys.
    /// </summary>
    /// <param name="content">The INI text.</param>
    /// <returns>The distinct value tokens.</returns>
    internal static HashSet<string> ScanValueTokens(string content)
    {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lines = content.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);
        foreach (var raw in lines)
        {
            var line = StripComment(raw);
            var separatorIndex = line.IndexOf(IniConstants.Syntax.KeyValueSeparator);
            if (separatorIndex < 0)
            {
                continue;
            }

            foreach (var token in SplitValueTokens(line[(separatorIndex + 1)..]))
            {
                tokens.Add(token);
            }
        }

        return tokens;
    }

    /// <summary>
    /// Splits a field value into candidate reference tokens.
    /// </summary>
    /// <param name="value">The field value.</param>
    /// <returns>The cleaned tokens.</returns>
    internal static List<string> SplitValueTokens(string value)
    {
        var tokens = new List<string>();
        foreach (var raw in value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var token = raw.Trim('"', '%', ',', ';');
            if (token.Length > 0 && token.Any(char.IsLetter))
            {
                tokens.Add(token);
            }
        }

        return tokens;
    }

    private static void CollectBlockReferencers(
        IReadOnlyList<IniBlock> blocks,
        string target,
        HashSet<string> seen,
        List<IniReferenceEntry> matches,
        IniReferenceSource source,
        string label,
        string? file)
    {
        int remaining = IniConstants.Editor.MaxReferencers - matches.Count;
        if (remaining <= 0)
        {
            return;
        }

        var entries = blocks
            .Where(block => BlockReferences(block, target) && seen.Add(ReferencerKey(block.BlockType, block.Name, file)))
            .Take(remaining)
            .Select(block => new IniReferenceEntry(block.BlockType, block.Name, source, label, file));
        matches.AddRange(entries);
    }

    private static void ProcessScanLine(
        string[] lines,
        int index,
        Stack<int> indentStack,
        List<(string BlockType, string Name)> headers)
    {
        var raw = lines[index];
        var line = StripComment(raw).Trim();
        if (line.Length == 0 || line.StartsWith('#'))
        {
            return;
        }

        if (string.Equals(line, IniConstants.BlockTags.End, StringComparison.OrdinalIgnoreCase))
        {
            if (indentStack.Count > 0)
            {
                indentStack.Pop();
            }

            return;
        }

        if (indentStack.Count > 0)
        {
            var separatorIndex = line.IndexOf(IniConstants.Syntax.KeyValueSeparator);
            if (separatorIndex >= 0)
            {
                var key = line[..separatorIndex].Trim();
                if (IniDocumentService.OpensModuleBlock(key))
                {
                    indentStack.Push(IniDocumentService.GetIndent(raw));
                }
            }
            else if (!IniDocumentService.IsValuelessKey(line) &&
                     IniDocumentService.IsBlockType(line.Split(' ', 2)[0]))
            {
                indentStack.Push(IniDocumentService.GetIndent(raw));
            }

            return;
        }

        if (line.Contains(IniConstants.Syntax.KeyValueSeparator))
        {
            return;
        }

        var tokens = line.Split([' '], StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length > 0)
        {
            headers.Add((tokens[0], tokens.Length > 1 ? string.Join(' ', tokens[1..]) : string.Empty));
            indentStack.Push(IniDocumentService.GetIndent(raw));
        }
    }

    private static IniBlock? FindBlock(List<IniBlock> blocks, IniReferenceEntry entry) =>
        blocks.FirstOrDefault(block =>
            string.Equals(block.BlockType, entry.BlockType, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(block.Name, entry.Name, StringComparison.OrdinalIgnoreCase));

    private static bool BlockReferences(IniBlock block, string name)
    {
        var queue = new Queue<IniBlock>();
        queue.Enqueue(block);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current.Fields.Any(field => SplitValueTokens(field.Value).Any(token => string.Equals(token, name, StringComparison.OrdinalIgnoreCase))))
            {
                return true;
            }

            foreach (var child in current.Children)
            {
                queue.Enqueue(child);
            }
        }

        return false;
    }

    private static string ReferencerKey(string blockType, string name, string? filePath) =>
        $"{blockType}\n{name}\n{filePath}";

    private static string StripComment(string raw)
    {
        var inQuotes = false;
        for (var i = 0; i < raw.Length; i++)
        {
            if (raw[i] == '"')
            {
                inQuotes = !inQuotes;
            }

            if (!inQuotes && raw[i] == IniConstants.Syntax.Comment)
            {
                return raw[..i];
            }
        }

        return raw;
    }

    private static string StableHash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes)[..16];
    }

    private static void AddDocumentEntries(IniDocument document, List<IniReferenceEntry> entries)
    {
        foreach (var block in document.Blocks)
        {
            entries.Add(new IniReferenceEntry(block.BlockType, block.Name, IniReferenceSource.Document, "Document", document.SourcePath));
        }
    }

    private void CollectDocumentReferencers(
        string target,
        HashSet<string> seen,
        List<IniReferenceEntry> matches,
        CancellationToken cancellationToken)
    {
        if (_document == null)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        CollectBlockReferencers(_document.Blocks, target, seen, matches, IniReferenceSource.Document, "Document", _document.SourcePath);
    }

    private async Task CollectFileReferencersAsync(
        string target,
        HashSet<string> seen,
        List<IniReferenceEntry> matches,
        CancellationToken cancellationToken)
    {
        var parsed = 0;
        foreach (var (file, source, tokens) in EnumerateTokenCandidates())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (parsed >= IniConstants.Editor.MaxReverseParseFiles || matches.Count >= IniConstants.Editor.MaxReferencers)
            {
                break;
            }

            if (!tokens.Contains(target))
            {
                continue;
            }

            parsed++;
            var doc = await iniDocumentService.ParseFileAsync(file, cancellationToken).ConfigureAwait(false);
            if (!doc.Success || doc.Data == null)
            {
                continue;
            }

            CollectBlockReferencers(doc.Data.Blocks, target, seen, matches, source, LabelFor(file), file);
        }
    }

    private IEnumerable<(string File, IniReferenceSource Source, HashSet<string> Tokens)> EnumerateTokenCandidates()
    {
        foreach (var pair in _folderTokens)
        {
            yield return (pair.Key, IniReferenceSource.Folder, pair.Value);
        }

        foreach (var pair in _vanillaTokens)
        {
            yield return (pair.Key, IniReferenceSource.Vanilla, pair.Value);
        }
    }

    private string LabelFor(string file)
    {
        var entry = _entries.FirstOrDefault(candidate => string.Equals(candidate.FilePath, file, PathHelper.PathComparison));
        if (entry != null)
        {
            return entry.SourceLabel;
        }

        try
        {
            return new DirectoryInfo(Path.GetDirectoryName(file) ?? file).Name;
        }
        catch (ArgumentException)
        {
            return Path.GetFileName(file);
        }
    }

    private async Task AddCachedFolderEntriesAsync(string? folderPath, bool forceRescan, List<IniReferenceEntry> entries, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
        {
            _folderEntries = [];
            _folderTokens = new(PathHelper.PathComparer);
            _indexedFolderPath = null;
            return;
        }

        if (!forceRescan && string.Equals(_indexedFolderPath, folderPath, PathHelper.PathComparison))
        {
            entries.AddRange(_folderEntries);
            return;
        }

        var scanned = new List<IniReferenceEntry>();
        var tokens = new Dictionary<string, HashSet<string>>(PathHelper.PathComparer);
        await Task.Run(() => AddFolderEntries(folderPath, scanned, tokens, cancellationToken), cancellationToken).ConfigureAwait(false);
        _folderEntries = scanned;
        _folderTokens = tokens;
        _indexedFolderPath = folderPath;
        entries.AddRange(scanned);
    }

    private async Task AddCachedVanillaEntriesAsync(bool forceRescan, List<IniReferenceEntry> entries, CancellationToken cancellationToken)
    {
        if (_vanillaIndexed && !forceRescan)
        {
            entries.AddRange(_vanillaEntries);
            return;
        }

        var scanned = new List<IniReferenceEntry>();
        var tokens = new Dictionary<string, HashSet<string>>(PathHelper.PathComparer);
        await AddVanillaEntriesAsync(scanned, tokens, cancellationToken).ConfigureAwait(false);
        _vanillaEntries = scanned;
        _vanillaTokens = tokens;
        _vanillaIndexed = true;
        entries.AddRange(scanned);
    }

    private void AddFolderEntries(string folderPath, List<IniReferenceEntry> entries, Dictionary<string, HashSet<string>> tokens, CancellationToken cancellationToken)
    {
        var label = new DirectoryInfo(folderPath).Name;
        foreach (var file in Directory.EnumerateFiles(folderPath, ModBuilderConstants.FileNames.IniSearchPattern, ScanEnumerationOptions))
        {
            cancellationToken.ThrowIfCancellationRequested();
            ScanFileInto(file, entries, tokens, IniReferenceSource.Folder, label);
        }
    }

    private async Task AddVanillaEntriesAsync(List<IniReferenceEntry> entries, Dictionary<string, HashSet<string>> tokens, CancellationToken cancellationToken)
    {
        var installations = await installationService.GetAllInstallationsAsync(cancellationToken).ConfigureAwait(false);
        if (!installations.Success || installations.Data == null)
        {
            return;
        }

        foreach (var installation in installations.Data)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await AddVanillaInstallationEntriesAsync(installation, entries, tokens, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task AddVanillaInstallationEntriesAsync(GameInstallation installation, List<IniReferenceEntry> entries, Dictionary<string, HashSet<string>> tokens, CancellationToken cancellationToken)
    {
        var probes = new (string? Directory, string Archive, string Label)[]
        {
            (installation.GeneralsPath, GameClientConstants.GeneralsIniBig, "Generals"),
            (installation.ZeroHourPath, GameClientConstants.ZeroHourIniBig, "Zero Hour"),
        };

        foreach (var (directory, archive, label) in probes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(directory))
            {
                continue;
            }

            var archivePath = Path.Combine(directory, archive);
            if (!File.Exists(archivePath))
            {
                continue;
            }

            var extracted = await EnsureVanillaExtractedAsync(archivePath, cancellationToken).ConfigureAwait(false);
            if (extracted == null)
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(extracted, ModBuilderConstants.FileNames.IniSearchPattern, ScanEnumerationOptions))
            {
                cancellationToken.ThrowIfCancellationRequested();
                ScanFileInto(file, entries, tokens, IniReferenceSource.Vanilla, label);
            }
        }
    }

    private async Task<string?> EnsureVanillaExtractedAsync(string archivePath, CancellationToken cancellationToken)
    {
        var info = new FileInfo(archivePath);
        var cacheDirectory = Path.Combine(
            Path.GetTempPath(),
            IniConstants.Cache.VanillaDirectoryName,
            $"{StableHash(archivePath)}-{info.Length}-{info.LastWriteTimeUtc.Ticks}");
        var markerPath = Path.Combine(cacheDirectory, IniConstants.Cache.ExtractedMarkerFileName);
        if (File.Exists(markerPath))
        {
            return cacheDirectory;
        }

        logger.LogInformation("Extracting vanilla INI archive {Archive} for the reference index", archivePath);
        var result = await archiveService.ExtractBigArchiveAsync(archivePath, cacheDirectory, true, null, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
        {
            logger.LogWarning("Failed to extract vanilla INI archive {Archive}: {Error}", archivePath, result.FirstError);
            return null;
        }

        Directory.CreateDirectory(cacheDirectory);
        await File.WriteAllTextAsync(markerPath, archivePath, cancellationToken).ConfigureAwait(false);
        return cacheDirectory;
    }

    private void ScanFileInto(string file, List<IniReferenceEntry> entries, Dictionary<string, HashSet<string>> tokens, IniReferenceSource source, string label)
    {
        string content = string.Empty;
        try
        {
            content = File.ReadAllText(file);
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Skipping unreadable INI file {File} during reference indexing", file);
            return;
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Skipping unreadable INI file {File} during reference indexing", file);
            return;
        }
        catch (ArgumentException ex)
        {
            logger.LogWarning(ex, "Skipping unreadable INI file {File} during reference indexing", file);
            return;
        }

        foreach (var (blockType, name) in ScanBlockHeaders(content))
        {
            entries.Add(new IniReferenceEntry(blockType, name, source, label, file));
        }

        tokens[file] = ScanValueTokens(content);
    }
}
