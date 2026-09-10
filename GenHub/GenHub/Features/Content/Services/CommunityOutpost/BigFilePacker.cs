using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Models.CommunityOutpost;

namespace GenHub.Features.Content.Services.CommunityOutpost;

/// <summary>
/// Packs and unpacks files in the .big archive format (Generals/Zero Hour).
/// </summary>
public static class BigFilePacker
{
    private const string Signature = "BIGF";

    private static readonly string[] KnownRoots =
    [
        "Data\\",
        "Art\\",
        "Audio\\",
        "W3D\\",
        "Textures\\",
        "Shaders\\",
        "Maps\\",
        "INI\\",
    ];

    /// <summary>
    /// Packs the contents of a directory into a .big file.
    /// </summary>
    /// <param name="sourceDirectory">The directory containing files to pack.</param>
    /// <param name="destinationPath">The output .big file path.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public static Task PackAsync(string sourceDirectory, string destinationPath, CancellationToken cancellationToken = default)
        => PackAsync(sourceDirectory, destinationPath, null, cancellationToken);

    /// <summary>
    /// Packs the contents of a directory into a .big file, excluding temporary and target archive files.
    /// </summary>
    /// <param name="sourceDirectory">The directory containing files to pack.</param>
    /// <param name="destinationPath">The output .big file path.</param>
    /// <param name="targetArchivePath">Optional target archive path to exclude if packing in-place.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public static async Task PackAsync(string sourceDirectory, string destinationPath, string? targetArchivePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var destinationFullPath = Path.GetFullPath(destinationPath);
        var targetArchiveFullPath = !string.IsNullOrEmpty(targetArchivePath) ? Path.GetFullPath(targetArchivePath) : null;
        var (entries, headerSize, totalSize) = CollectBigEntries(sourceDirectory, destinationFullPath, targetArchiveFullPath, cancellationToken);

        var destinationDir = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(destinationDir) && !Directory.Exists(destinationDir))
        {
            Directory.CreateDirectory(destinationDir);
        }

        var tempPath = destinationPath + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";

        try
        {
            await using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await WriteBigArchiveAsync(fs, entries, headerSize, totalSize, cancellationToken).ConfigureAwait(false);
            }

            File.Move(tempPath, destinationPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch
                {
                    // Ignore temporary file deletion failure
                }
            }
        }
    }

    /// <summary>
    /// Unpacks a .big archive into the destination directory.
    /// </summary>
    /// <param name="bigPath">Path to the .big archive.</param>
    /// <param name="destinationDirectory">The destination folder to extract files to.</param>
    /// <param name="overwrite">Whether to overwrite existing files.</param>
    /// <param name="progress">Optional progress reporter (0.0 to 1.0).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of files extracted.</returns>
    public static async Task<int> UnpackAsync(
        string bigPath,
        string destinationDirectory,
        bool overwrite = true,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(bigPath))
        {
            throw new FileNotFoundException($"BIG file not found: {bigPath}", bigPath);
        }

        Directory.CreateDirectory(destinationDirectory);
        var destFullPath = Path.GetFullPath(destinationDirectory);
        var destFullPathWithSep = destFullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

        await using var fs = new FileStream(bigPath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true);

        List<BigArchiveEntryInfo> entries;
        var (reader, entryCount) = ReadAndValidateBigHeader(fs);
        using (reader)
        {
            entries = ReadBigArchiveEntries(fs, reader, entryCount, validateOffsets: true, cancellationToken);
        }

        return await ExtractEntriesAsync(fs, entries, destFullPath, destFullPathWithSep, overwrite, progress, cancellationToken).ConfigureAwait(false);
    }

    private static (BinaryReader Reader, uint EntryCount) ReadAndValidateBigHeader(FileStream fs)
    {
        if (fs.Length < 16)
        {
            throw new InvalidDataException($"BIG archive is too small to contain a valid header: {fs.Length} bytes.");
        }

        var reader = new BinaryReader(fs, Encoding.ASCII, leaveOpen: true);

        var sigBytes = reader.ReadBytes(4);
        var sig = Encoding.ASCII.GetString(sigBytes);
        if (!string.Equals(sig, "BIGF", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(sig, "BIG4", StringComparison.OrdinalIgnoreCase))
        {
            reader.Dispose();
            throw new InvalidDataException($"Invalid BIG archive signature: '{sig}'. Expected 'BIGF' or 'BIG4'.");
        }

        _ = reader.ReadUInt32();

        var uintBuffer = new byte[4];
        if (reader.Read(uintBuffer, 0, 4) < 4)
        {
            reader.Dispose();
            throw new EndOfStreamException("Unexpected end of file while reading BIG entry count.");
        }

        var entryCount = BinaryPrimitives.ReadUInt32BigEndian(uintBuffer);

        if (reader.Read(uintBuffer, 0, 4) < 4)
        {
            reader.Dispose();
            throw new EndOfStreamException("Unexpected end of file while reading BIG header size.");
        }

        _ = BinaryPrimitives.ReadUInt32BigEndian(uintBuffer);

        return (reader, entryCount);
    }

    private static List<BigArchiveEntryInfo> ReadBigArchiveEntries(
        FileStream fs,
        BinaryReader reader,
        uint entryCount,
        bool validateOffsets,
        CancellationToken cancellationToken)
    {
        var uintBuffer = new byte[4];
        var entries = new List<BigArchiveEntryInfo>((int)Math.Min(entryCount, 100000));

        for (var i = 0; i < entryCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (reader.Read(uintBuffer, 0, 4) < 4)
            {
                throw new EndOfStreamException($"Unexpected end of stream while reading offset for BIG entry {i}.");
            }

            var offset = BinaryPrimitives.ReadUInt32BigEndian(uintBuffer);

            if (reader.Read(uintBuffer, 0, 4) < 4)
            {
                throw new EndOfStreamException($"Unexpected end of stream while reading size for BIG entry {i}.");
            }

            var size = BinaryPrimitives.ReadUInt32BigEndian(uintBuffer);

            if (validateOffsets && (ulong)offset + size > (ulong)fs.Length)
            {
                throw new InvalidDataException($"Entry {i} has offset ({offset}) and size ({size}) exceeding archive length ({fs.Length}).");
            }

            var pathBytes = new List<byte>(64);
            int b;
            while ((b = fs.ReadByte()) > 0)
            {
                pathBytes.Add((byte)b);
            }

            if (b < 0 && pathBytes.Count == 0)
            {
                throw new EndOfStreamException($"Unexpected end of stream while reading name for BIG entry {i}.");
            }

            var relPath = Encoding.ASCII.GetString(pathBytes.ToArray());
            entries.Add(new BigArchiveEntryInfo(relPath, offset, size));
        }

        return entries;
    }

    private static async Task<int> ExtractEntriesAsync(
        FileStream fs,
        List<BigArchiveEntryInfo> entries,
        string destFullPath,
        string destFullPathWithSep,
        bool overwrite,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var extractedCount = 0;
        var buffer = new byte[64 * 1024];

        for (var i = 0; i < entries.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = entries[i];

            if (TryGetValidTargetPath(entry.RelativePath, destFullPath, destFullPathWithSep, overwrite, out var targetPath))
            {
                await ExtractSingleEntryAsync(fs, entry, targetPath!, buffer, cancellationToken).ConfigureAwait(false);
                extractedCount++;
            }

            progress?.Report((double)(i + 1) / entries.Count);
        }

        return extractedCount;
    }

    private static bool TryGetValidTargetPath(
        string relPath,
        string destFullPath,
        string destFullPathWithSep,
        bool overwrite,
        out string? targetPath)
    {
        targetPath = null;
        var normalizedRel = relPath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(normalizedRel))
        {
            normalizedRel = normalizedRel.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        var fullPath = Path.GetFullPath(Path.Combine(destFullPath, normalizedRel));
        var relCheck = Path.GetRelativePath(destFullPath, fullPath);

        if (!fullPath.StartsWith(destFullPathWithSep, StringComparison.OrdinalIgnoreCase) ||
            relCheck == "." ||
            relCheck == ".." ||
            relCheck.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            relCheck.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal) ||
            Path.IsPathRooted(relCheck))
        {
            // Path traversal protection
            return false;
        }

        if (Directory.Exists(fullPath))
        {
            return false;
        }

        if (File.Exists(fullPath) && !overwrite)
        {
            return false;
        }

        targetPath = fullPath;
        return true;
    }

    private static async Task ExtractSingleEntryAsync(
        FileStream fs,
        BigArchiveEntryInfo entry,
        string targetPath,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        var targetDir = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(targetDir))
        {
            Directory.CreateDirectory(targetDir);
        }

        fs.Seek(entry.Offset, SeekOrigin.Begin);

        await using var outFs = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true);
        long remaining = entry.Size;
        while (remaining > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var toRead = (int)Math.Min(buffer.Length, remaining);
            var read = await fs.ReadAsync(buffer.AsMemory(0, toRead), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            await outFs.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            remaining -= read;
        }

        if (remaining > 0)
        {
            throw new EndOfStreamException($"Archive truncated while extracting entry '{entry.RelativePath}'. Expected {entry.Size} bytes, read {entry.Size - remaining} bytes.");
        }
    }

    /// <summary>
    /// Unpacks multiple .big archives sequentially into the destination directory.
    /// Later archives overwrite conflicting files from earlier ones.
    /// </summary>
    /// <param name="bigPaths">Collection of paths to .big archives.</param>
    /// <param name="destinationDirectory">The destination directory where files will be written.</param>
    /// <param name="overwrite">Whether to overwrite existing files.</param>
    /// <param name="progress">Optional aggregated progress reporter (0.0 to 1.0).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The total count of extracted files across all archives.</returns>
    public static async Task<int> UnpackMultipleAsync(
        IEnumerable<string> bigPaths,
        string destinationDirectory,
        bool overwrite = true,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var pathsList = bigPaths.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        if (pathsList.Count == 0)
        {
            return 0;
        }

        var totalExtracted = 0;
        for (var i = 0; i < pathsList.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var p = pathsList[i];
            var baseProgress = (double)i / pathsList.Count;
            var stepProgress = 1.0 / pathsList.Count;

            var subProgress = progress != null
                ? new Progress<double>(val => progress.Report(baseProgress + (val * stepProgress)))
                : null;

            totalExtracted += await UnpackAsync(p, destinationDirectory, overwrite, subProgress, cancellationToken).ConfigureAwait(false);
        }

        progress?.Report(1.0);
        return totalExtracted;
    }

    /// <summary>
    /// Reads entry metadata from a .big archive without extracting files.
    /// </summary>
    /// <param name="bigPath">Path to the .big archive.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A read-only list of entry metadata.</returns>
    public static async Task<IReadOnlyList<BigArchiveEntryInfo>> ReadEntriesAsync(string bigPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(bigPath))
        {
            throw new FileNotFoundException($"BIG file not found: {bigPath}", bigPath);
        }

        await using var fs = new FileStream(bigPath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true);
        var (reader, entryCount) = ReadAndValidateBigHeader(fs);
        using (reader)
        {
            return ReadBigArchiveEntries(fs, reader, entryCount, validateOffsets: false, cancellationToken);
        }
    }

    private static (List<BigFileEntry> Entries, long HeaderSize, long TotalSize) CollectBigEntries(
        string sourceDirectory,
        string destinationFullPath,
        string? targetArchiveFullPath,
        CancellationToken cancellationToken)
    {
        var relativePaths = EnumerateBigFiles(sourceDirectory, cancellationToken);

        var candidateEntries = new List<(string FullPath, string NormalizedRelPath, long Size)>();

        foreach (var relPath in relativePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fullPath = Path.Combine(sourceDirectory, relPath);
            if (!IsEligibleBigSourceFile(fullPath, destinationFullPath, targetArchiveFullPath))
            {
                continue;
            }

            var normalizedRelPath = NormalizeBigPath(relPath.Replace('/', '\\'));
            if (normalizedRelPath.Any(c => c > 127))
            {
                throw new NotSupportedException($"File path contains non-ASCII characters, which are not supported by the .big format: {normalizedRelPath}");
            }

            candidateEntries.Add((fullPath, normalizedRelPath, new FileInfo(fullPath).Length));
        }

        // Deterministic ordinal sort by normalized backslash relative path across all platforms;
        // break ties with FullPath for total ordering
        candidateEntries.Sort((a, b) =>
        {
            var cmp = string.Compare(a.NormalizedRelPath, b.NormalizedRelPath, StringComparison.Ordinal);
            return cmp != 0 ? cmp : string.Compare(a.FullPath, b.FullPath, StringComparison.Ordinal);
        });

        // De-duplicate colliding normalized relative paths to guarantee total order and prevent duplicate archive entries
        var uniqueEntries = new List<(string FullPath, string NormalizedRelPath, long Size)>(candidateEntries.Count);
        var seenRelPaths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in candidateEntries)
        {
            if (seenRelPaths.Add(entry.NormalizedRelPath))
            {
                uniqueEntries.Add(entry);
            }
        }

        var entries = new List<BigFileEntry>(uniqueEntries.Count);
        long headerSize = 16;

        foreach (var (fullPath, normalizedRelPath, size) in uniqueEntries)
        {
            var nameBytes = Encoding.ASCII.GetBytes(normalizedRelPath);
            headerSize += 4 + 4 + nameBytes.Length + 1;

            entries.Add(new BigFileEntry
            {
                FullPath = fullPath,
                RelativePath = normalizedRelPath,
                Size = size,
            });
        }

        headerSize += 8; // SBigLastHeader (unknown1: uint32 + unknown2: uint32)

        long totalSize = headerSize + entries.Sum(e => e.Size);
        if (totalSize > uint.MaxValue)
        {
            throw new NotSupportedException($"Generated BIG archive size ({totalSize} bytes) exceeds the 4GB limit supported by the .big format.");
        }

        return (entries, headerSize, totalSize);
    }

    private static List<string> EnumerateBigFiles(string rootDirectory, CancellationToken cancellationToken)
    {
        var results = new List<string>();
        EnumerateBigFilesCore(rootDirectory, string.Empty, 0, results, cancellationToken);
        return results;
    }

    private static void EnumerateBigFilesCore(
        string rootDirectory,
        string relativeDir,
        int depth,
        List<string> results,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var currentDir = string.IsNullOrEmpty(relativeDir)
            ? rootDirectory
            : Path.Combine(rootDirectory, relativeDir);

        if (!Directory.Exists(currentDir) || depth > 32)
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(currentDir))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileName = Path.GetFileName(file);
            results.Add(string.IsNullOrEmpty(relativeDir) ? fileName : Path.Combine(relativeDir, fileName));
        }

        foreach (var subDir in Directory.EnumerateDirectories(currentDir))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var dirName = Path.GetFileName(subDir);
            var nextRelative = string.IsNullOrEmpty(relativeDir) ? dirName : Path.Combine(relativeDir, dirName);
            EnumerateBigFilesCore(rootDirectory, nextRelative, depth + 1, results, cancellationToken);
        }
    }

    private static bool IsEligibleBigSourceFile(string fullPath, string destinationFullPath, string? targetArchiveFullPath)
    {
        fullPath = Path.GetFullPath(fullPath);
        destinationFullPath = Path.GetFullPath(destinationFullPath);
        if (targetArchiveFullPath != null)
        {
            targetArchiveFullPath = Path.GetFullPath(targetArchiveFullPath);
        }

        if (string.Equals(fullPath, destinationFullPath, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (targetArchiveFullPath != null &&
            string.Equals(fullPath, targetArchiveFullPath, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var fileName = Path.GetFileName(fullPath);
        if (string.IsNullOrEmpty(fileName))
        {
            return false;
        }

        if (fileName.StartsWith('.') ||
            fileName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".bak", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fileName, "Thumbs.db", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fileName, "desktop.ini", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static string NormalizeBigPath(string relativePath)
    {
        var normalized = relativePath.TrimStart('\\');

        var bestIndex = -1;
        foreach (var root in KnownRoots)
        {
            var idx = normalized.IndexOf(root, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0 && (bestIndex == -1 || idx < bestIndex))
            {
                bestIndex = idx;
            }
        }

        if (bestIndex >= 0)
        {
            return normalized[bestIndex..];
        }

        return normalized;
    }

    private static async Task WriteBigArchiveAsync(
        Stream stream,
        List<BigFileEntry> entries,
        long headerSize,
        long totalSize,
        CancellationToken cancellationToken)
    {
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);

        // Header: Signature (4 bytes)
        writer.Write(Encoding.ASCII.GetBytes(Signature));

        // Header: Total archive size (4 bytes, Little Endian)
        writer.Write((uint)totalSize);

        // Header: Number of files (4 bytes, Big Endian)
        WriteUInt32BigEndian(writer, (uint)entries.Count);

        // Header: Size of header block (4 bytes, Big Endian)
        WriteUInt32BigEndian(writer, (uint)headerSize);

        // File entries in header
        uint currentOffset = (uint)headerSize;
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Offset to file data (4 bytes, Big Endian)
            WriteUInt32BigEndian(writer, currentOffset);

            // File size (4 bytes, Big Endian)
            WriteUInt32BigEndian(writer, (uint)entry.Size);

            // Null-terminated file path
            var pathBytes = Encoding.ASCII.GetBytes(entry.RelativePath);
            writer.Write(pathBytes);
            writer.Write((byte)0);

            currentOffset += (uint)entry.Size;
        }

        // SBigLastHeader trailer (8 bytes: unknown1: uint32 + unknown2: uint32)
        writer.Write(0u);
        writer.Write(0u);

        // File contents
        var buffer = new byte[64 * 1024];
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await using var fileStream = new FileStream(entry.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (fileStream.Length != entry.Size)
            {
                throw new InvalidOperationException($"File '{entry.FullPath}' size changed from {entry.Size} to {fileStream.Length} during packing.");
            }

            int bytesRead;
            while ((bytesRead = await fileStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
            {
                await stream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
            }
        }

        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void WriteUInt32BigEndian(BinaryWriter writer, uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        writer.Write(bytes);
    }

    private sealed class BigFileEntry
    {
        public string FullPath { get; set; } = string.Empty;

        public string RelativePath { get; set; } = string.Empty;

        public long Size { get; set; }
    }
}
