using System.IO.Compression;

namespace GenHub.Tests.Core.Infrastructure;

/// <summary>
/// Builds archive fixtures for extraction tests.
/// </summary>
internal static class ArchiveFixtures
{
    private const int EndOfCentralDirectoryLength = 22;
    private const int CentralDirectoryOffsetField = 16;
    private const int CentralUncompressedSizeField = 24;
    private const int CentralLocalHeaderOffsetField = 42;
    private const int LocalUncompressedSizeField = 22;
    private const int EndOfCentralDirectorySignature = 0x06054b50;
    private const int CentralDirectorySignature = 0x02014b50;
    private const int LocalFileHeaderSignature = 0x04034b50;

    /// <summary>
    /// Writes a single-entry archive that advertises a harmless size and then inflates to a much
    /// larger one, which is the shape of a hostile archive that only gives itself away part-way
    /// through decompression.
    /// </summary>
    /// <param name="archivePath">The archive to write.</param>
    /// <param name="entryName">The name of the single entry.</param>
    /// <param name="actualBytes">The number of bytes the entry really decompresses to.</param>
    /// <param name="declaredBytes">The size the archive headers advertise.</param>
    public static void CreateWithSpoofedEntrySize(
        string archivePath,
        string entryName,
        int actualBytes,
        int declaredBytes)
    {
        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
            using var entryStream = entry.Open();
            entryStream.Write(new byte[actualBytes]);
        }

        // Rewrite the uncompressed-size fields in both the central directory record and the local
        // file header. Offsets follow the ZIP layout: the end-of-central-directory record ends the
        // file and points at the central directory, whose record points back at the local header.
        var bytes = File.ReadAllBytes(archivePath);
        var endOfCentralDirectory = bytes.Length - EndOfCentralDirectoryLength;
        RequireSignature(bytes, endOfCentralDirectory, EndOfCentralDirectorySignature, "end-of-central-directory record");

        var centralDirectory = BitConverter.ToInt32(bytes, endOfCentralDirectory + CentralDirectoryOffsetField);
        RequireSignature(bytes, centralDirectory, CentralDirectorySignature, "central directory record");

        var localHeader = BitConverter.ToInt32(bytes, centralDirectory + CentralLocalHeaderOffsetField);
        RequireSignature(bytes, localHeader, LocalFileHeaderSignature, "local file header");

        BitConverter.GetBytes(declaredBytes).CopyTo(bytes, centralDirectory + CentralUncompressedSizeField);
        BitConverter.GetBytes(declaredBytes).CopyTo(bytes, localHeader + LocalUncompressedSizeField);
        File.WriteAllBytes(archivePath, bytes);
    }

    private static void RequireSignature(byte[] bytes, int offset, int signature, string recordName)
    {
        if (offset < 0 || offset + sizeof(int) > bytes.Length ||
            BitConverter.ToInt32(bytes, offset) != signature)
        {
            throw new InvalidOperationException(
                $"Expected a ZIP {recordName} at offset {offset}. The layout written by ZipFile has drifted, " +
                "so patching these offsets would corrupt the fixture instead of resizing its entry.");
        }
    }
}
