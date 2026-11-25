using ImageMagick;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace GenHub.Tests.Core.Features.Tools.WndEditor.TestSupport;

/// <summary>
/// Shared fixtures for WND editor asset pipeline tests: minimal .BIG archives
/// and solid-color PNG textures with pixel-level assertions.
/// </summary>
internal static class WndTestAssets
{
    /// <summary>
    /// Creates a valid .BIG archive containing arbitrary file entries.
    /// </summary>
    /// <param name="archivePath">The output archive path.</param>
    /// <param name="entries">The relative path and data pairs to pack.</param>
    internal static void CreateBigArchive(string archivePath, params (string RelPath, byte[] Data)[] entries)
    {
        // Big format:
        // 0..3: "BIG4"
        // 4..7: total file size (uint32 LittleEndian)
        // 8..11: entry count (uint32 BigEndian)
        // 12..15: header size (uint32 BigEndian) = 16 + directory table size
        // Directory table: for each entry:
        //   4 bytes: offset (BigEndian)
        //   4 bytes: size (BigEndian)
        //   null-terminated relative path
        // File data at specified offsets
        var dirEntries = new List<(string Path, byte[] Data, int PathBytesLength)>();
        int dirTableSize = 0;
        foreach (var (relPath, data) in entries)
        {
            var normalized = relPath.Replace('/', '\\');
            var pathBytesLen = Encoding.Latin1.GetByteCount(normalized) + 1; // including null terminator
            dirEntries.Add((normalized, data, pathBytesLen));
            dirTableSize += 8 + pathBytesLen;
        }

        int headerSize = 16 + dirTableSize;
        int currentOffset = headerSize;
        var entryOffsets = new List<int>();

        foreach (var (_, data, _) in dirEntries)
        {
            entryOffsets.Add(currentOffset);
            currentOffset += data.Length;
        }

        int totalFileSize = currentOffset;

        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        // Magic "BIG4"
        writer.Write((byte)'B');
        writer.Write((byte)'I');
        writer.Write((byte)'G');
        writer.Write((byte)'4');

        // Total file size (Little Endian uint32)
        writer.Write((uint)totalFileSize);

        // Entry count (Big Endian uint32)
        byte[] countBytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(countBytes, (uint)entries.Length);
        writer.Write(countBytes);

        // Header size (Big Endian uint32)
        byte[] headerSizeBytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(headerSizeBytes, (uint)headerSize);
        writer.Write(headerSizeBytes);

        // Directory table
        byte[] u32Buf = new byte[4];
        for (int i = 0; i < dirEntries.Count; i++)
        {
            var (path, data, _) = dirEntries[i];
            int offset = entryOffsets[i];

            // Offset BigEndian
            BinaryPrimitives.WriteUInt32BigEndian(u32Buf, (uint)offset);
            writer.Write(u32Buf);

            // Size BigEndian
            BinaryPrimitives.WriteUInt32BigEndian(u32Buf, (uint)data.Length);
            writer.Write(u32Buf);

            // Path null-terminated Latin1
            writer.Write(Encoding.Latin1.GetBytes(path));
            writer.Write((byte)0);
        }

        // File payload data
        foreach (var (_, data, _) in dirEntries)
        {
            writer.Write(data);
        }

        writer.Flush();
        File.WriteAllBytes(archivePath, ms.ToArray());
    }

    /// <summary>
    /// Creates a solid-color PNG for texture fixtures with specified dimensions.
    /// </summary>
    /// <param name="color">The solid color.</param>
    /// <param name="width">The image width.</param>
    /// <param name="height">The image height.</param>
    /// <returns>The PNG bytes.</returns>
    internal static byte[] CreateSolidPng(MagickColor color, uint width = 1, uint height = 1)
    {
        ArgumentNullException.ThrowIfNull(color);
        using var image = new MagickImage(color, width, height);
        return image.ToByteArray(MagickFormat.Png);
    }

    /// <summary>
    /// Compares two PNGs pixel-by-pixel using root mean square error (0 means identical).
    /// </summary>
    /// <param name="actual">The actual PNG bytes.</param>
    /// <param name="expected">The expected PNG bytes.</param>
    /// <returns>The root mean square error between the two images.</returns>
    internal static double ComparePngs(byte[] actual, byte[] expected)
    {
        ArgumentNullException.ThrowIfNull(actual);
        ArgumentNullException.ThrowIfNull(expected);
        using var actualImage = new MagickImage(actual);
        using var expectedImage = new MagickImage(expected);
        return actualImage.Compare(expectedImage, ErrorMetric.RootMeanSquared);
    }
}
