using GenHub.Core.Constants;
using System.IO;

namespace GenHub.Core.Helpers;

/// <summary>
/// Maps artifact file names to MIME content types for publisher catalog artifacts.
/// </summary>
public static class MimeTypeHelper
{
    /// <summary>
    /// Resolves the MIME content type for an artifact file name based on its extension.
    /// </summary>
    /// <param name="fileName">The artifact file name or path. May be null or empty.</param>
    /// <returns>The resolved MIME type. Falls back to binary for unknown extensions.</returns>
    public static string FromFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return HostingConstants.BinaryContentType;
        }

        return Path.GetExtension(fileName.Trim()).ToLowerInvariant() switch
        {
            ".zip" => HostingConstants.ZipContentType,
            ".rar" => HostingConstants.RarContentType,
            ".7z" => HostingConstants.SevenZipContentType,
            ".tar" => HostingConstants.TarContentType,
            ".gz" => HostingConstants.GzipContentType,
            ".exe" => HostingConstants.ExecutableContentType,
            ".msi" => HostingConstants.MsiContentType,
            ".json" => HostingConstants.JsonContentType,
            ".txt" => HostingConstants.TextContentType,
            ".md" => HostingConstants.MarkdownContentType,
            _ => HostingConstants.BinaryContentType,
        };
    }
}
