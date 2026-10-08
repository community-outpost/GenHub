using System;
using System.IO;
using System.Security;
using System.Text;

namespace GenHub.Features.AppUpdate.Services;

/// <summary>
/// Utility to extract embedded Velopack .nupkg release packages from Windows Setup executables.
/// </summary>
public static class VelopackBundleExtractor
{
    private const uint ZipLocalHeaderSignature = 0x04034b50; // 'PK\x03\x04'

    // Velopack Windows Setup bundles append the .nupkg to Setup.exe with a 48-byte header:
    // 8 bytes package offset (little-endian), 8 bytes package length (little-endian),
    // followed by this 32-byte SHA-256 signature for "squirrel bundle".
    private const int BundleHeaderPrefixBytes = 16;

    private const int MinimumBundleFileBytes = 64;

    private const int CopyBufferBytes = 81920;

    private static readonly byte[] SquirrelBundleSignature =
    [
        0x94, 0xf0, 0xb1, 0x7b, 0x68, 0x93, 0xe0, 0x29,
        0x37, 0xeb, 0x34, 0xef, 0x53, 0xaa, 0xe7, 0xd4,
        0x2b, 0x54, 0xf5, 0x70, 0x7e, 0xf5, 0xd6, 0xf5,
        0x78, 0x54, 0x98, 0x3e, 0x5e, 0x94, 0xed, 0x7d,
    ];

    /// <summary>
    /// Attempts to extract the embedded Velopack .nupkg release package from an installer executable.
    /// </summary>
    /// <param name="exePath">Path to the installer executable.</param>
    /// <param name="destinationNupkgPath">Destination path for the extracted .nupkg file.</param>
    /// <param name="extractedBytes">When successful, the number of bytes extracted.</param>
    /// <returns>True if an embedded Velopack bundle was successfully located and extracted; otherwise, false.</returns>
    public static bool TryExtractBundle(string exePath, string destinationNupkgPath, out long extractedBytes)
    {
        extractedBytes = 0;
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
        {
            return false;
        }

        try
        {
            using var fileStream = new FileStream(exePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (fileStream.Length < MinimumBundleFileBytes)
            {
                return false;
            }

            var position = FindSignature(fileStream, SquirrelBundleSignature);
            if (position < BundleHeaderPrefixBytes)
            {
                return false;
            }

            fileStream.Seek(position - BundleHeaderPrefixBytes, SeekOrigin.Begin);
            using var reader = new BinaryReader(fileStream, Encoding.UTF8, leaveOpen: true);
            var offset = reader.ReadInt64();
            var length = reader.ReadInt64();

            if (offset <= 0 || length <= 0 || offset > fileStream.Length || length > fileStream.Length - offset)
            {
                return false;
            }

            fileStream.Seek(offset, SeekOrigin.Begin);
            var zipHeader = reader.ReadUInt32();
            if (zipHeader != ZipLocalHeaderSignature)
            {
                return false;
            }

            fileStream.Seek(offset, SeekOrigin.Begin);
            var destDir = Path.GetDirectoryName(destinationNupkgPath);
            if (!string.IsNullOrEmpty(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            var extracted = false;
            try
            {
                using var outStream = new FileStream(destinationNupkgPath, FileMode.Create, FileAccess.Write, FileShare.None);
                CopyExactBytes(fileStream, outStream, length);
                extracted = true;
            }
            finally
            {
                if (!extracted)
                {
                    TryDeleteFile(destinationNupkgPath);
                }
            }

            extractedBytes = length;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or ArgumentException or EndOfStreamException)
        {
            return false;
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Best effort cleanup of partial extraction output
        }
    }

    private static long FindSignature(FileStream stream, byte[] pattern)
    {
        const int bufferSize = 64 * 1024;
        var buffer = new byte[bufferSize];
        var patternLength = pattern.Length;
        long totalRead = 0;
        var bytesRead = 0;

        stream.Seek(0, SeekOrigin.Begin);
        while ((bytesRead = stream.Read(buffer, 0, bufferSize)) > 0)
        {
            for (var i = 0; i <= bytesRead - patternLength; i++)
            {
                if (MatchesPattern(buffer, i, pattern))
                {
                    return totalRead + i;
                }
            }

            if (bytesRead == bufferSize)
            {
                stream.Seek(-(patternLength - 1), SeekOrigin.Current);
                totalRead += bytesRead - (patternLength - 1);
            }
            else
            {
                totalRead += bytesRead;
            }
        }

        return -1;
    }

    private static bool MatchesPattern(byte[] buffer, int offset, byte[] pattern)
    {
        for (var j = 0; j < pattern.Length; j++)
        {
            if (buffer[offset + j] != pattern[j])
            {
                return false;
            }
        }

        return true;
    }

    private static void CopyExactBytes(Stream source, Stream destination, long count)
    {
        var buffer = new byte[CopyBufferBytes];
        var remaining = count;
        while (remaining > 0)
        {
            var toRead = (int)Math.Min(remaining, buffer.Length);
            var read = source.Read(buffer, 0, toRead);
            if (read == 0)
            {
                throw new EndOfStreamException($"Unexpected end of stream while copying bundle: {remaining} bytes remaining.");
            }

            destination.Write(buffer, 0, read);
            remaining -= read;
        }
    }
}
