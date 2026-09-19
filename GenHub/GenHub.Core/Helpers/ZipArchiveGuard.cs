using GenHub.Core.Constants;
using System;
using System.IO;
using System.IO.Compression;

namespace GenHub.Core.Helpers;

/// <summary>
/// Hardened ZIP extraction guard against zip-slip path traversal and decompression bombs.
/// </summary>
public static class ZipArchiveGuard
{
    /// <summary>
    /// Extracts a ZIP archive while validating that no entry escapes the destination directory
    /// and that expansion stays within entry-count, per-entry, and aggregate size limits.
    /// </summary>
    /// <param name="zipPath">The archive file path.</param>
    /// <param name="destinationDirectory">The extraction root directory.</param>
    public static void ExtractToDirectory(string zipPath, string destinationDirectory)
    {
        var fullDestination = Path.GetFullPath(destinationDirectory);
        Directory.CreateDirectory(fullDestination);

        using var archive = ZipFile.OpenRead(zipPath);
        if (archive.Entries.Count > ValidationLimits.MaxZipArchiveEntries)
        {
            throw new IOException($"Zip archive exceeds entry count limit of {ValidationLimits.MaxZipArchiveEntries}.");
        }

        long totalBytes = 0;
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

            totalBytes = CopyEntryWithinLimits(entry, targetPath, totalBytes);
        }
    }

    private static long CopyEntryWithinLimits(ZipArchiveEntry entry, string targetPath, long totalBytes)
    {
        bool exceeded = false;
        long entryBytes = 0;
        using (var source = entry.Open())
        using (var destination = File.Create(targetPath))
        {
            var buffer = new byte[ValidationLimits.ZipCopyBufferSize];
            int read;
            while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
            {
                entryBytes += read;
                totalBytes += read;
                if (entryBytes > ValidationLimits.MaxZipArchiveEntryBytes || totalBytes > ValidationLimits.MaxZipArchiveTotalBytes)
                {
                    exceeded = true;
                    break;
                }

                destination.Write(buffer, 0, read);
            }
        }

        if (exceeded)
        {
            DeleteBestEffort(targetPath);
            throw new IOException($"Zip entry exceeds extraction size limits: {entry.FullName}");
        }

        return totalBytes;
    }

    private static void DeleteBestEffort(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Best-effort cleanup of the partial extraction; the size-limit error below is what matters.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort cleanup of the partial extraction; the size-limit error below is what matters.
        }
    }
}
