using System;
using System.IO;

namespace GenHub.Core.Utilities;

/// <summary>
/// Provides utility methods for detecting and validating BIG format archives based on file headers.
/// </summary>
public static class BigArchiveClassifier
{
    /// <summary>
    /// Magic header length required to inspect BIG format signature (4 bytes).
    /// </summary>
    public const int MagicHeaderLength = 4;

    /// <summary>
    /// Minimum valid BIG archive file size in bytes (16 bytes for header fields).
    /// </summary>
    public const int MinimumBigFileSize = 16;

    /// <summary>
    /// Determines whether the file at <paramref name="filePath"/> starts with the magic bytes
    /// of a BIG archive format (BIG4, BIGF, BIGE, or BIG null byte) and meets minimum size requirements.
    /// </summary>
    /// <param name="filePath">The path of the file to inspect.</param>
    /// <returns>
    /// <c>true</c> if the file exists and its header matches a known BIG archive signature;
    /// otherwise <c>false</c>. Missing, short, or unreadable files safely return <c>false</c>.
    /// </returns>
    public static bool IsBigArchiveFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return false;
        }

        try
        {
            using var stream = new FileStream(
                filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length < MinimumBigFileSize)
            {
                return false;
            }

            Span<byte> header = stackalloc byte[MagicHeaderLength];
            if (stream.Read(header) < MagicHeaderLength)
            {
                return false;
            }

            return HasBigArchiveMagicBytes(header);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>
    /// Determines whether <paramref name="header"/> starts with the magic bytes of a BIG archive.
    /// Accepted signatures start with 'B', 'I', 'G' followed by '4', 'F', 'E', or 0x00.
    /// </summary>
    /// <param name="header">The byte span containing at least <see cref="MagicHeaderLength"/> bytes.</param>
    /// <returns><c>true</c> if the header matches a BIG signature; otherwise <c>false</c>.</returns>
    public static bool HasBigArchiveMagicBytes(ReadOnlySpan<byte> header)
    {
        if (header.Length < MagicHeaderLength)
        {
            return false;
        }

        return header[0] == (byte)'B' &&
               header[1] == (byte)'I' &&
               header[2] == (byte)'G' &&
               (header[3] == (byte)'4' || header[3] == (byte)'F' || header[3] == (byte)'E' || header[3] == 0);
    }
}
