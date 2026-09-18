using System;
using System.IO;
using System.IO.Compression;

namespace GenHub.Core.Helpers;

/// <summary>
/// Hardened ZIP extraction guard against zip-slip path traversal.
/// </summary>
public static class ZipArchiveGuard
{
    /// <summary>
    /// Extracts a ZIP archive while validating that no entry escapes the destination directory.
    /// </summary>
    /// <param name="zipPath">The archive file path.</param>
    /// <param name="destinationDirectory">The extraction root directory.</param>
    public static void ExtractToDirectory(string zipPath, string destinationDirectory)
    {
        var fullDestination = Path.GetFullPath(destinationDirectory);
        Directory.CreateDirectory(fullDestination);

        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name) && entry.FullName.EndsWith("/", StringComparison.Ordinal))
            {
                continue;
            }

            var targetPath = Path.GetFullPath(Path.Combine(fullDestination, entry.FullName));
            if (!targetPath.StartsWith(fullDestination + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                && !string.Equals(targetPath, fullDestination, StringComparison.Ordinal))
            {
                throw new IOException($"Zip entry escapes destination directory: {entry.FullName}");
            }

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(targetPath);
                continue;
            }

            var entryDir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(entryDir))
            {
                Directory.CreateDirectory(entryDir);
            }

            entry.ExtractToFile(targetPath, overwrite: true);
        }
    }
}
