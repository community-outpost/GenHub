using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public static async Task PackAsync(string sourceDirectory, string destinationPath)
    {
        var destinationFullPath = Path.GetFullPath(destinationPath);
        var files = Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories)
            .Where(f => !Path.GetFullPath(f).Equals(destinationFullPath, StringComparison.OrdinalIgnoreCase))
            .Select(f => new
            {
                FullPath = f,
                RelativePath = NormalizeBigPath(Path.GetRelativePath(sourceDirectory, f).Replace('/', '\\')),
            })
            .OrderBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var entries = new List<BigFileEntry>();

        // Calculate header size
        // Header: Signature (4) + TotalSize (4) + NumFiles (4) + HeaderSize (4) = 16 bytes
        long headerSize = 16;

        foreach (var file in files)
        {
            var relativePath = file.RelativePath;

            // Validate components are ASCII-only
            if (relativePath.Any(c => c > 127))
            {
                 throw new NotSupportedException($"File path contains non-ASCII characters, which are not supported by the .big format: {relativePath}");
            }

            var encoding = Encoding.ASCII;
            var nameBytes = encoding.GetBytes(relativePath);

            // Entry: Offset (4) + Size (4) + Name (n) + Null Terminator (1)
            headerSize += 4 + 4 + nameBytes.Length + 1;

            entries.Add(new BigFileEntry
            {
                FullPath = file.FullPath,
                RelativePath = relativePath,
                Size = new FileInfo(file.FullPath).Length,
            });
        }

        // Calculate total size and check for BIG format overflow (4GB limit)
        long totalSize = headerSize + entries.Sum(e => e.Size);
        if (totalSize > uint.MaxValue)
        {
            throw new NotSupportedException($"Generated BIG archive size ({totalSize} bytes) exceeds the 4GB limit supported by the .big format.");
        }

        using var fs = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var writer = new BinaryWriter(fs);

        // Write Header
        writer.Write(Encoding.ASCII.GetBytes(Signature));
        WriteUInt32BigEndian(writer, (uint)totalSize);
        WriteUInt32BigEndian(writer, (uint)entries.Count);
        WriteUInt32BigEndian(writer, (uint)headerSize);

        // Calculate initial offset
        long currentOffset = headerSize;

        // Write Index
        foreach (var entry in entries)
        {
            WriteUInt32BigEndian(writer, (uint)currentOffset);
            WriteUInt32BigEndian(writer, (uint)entry.Size);
            writer.Write(Encoding.ASCII.GetBytes(entry.RelativePath));
            writer.Write((byte)0); // Null terminator

            currentOffset += entry.Size;
        }

        // Write Data
        writer.Flush();
        foreach (var entry in entries)
        {
            using var fileStream = File.OpenRead(entry.FullPath);
            if (fileStream.Length != entry.Size)
            {
                throw new IOException($"File size changed during packing for {entry.RelativePath}. Expected {entry.Size} bytes, found {fileStream.Length} bytes.");
            }

            await fileStream.CopyToAsync(fs);
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