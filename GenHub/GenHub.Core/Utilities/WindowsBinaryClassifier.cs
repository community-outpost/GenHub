using System;
using System.IO;

namespace GenHub.Core.Utilities;

/// <summary>
/// Provides utility methods for detecting Windows binaries based on file headers.
/// </summary>
public static class WindowsBinaryClassifier
{
    /// <summary>
    /// Magic header length required to inspect the Windows executable signature (2 bytes).
    /// </summary>
    public const int MagicHeaderLength = 2;

    /// <summary>
    /// Determines whether the file at <paramref name="filePath"/> starts with the magic bytes
    /// of a Windows executable (the 'M', 'Z' DOS header signature).
    /// </summary>
    /// <param name="filePath">The path of the file to inspect.</param>
    /// <returns>
    /// <c>true</c> if the file exists and its header matches the Windows executable signature;
    /// otherwise <c>false</c>. Missing, short, or unreadable files safely return <c>false</c>.
    /// </returns>
    public static bool IsWindowsBinary(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return false;
        }

        try
        {
            using var stream = new FileStream(
                filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            Span<byte> header = stackalloc byte[MagicHeaderLength];
            if (stream.Read(header) < MagicHeaderLength)
            {
                return false;
            }

            return HasWindowsBinaryMagicBytes(header);
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
    /// Determines whether <paramref name="header"/> starts with the magic bytes of a Windows executable.
    /// Accepted signature is 'M', 'Z'.
    /// </summary>
    /// <param name="header">The byte span containing at least <see cref="MagicHeaderLength"/> bytes.</param>
    /// <returns><c>true</c> if the header matches the Windows executable signature; otherwise <c>false</c>.</returns>
    public static bool HasWindowsBinaryMagicBytes(ReadOnlySpan<byte> header)
    {
        if (header.Length < MagicHeaderLength)
        {
            return false;
        }

        return header[0] == (byte)'M' &&
               header[1] == (byte)'Z';
    }
}
