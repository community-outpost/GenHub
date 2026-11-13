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
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
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
                var indent = IniDocumentService.GetIndent(raw);
                if (IniDocumentService.OpensModuleBlock(key, indentStack.Peek(), indent, lines, index))
                {
                    indentStack.Push(indent);
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

    private async Task AddCachedFolderEntriesAsync(string? folderPath, bool forceRescan, List<IniReferenceEntry> entries, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
        {
            _folderEntries = [];
            _indexedFolderPath = null;
            return;
        }

        if (!forceRescan && string.Equals(_indexedFolderPath, folderPath, PathHelper.PathComparison))
        {
            entries.AddRange(_folderEntries);
            return;
        }

        var scanned = new List<IniReferenceEntry>();
        await Task.Run(() => AddFolderEntries(folderPath, scanned, cancellationToken), cancellationToken).ConfigureAwait(false);
        _folderEntries = scanned;
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
        await AddVanillaEntriesAsync(scanned, cancellationToken).ConfigureAwait(false);
        _vanillaEntries = scanned;
        _vanillaIndexed = true;
        entries.AddRange(scanned);
    }

    private void AddFolderEntries(string folderPath, List<IniReferenceEntry> entries, CancellationToken cancellationToken)
    {
        var label = new DirectoryInfo(folderPath).Name;
        foreach (var file in Directory.EnumerateFiles(folderPath, ModBuilderConstants.FileNames.IniSearchPattern, ScanEnumerationOptions))
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var (blockType, name) in ScanFileHeaders(file))
            {
                entries.Add(new IniReferenceEntry(blockType, name, IniReferenceSource.Folder, label, file));
            }
        }
    }

    private async Task AddVanillaEntriesAsync(List<IniReferenceEntry> entries, CancellationToken cancellationToken)
    {
        var installations = await installationService.GetAllInstallationsAsync(cancellationToken).ConfigureAwait(false);
        if (!installations.Success || installations.Data == null)
        {
            return;
        }

        foreach (var installation in installations.Data)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await AddVanillaInstallationEntriesAsync(installation, entries, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task AddVanillaInstallationEntriesAsync(GameInstallation installation, List<IniReferenceEntry> entries, CancellationToken cancellationToken)
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
                foreach (var (blockType, name) in ScanFileHeaders(file))
                {
                    entries.Add(new IniReferenceEntry(blockType, name, IniReferenceSource.Vanilla, label, file));
                }
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

    private List<(string BlockType, string Name)> ScanFileHeaders(string file)
    {
        try
        {
            var content = File.ReadAllText(file);
            return ScanBlockHeaders(content);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            logger.LogWarning(ex, "Skipping unreadable INI file {File} during reference indexing", file);
            return [];
        }
    }
}
