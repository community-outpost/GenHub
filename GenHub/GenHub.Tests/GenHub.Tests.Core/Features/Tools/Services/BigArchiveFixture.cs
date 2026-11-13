using System.Buffers.Binary;
using System.Text;

namespace GenHub.Tests.Core.Features.Tools.Services;

/// <summary>
/// Writes minimal valid BIG archives for checksum and filesystem tests.
/// </summary>
internal static class BigArchiveFixture
{
    /// <summary>
    /// Writes a BIG archive containing the given entries.
    /// </summary>
    /// <param name="archivePath">The output archive path.</param>
    /// <param name="entries">Entry paths paired with ASCII contents.</param>
    public static void Write(string archivePath, params (string EntryPath, string Contents)[] entries)
    {
        var payloads = entries.Select(e => Encoding.ASCII.GetBytes(e.Contents)).ToList();
        var headers = new List<byte[]>(entries.Length);
        foreach (var (entryPath, _) in entries)
        {
            var nameBytes = Encoding.ASCII.GetBytes(entryPath);
            headers.Add(new byte[8 + nameBytes.Length + 1]);
            nameBytes.CopyTo(headers[^1], 8);
        }

        var headerSize = 16 + headers.Sum(h => h.Length);
        var totalSize = headerSize + payloads.Sum(p => p.Length);

        using var output = new MemoryStream();
        output.Write(Encoding.ASCII.GetBytes("BIGF"));
        Span<byte> counts = stackalloc byte[12];
        BinaryPrimitives.WriteInt32BigEndian(counts.Slice(0, 4), totalSize);
        BinaryPrimitives.WriteInt32BigEndian(counts.Slice(4, 4), entries.Length);
        BinaryPrimitives.WriteInt32BigEndian(counts.Slice(8, 4), headerSize);
        output.Write(counts);

        var runningOffset = headerSize;
        for (int i = 0; i < headers.Count; i++)
        {
            BinaryPrimitives.WriteInt32BigEndian(headers[i].AsSpan(0, 4), runningOffset);
            BinaryPrimitives.WriteInt32BigEndian(headers[i].AsSpan(4, 4), payloads[i].Length);
            output.Write(headers[i]);
            runningOffset += payloads[i].Length;
        }

        foreach (var payload in payloads)
        {
            output.Write(payload);
        }

        File.WriteAllBytes(archivePath, output.ToArray());
    }
}
