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

        long fileLength = fileStream.Length;
        ValidateHeader(headerBuffer, fileLength, archivePath, out int count, out int headerSize);

        int dirSize = headerSize - 16;
        byte[] dirBuffer = new byte[dirSize];
        int dirRead = fileStream.Read(dirBuffer, 0, dirSize);
        if (dirRead < dirSize)
        {
            throw new InvalidDataException($"'{archivePath}' directory table is truncated.");
        }

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

        if (entry.Size > int.MaxValue)
        {
            throw new InvalidDataException($"Entry '{entry.Path}' in '{entry.ArchivePath}' exceeds maximum supported entry size of 2 GB.");
        }

        using var fileStream = new FileStream(entry.ArchivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        fileStream.Seek(entry.Offset, SeekOrigin.Begin);
        int entrySize = (int)entry.Size;
        byte[] data = new byte[entrySize];
        int read = fileStream.Read(data, 0, entrySize);
        if (read < entrySize)
        {
            throw new InvalidDataException($"Incomplete read for entry '{entry.Path}' in '{entry.ArchivePath}'.");
        }

        return data;
    }

    private static void ValidateHeader(byte[] headerBuffer, long fileLength, string archivePath, out int count, out int headerSize)
    {
        ReadOnlySpan<byte> magic = headerBuffer.AsSpan(0, 4);
        if (!magic.SequenceEqual(BigfMagic) && !magic.SequenceEqual(Big4Magic))
        {
            throw new InvalidDataException($"'{archivePath}' does not contain a valid BIG magic header.");
        }

        uint rawCount = BinaryPrimitives.ReadUInt32BigEndian(headerBuffer.AsSpan(8, 4));
        uint rawHeaderSize = BinaryPrimitives.ReadUInt32BigEndian(headerBuffer.AsSpan(12, 4));

        if (rawHeaderSize < 16 || rawHeaderSize > (ulong)fileLength || rawHeaderSize > int.MaxValue)
        {
            throw new InvalidDataException($"'{archivePath}' contains invalid header size {rawHeaderSize}.");
        }

        headerSize = (int)rawHeaderSize;
        int dirSize = headerSize - 16;

        // Each directory entry requires at least 4 (offset) + 4 (size) + 1 (null terminator) = 9 bytes
        if (rawCount > (uint)(dirSize / 9))
        {
            throw new InvalidDataException($"'{archivePath}' declares entry count {rawCount} which exceeds capacity of directory table size {dirSize}.");
        }

        count = (int)rawCount;
    }

    private static BigArchiveEntry ReadDirectoryEntry(byte[] dirBuffer, ref int pos, int dirSize, long fileLength, string archivePath)
    {
        if (pos + 8 > dirSize)
        {
            throw new InvalidDataException($"'{archivePath}' directory table ended prematurely.");
        }

        long offset = BinaryPrimitives.ReadUInt32BigEndian(dirBuffer.AsSpan(pos, 4));
        long size = BinaryPrimitives.ReadUInt32BigEndian(dirBuffer.AsSpan(pos + 4, 4));
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

        if (offset < 0 || size < 0 || offset + size > fileLength)
        {
            throw new InvalidDataException($"'{archivePath}' entry '{relativePath}' exceeds archive boundaries.");
        }

        return new BigArchiveEntry(relativePath, archivePath, offset, size);
    }
}
