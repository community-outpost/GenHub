using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Content.Services.CommunityOutpost;

/// <summary>
/// Packs files into a .big archive format (Generals/Zero Hour).
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

    private static (List<BigFileEntry> Entries, long HeaderSize, long TotalSize) CollectBigEntries(
        string sourceDirectory,
        string destinationFullPath,
        string? targetArchiveFullPath,
        CancellationToken cancellationToken)
    {
        var files = Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories)
            .Where(f => IsEligibleBigSourceFile(f, destinationFullPath, targetArchiveFullPath))
            .Select(f => new
            {
                FullPath = f,
                RelativePath = NormalizeBigPath(Path.GetRelativePath(sourceDirectory, f).Replace('/', '\\')),
            })
            .OrderBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var entries = new List<BigFileEntry>();
        long headerSize = 16;

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relativePath = file.RelativePath;
            if (relativePath.Any(c => c > 127))
            {
                throw new NotSupportedException($"File path contains non-ASCII characters, which are not supported by the .big format: {relativePath}");
            }

            var nameBytes = Encoding.ASCII.GetBytes(relativePath);
            headerSize += 4 + 4 + nameBytes.Length + 1;

            entries.Add(new BigFileEntry
            {
                FullPath = file.FullPath,
                RelativePath = relativePath,
                Size = new FileInfo(file.FullPath).Length,
            });
        }

        long totalSize = headerSize + entries.Sum(e => e.Size);
        if (totalSize > uint.MaxValue)
        {
            throw new NotSupportedException($"Generated BIG archive size ({totalSize} bytes) exceeds the 4GB limit supported by the .big format.");
        }

        return (entries, headerSize, totalSize);
    }

    private static bool IsEligibleBigSourceFile(string filePath, string destinationFullPath, string? targetArchiveFullPath)
    {
        var full = Path.GetFullPath(filePath);
        if (full.Equals(destinationFullPath, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (targetArchiveFullPath != null && full.Equals(targetArchiveFullPath, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !full.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task WriteBigArchiveAsync(
        Stream outputStream,
        List<BigFileEntry> entries,
        long headerSize,
        long totalSize,
        CancellationToken cancellationToken)
    {
        using var writer = new BinaryWriter(outputStream, Encoding.ASCII, leaveOpen: true);

        // Write Header
        writer.Write(Encoding.ASCII.GetBytes(Signature));
        WriteUInt32BigEndian(writer, (uint)totalSize);
        WriteUInt32BigEndian(writer, (uint)entries.Count);
        WriteUInt32BigEndian(writer, (uint)headerSize);

        // Write Index
        long currentOffset = headerSize;
        foreach (var entry in entries)
        {
            WriteUInt32BigEndian(writer, (uint)currentOffset);
            WriteUInt32BigEndian(writer, (uint)entry.Size);
            writer.Write(Encoding.ASCII.GetBytes(entry.RelativePath));
            writer.Write((byte)0);

            currentOffset += entry.Size;
        }

        writer.Flush();

        // Write Data
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var fileStream = File.OpenRead(entry.FullPath);
            if (fileStream.Length != entry.Size)
            {
                throw new IOException($"File size changed during packing for {entry.RelativePath}. Expected {entry.Size} bytes, found {fileStream.Length} bytes.");
            }

            await fileStream.CopyToAsync(outputStream, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Writes a 32-bit unsigned integer in big-endian format.
    /// </summary>
    /// <param name="writer">The binary writer.</param>
    /// <param name="value">The value to write.</param>
    private static void WriteUInt32BigEndian(BinaryWriter writer, uint value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(buffer, value);
        writer.Write(buffer);
    }

    private static string NormalizeBigPath(string relativePath)
    {
        var path = relativePath.TrimStart('.', '\\').Replace('/', '\\');

        if (path.StartsWith("ZH\\BIG", StringComparison.OrdinalIgnoreCase))
        {
            path = path[6..].TrimStart('\\', ' ');
        }
        else if (path.StartsWith("CCG\\BIG", StringComparison.OrdinalIgnoreCase))
        {
            path = path[7..].TrimStart('\\', ' ');
        }
        else if (path.StartsWith("BIG", StringComparison.OrdinalIgnoreCase))
        {
            path = path[3..].TrimStart('\\', ' ');
        }

        // If the path still contains extra leading folders, cut to known game roots
        var roots = KnownRoots;

        var bestIndex = -1;
        foreach (var root in roots)
        {
            var idx = path.IndexOf(root, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0 && (bestIndex < 0 || idx < bestIndex))
            {
                bestIndex = idx;
            }
        }

        var result = bestIndex > 0 ? path[bestIndex..] : path;
        return string.IsNullOrWhiteSpace(result) ? path : result;
    }

    private class BigFileEntry
    {
        public string FullPath { get; set; } = string.Empty;

        public string RelativePath { get; set; } = string.Empty;

        public long Size { get; set; }
    }
}