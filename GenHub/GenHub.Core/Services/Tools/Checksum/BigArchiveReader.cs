using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace GenHub.Core.Services.Tools.Checksum;

/// <summary>
/// Parser and reader for SAGE .BIG archive files.
/// </summary>
public static class BigArchiveReader
{
    private static readonly byte[] BigfMagic = [(byte)'B', (byte)'I', (byte)'G', (byte)'F'];
    private static readonly byte[] Big4Magic = [(byte)'B', (byte)'I', (byte)'G', (byte)'4'];

    /// <summary>
    /// Reads and indexes the directory table from a .BIG archive file.
    /// </summary>
    /// <param name="archivePath">The path to the .BIG archive.</param>
    /// <returns>A dictionary of normalized lowercase relative paths to archive entries.</returns>
    /// <exception cref="InvalidDataException">Thrown if the header or directory structure is invalid.</exception>
    public static Dictionary<string, BigArchiveEntry> ReadIndex(string archivePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);

        byte[] headerBuffer = new byte[16];
        using var fileStream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        int bytesRead = fileStream.Read(headerBuffer, 0, 16);
        if (bytesRead < 16)
        {
            throw new InvalidDataException($"'{archivePath}' is too small to be a valid BIG archive.");
        }

        ValidateHeader(headerBuffer, archivePath, out int count, out int headerSize);

        int dirSize = headerSize - 16;
        byte[] dirBuffer = new byte[dirSize];
        int dirRead = fileStream.Read(dirBuffer, 0, dirSize);
        if (dirRead < dirSize)
        {
            throw new InvalidDataException($"'{archivePath}' directory table is truncated.");
        }

        long fileLength = fileStream.Length;
        var entries = new Dictionary<string, BigArchiveEntry>(count, StringComparer.OrdinalIgnoreCase);
        int pos = 0;

        for (int i = 0; i < count; i++)
        {
            var entry = ReadDirectoryEntry(dirBuffer, ref pos, dirSize, fileLength, archivePath);
            entries[entry.Path.ToLowerInvariant()] = entry;
        }

        return entries;
    }

    /// <summary>
    /// Reads the raw byte contents of a specific entry from a .BIG archive.
    /// </summary>
    /// <param name="entry">The entry to read.</param>
    /// <returns>The uncompressed file bytes.</returns>
    public static byte[] ReadEntryData(BigArchiveEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        using var fileStream = new FileStream(entry.ArchivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        fileStream.Seek(entry.Offset, SeekOrigin.Begin);
        byte[] data = new byte[entry.Size];
        int read = fileStream.Read(data, 0, entry.Size);
        if (read < entry.Size)
        {
            throw new InvalidDataException($"Incomplete read for entry '{entry.Path}' in '{entry.ArchivePath}'.");
        }

        return data;
    }

    private static void ValidateHeader(byte[] headerBuffer, string archivePath, out int count, out int headerSize)
    {
        ReadOnlySpan<byte> magic = headerBuffer.AsSpan(0, 4);
        if (!magic.SequenceEqual(BigfMagic) && !magic.SequenceEqual(Big4Magic))
        {
            throw new InvalidDataException($"'{archivePath}' does not contain a valid BIG magic header.");
        }

        count = unchecked((int)BinaryPrimitives.ReadUInt32BigEndian(headerBuffer.AsSpan(8, 4)));
        headerSize = unchecked((int)BinaryPrimitives.ReadUInt32BigEndian(headerBuffer.AsSpan(12, 4)));

        if (count < 0 || headerSize < 16)
        {
            throw new InvalidDataException($"'{archivePath}' contains invalid header entry counts or sizes.");
        }
    }

    private static BigArchiveEntry ReadDirectoryEntry(byte[] dirBuffer, ref int pos, int dirSize, long fileLength, string archivePath)
    {
        if (pos + 8 > dirSize)
        {
            throw new InvalidDataException($"'{archivePath}' directory table ended prematurely.");
        }

        int offset = unchecked((int)BinaryPrimitives.ReadUInt32BigEndian(dirBuffer.AsSpan(pos, 4)));
        int size = unchecked((int)BinaryPrimitives.ReadUInt32BigEndian(dirBuffer.AsSpan(pos + 4, 4)));
        pos += 8;

        int nameStart = pos;
        while (pos < dirSize && dirBuffer[pos] != 0)
        {
            pos++;
        }

        if (pos >= dirSize)
        {
            throw new InvalidDataException($"'{archivePath}' entry path is not null-terminated.");
        }

        string relativePath = Encoding.Latin1.GetString(dirBuffer, nameStart, pos - nameStart).Replace('/', '\\');
        pos++; // Skip null terminator

        if (offset < 0 || size < 0 || offset + (long)size > fileLength)
        {
            throw new InvalidDataException($"'{archivePath}' entry '{relativePath}' exceeds archive boundaries.");
        }

        return new BigArchiveEntry(relativePath, archivePath, offset, size);
    }
}
