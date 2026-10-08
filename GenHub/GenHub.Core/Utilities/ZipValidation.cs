using GenHub.Core.Constants;
using System;
using System.IO;

namespace GenHub.Core.Utilities;

/// <summary>
/// Utility methods for ZIP file validation.
/// </summary>
public static class ZipValidation
{
    /// <summary>
    /// Validates if the given file path points to a valid ZIP archive by checking magic bytes.
    /// </summary>
    /// <param name="filePath">The path to the file to validate.</param>
    /// <returns>True if the file appears to be a valid ZIP archive.</returns>
    public static bool IsValidZipFile(string filePath)
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            if (stream.Length < 4)
            {
                return false;
            }

            var buffer = new byte[4];
            if (stream.Read(buffer, 0, 4) < 4)
            {
                return false;
            }

            return HasZipSignature(buffer);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Checks for a complete ZIP local-file or empty-archive signature.
    /// </summary>
    /// <param name="header">The initial archive bytes.</param>
    /// <returns>True if the header starts with a supported ZIP signature.</returns>
    public static bool HasZipSignature(ReadOnlySpan<byte> header) =>
        header.StartsWith(ArchiveSignatureConstants.ZipLocalFileHeader) ||
        header.StartsWith(ArchiveSignatureConstants.ZipEndOfCentralDirectory);
}
