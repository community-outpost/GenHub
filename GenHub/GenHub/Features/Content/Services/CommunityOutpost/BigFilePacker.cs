using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Models.CommunityOutpost;
using GenHub.Core.Models.Tools.ModBuilder;

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
        "Window\\",
    ];

    /// <summary>
    /// Packs the contents of a directory into a .big file.
    /// </summary>
    /// <param name="sourceDirectory">The directory containing files to pack.</param>
    /// <param name="destinationPath">The output .big file path.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of duplicate or colliding entries dropped during packing.</returns>
    public static Task<int> PackAsync(string sourceDirectory, string destinationPath, CancellationToken cancellationToken = default)
        => PackAsync(sourceDirectory, destinationPath, null, manifest: null, cancellationToken);

    /// <summary>
    /// Packs the contents of a directory into a .big file, excluding temporary and target archive files.
    /// </summary>
    /// <param name="sourceDirectory">The directory containing files to pack.</param>
    /// <param name="destinationPath">The output .big file path.</param>
    /// <param name="targetArchivePath">Optional target archive path to exclude if packing in-place.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of duplicate or colliding entries dropped during packing.</returns>
    public static Task<int> PackAsync(string sourceDirectory, string destinationPath, string? targetArchivePath, CancellationToken cancellationToken = default)
        => PackAsync(sourceDirectory, destinationPath, targetArchivePath, manifest: null, cancellationToken);

    /// <summary>
    /// Packs the contents of a directory into a .big file with optional manifest-guided ordering and header metadata for byte-for-byte reproducibility.
    /// </summary>
    /// <param name="sourceDirectory">The directory containing files to pack.</param>
    /// <param name="destinationPath">The output .big file path.</param>
    /// <param name="targetArchivePath">Optional target archive path to exclude if packing in-place.</param>
    /// <param name="manifest">Optional archive manifest specifying entry ordering, trailer bytes, and header overrides.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of duplicate or colliding entries dropped during packing.</returns>
    public static async Task<int> PackAsync(
        string sourceDirectory,
        string destinationPath,
        string? targetArchivePath,
        BigArchiveManifest? manifest,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var destinationFullPath = Path.GetFullPath(destinationPath);
        var targetArchiveFullPath = !string.IsNullOrEmpty(targetArchivePath) ? Path.GetFullPath(targetArchivePath) : null;
        var (entries, headerSize, totalSize, trailerBytes, duplicateCount) = CollectBigEntries(
            sourceDirectory, destinationFullPath, targetArchiveFullPath, manifest, cancellationToken);

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
                await WriteBigArchiveAsync(fs, entries, headerSize, totalSize, trailerBytes, cancellationToken).ConfigureAwait(false);
            }

            File.Move(tempPath, destinationPath, overwrite: true);
            return duplicateCount;
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
    /// Extracts a byte-for-byte reproducibility manifest from a .big file.
    /// </summary>
    /// <param name="bigPath">Path to the .big file.</param>
    /// <returns>A BigArchiveManifest describing the archive layout.</returns>
    public static BigArchiveManifest ExtractManifest(string bigPath)
    {
        using var stream = File.OpenRead(bigPath);
        return ExtractManifest(stream, Path.GetFileName(bigPath));
    }

    /// <summary>
    /// Extracts a byte-for-byte reproducibility manifest from a .big stream.
    /// </summary>
    /// <param name="stream">The stream to read.</param>
    /// <param name="bigFileName">Optional filename of the .big archive.</param>
    /// <returns>A BigArchiveManifest describing the archive layout.</returns>
    public static BigArchiveManifest ExtractManifest(Stream stream, string? bigFileName = null)
    {
        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
        var signatureBytes = reader.ReadBytes(4);
        var signature = Encoding.ASCII.GetString(signatureBytes);
        if (!string.Equals(signature, "BIGF", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(signature, "BIG4", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Invalid BIG header signature: {signature}");
        }

        _ = reader.ReadUInt32(); // totalSize
        var entryCount = ReadUInt32BigEndian(reader);
        var headerSize = ReadUInt32BigEndian(reader);

        var entries = new List<(string Name, uint Offset, uint Size)>();
        for (int i = 0; i < entryCount; i++)
        {
            var offset = ReadUInt32BigEndian(reader);
            var size = ReadUInt32BigEndian(reader);
            var nameBytes = new List<byte>();
            byte entryByte;
            while ((entryByte = reader.ReadByte()) != 0)
            {
                nameBytes.Add(entryByte);
            }

            var name = Encoding.ASCII.GetString(nameBytes.ToArray());
            entries.Add((name, offset, size));
        }

        var trailerStart = stream.Position;
        uint firstDataOffset = entries.Count > 0 ? entries[0].Offset : headerSize;
        int trailerLength = (int)(firstDataOffset - trailerStart);
        byte[] trailerBytes = trailerLength > 0 ? reader.ReadBytes(trailerLength) : new byte[8];

        uint? headerOverride = null;
        if (headerSize != (trailerStart + trailerBytes.Length))
        {
            headerOverride = headerSize;
        }

        return new BigArchiveManifest
        {
            BigFileName = bigFileName,
            TrailerHex = Convert.ToHexString(trailerBytes),
            HeaderSizeOverride = headerOverride,
            EntryOrder = entries.Select(e => e.Name).ToList(),
        };
    }

    /// <summary>
    /// Saves the BIG archive manifest to disk as JSON.
    /// </summary>
    /// <param name="manifest">The manifest to save.</param>
    /// <param name="outputPath">The output file path.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous save operation.</returns>
    public static async Task SaveManifestAsync(BigArchiveManifest manifest, string outputPath, CancellationToken cancellationToken = default)
    {
        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(outputPath, json, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Loads a BIG archive manifest from disk if it exists.
    /// </summary>
    /// <param name="manifestPath">Path to the manifest JSON file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The loaded manifest or null if not found.</returns>
    public static async Task<BigArchiveManifest?> LoadManifestAsync(string manifestPath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(manifestPath))
        {
            return null;
        }

        var json = await File.ReadAllTextAsync(manifestPath, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<BigArchiveManifest>(json);
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

        List<BigArchiveEntryInfo> entries = [];
        var (reader, entryCount) = ReadAndValidateBigHeader(fs);
        using (reader)
        {
            entries = ReadBigArchiveEntries(fs, reader, entryCount, validateOffsets: true, cancellationToken);
        }

        return await ExtractEntriesAsync(fs, entries, destFullPath, destFullPathWithSep, overwrite, progress, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Unpacks multiple .big archives sequentially into a single destination directory.
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

        return totalExtracted;
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
                throw new InvalidDataException(
                    $"BIG entry {i} data range [{offset}..{offset + size}) exceeds archive size ({fs.Length} bytes). Archive may be corrupted or truncated.");
            }

            var nameBytes = new List<byte>(128);
            while (true)
            {
                var b = reader.ReadByte();
                if (b == 0)
                {
                    break;
                }

                nameBytes.Add(b);
            }

            var relativePath = Encoding.ASCII.GetString(nameBytes.ToArray());

            entries.Add(new BigArchiveEntryInfo(relativePath, offset, size));
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
        var buffer = new byte[64 * 1024];
        var extractedCount = 0;
        var totalEntries = entries.Count;

        for (var i = 0; i < totalEntries; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var entry = entries[i];
            var sanitizedRelativePath = entry.RelativePath.Replace('\\', Path.DirectorySeparatorChar);

            var entryDestPath = Path.GetFullPath(Path.Combine(destFullPath, sanitizedRelativePath));
            if (!entryDestPath.StartsWith(destFullPathWithSep, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(entryDestPath, destFullPath, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"BIG archive entry path traverses outside destination directory: '{entry.RelativePath}' -> '{entryDestPath}'.");
            }

            if (!overwrite && File.Exists(entryDestPath))
            {
                progress?.Report((double)(i + 1) / totalEntries);
                continue;
            }

            var entryDestDir = Path.GetDirectoryName(entryDestPath);
            if (!string.IsNullOrEmpty(entryDestDir) && !Directory.Exists(entryDestDir))
            {
                Directory.CreateDirectory(entryDestDir);
            }

            fs.Seek(entry.Offset, SeekOrigin.Begin);

            await using (var outFs = new FileStream(entryDestPath, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true))
            {
                long remaining = entry.Size;
                while (remaining > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var toRead = (int)Math.Min(remaining, buffer.Length);
                    var bytesRead = await fs.ReadAsync(buffer.AsMemory(0, toRead), cancellationToken).ConfigureAwait(false);
                    if (bytesRead == 0)
                    {
                        throw new EndOfStreamException(
                            $"Unexpected end of stream while reading data for entry '{entry.RelativePath}'. Expected {entry.Size} bytes, got {entry.Size - remaining}.");
                    }

                    await outFs.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
                    remaining -= bytesRead;
                }
            }

            extractedCount++;
            progress?.Report((double)(i + 1) / totalEntries);
        }

        return extractedCount;
    }

    private static List<(string FullPath, string NormalizedRelPath, long Size)> CollectCandidateEntries(
        string sourceDirectory,
        string destinationFullPath,
        string? targetArchiveFullPath,
        CancellationToken cancellationToken)
    {
        var rawRelPaths = EnumerateBigFiles(sourceDirectory, cancellationToken);
        var candidateEntries = new List<(string FullPath, string NormalizedRelPath, long Size)>();

        foreach (var relPath in rawRelPaths)
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

        return candidateEntries;
    }

    private static (List<(string FullPath, string NormalizedRelPath, long Size)> Unique, int DuplicateCount) DeduplicateCandidates(
        IEnumerable<(string FullPath, string NormalizedRelPath, long Size)> candidates)
    {
        var seenRelPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var uniqueCandidates = new List<(string FullPath, string NormalizedRelPath, long Size)>();
        var duplicateCount = 0;

        foreach (var candidate in candidates)
        {
            if (seenRelPaths.Add(candidate.NormalizedRelPath))
            {
                uniqueCandidates.Add(candidate);
            }
            else
            {
                duplicateCount++;
            }
        }

        return (uniqueCandidates, duplicateCount);
    }

    private static byte[] ResolveTrailerBytes(BigArchiveManifest? manifest)
    {
        if (string.IsNullOrEmpty(manifest?.TrailerHex))
        {
            return new byte[8];
        }

        try
        {
            return Convert.FromHexString(manifest.TrailerHex);
        }
        catch (FormatException)
        {
            return new byte[8];
        }
    }

    private static List<BigFileEntry> OrderEntriesByManifest(
        List<(string FullPath, string NormalizedRelPath, long Size)> uniqueCandidateEntries,
        BigArchiveManifest manifest)
    {
        var entries = new List<BigFileEntry>();
        var availableDict = uniqueCandidateEntries.ToDictionary(e => e.NormalizedRelPath, StringComparer.OrdinalIgnoreCase);
        var matchedSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var manifestPath in manifest.EntryOrder)
        {
            var normManifestPath = NormalizeBigPath(manifestPath.Replace('/', '\\'));
            if (availableDict.TryGetValue(normManifestPath, out var foundCandidate))
            {
                entries.Add(new BigFileEntry
                {
                    FullPath = foundCandidate.FullPath,
                    RelativePath = manifestPath.Replace('/', '\\'),
                    Size = foundCandidate.Size,
                });
                matchedSet.Add(normManifestPath);
            }
        }

        var extraEntries = uniqueCandidateEntries.Where(e => !matchedSet.Contains(e.NormalizedRelPath)).ToList();
        extraEntries.Sort((a, b) => string.Compare(a.NormalizedRelPath, b.NormalizedRelPath, StringComparison.Ordinal));
        foreach (var extra in extraEntries)
        {
            entries.Add(new BigFileEntry
            {
                FullPath = extra.FullPath,
                RelativePath = extra.NormalizedRelPath,
                Size = extra.Size,
            });
        }

        return entries;
    }

    private static List<BigFileEntry> OrderBigEntries(
        List<(string FullPath, string NormalizedRelPath, long Size)> uniqueCandidateEntries,
        BigArchiveManifest? manifest)
    {
        if (manifest != null && manifest.EntryOrder.Count > 0)
        {
            return OrderEntriesByManifest(uniqueCandidateEntries, manifest);
        }

        uniqueCandidateEntries.Sort((a, b) =>
        {
            var cmp = string.Compare(a.NormalizedRelPath, b.NormalizedRelPath, StringComparison.Ordinal);
            return cmp != 0 ? cmp : string.Compare(a.FullPath, b.FullPath, StringComparison.Ordinal);
        });

        return uniqueCandidateEntries.Select(candidate => new BigFileEntry
        {
            FullPath = candidate.FullPath,
            RelativePath = candidate.NormalizedRelPath,
            Size = candidate.Size,
        }).ToList();
    }

    private static (List<BigFileEntry> Entries, long HeaderSize, long TotalSize, byte[] TrailerBytes, int DuplicateCount) CollectBigEntries(
        string sourceDirectory,
        string destinationFullPath,
        string? targetArchiveFullPath,
        BigArchiveManifest? manifest,
        CancellationToken cancellationToken)
    {
        var candidateEntries = CollectCandidateEntries(sourceDirectory, destinationFullPath, targetArchiveFullPath, cancellationToken);
        var (uniqueCandidates, duplicateCount) = DeduplicateCandidates(candidateEntries);
        var trailerBytes = ResolveTrailerBytes(manifest);
        var entries = OrderBigEntries(uniqueCandidates, manifest);

        long calculatedTableEnd = 16;
        foreach (var entry in entries)
        {
            var nameBytes = Encoding.ASCII.GetBytes(entry.RelativePath);
            calculatedTableEnd += 4 + 4 + nameBytes.Length + 1;
        }

        long firstDataOffset = calculatedTableEnd + trailerBytes.Length;
        long headerSize = (manifest?.HeaderSizeOverride.HasValue == true && entries.Count == manifest.EntryOrder.Count)
            ? manifest.HeaderSizeOverride.Value
            : firstDataOffset;

        long totalSize = firstDataOffset + entries.Sum(e => e.Size);
        if (totalSize > uint.MaxValue)
        {
            throw new NotSupportedException($"Generated BIG archive size ({totalSize} bytes) exceeds the 4GB limit supported by the .big format.");
        }

        return (entries, headerSize, totalSize, trailerBytes, duplicateCount);
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

        if (depth > 32)
        {
            throw new InvalidOperationException($"Directory nesting depth exceeds maximum limit of 32 levels at '{currentDir}'.");
        }

        if (!Directory.Exists(currentDir))
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
        byte[] trailerBytes,
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
        uint currentOffset = (uint)(16 + entries.Sum(e => 4 + 4 + Encoding.ASCII.GetByteCount(e.RelativePath) + 1) + trailerBytes.Length);
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

        // SBigLastHeader / padding trailer
        writer.Write(trailerBytes);

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

            int bytesRead = 0;
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

    private static uint ReadUInt32BigEndian(BinaryReader reader)
    {
        Span<byte> bytes = stackalloc byte[4];
        if (reader.Read(bytes) < 4)
        {
            throw new EndOfStreamException("Unexpected end of stream while reading big-endian uint32.");
        }

        return BinaryPrimitives.ReadUInt32BigEndian(bytes);
    }

    private sealed class BigFileEntry
    {
        public string FullPath { get; set; } = string.Empty;

        public string RelativePath { get; set; } = string.Empty;

        public long Size { get; set; }
    }
}
