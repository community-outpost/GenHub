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

            // Check for ZIP magic bytes: 50 4B 03 04 (local file header) or 50 4B 05 06 (end of central directory)
            return (buffer[0] == 0x50 && buffer[1] == 0x4B && buffer[2] == 0x03 && buffer[3] == 0x04) ||
                   (buffer[0] == 0x50 && buffer[1] == 0x4B && buffer[2] == 0x05 && buffer[3] == 0x06);
        }
        catch
        {
            return false;
        }
    }
}