using GenHub.Core.Helpers;
using GenHub.Core.Utilities;
using SharpCompress.Archives;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.ModBuilder.Services;

/// <summary>
/// Shared guarded archive extraction for ModBuilder acquisitions.
/// </summary>
internal static class ModBuilderArchiveExtractor
{
    private const long MaxEntryBytes = 1024L * 1024 * 1024;
    private const long MaxTotalBytes = 2048L * 1024 * 1024;

    /// <summary>
    /// Extracts an archive into a directory, skipping directory entries, non-extractable
    /// names, and entries that would escape the destination.
    /// </summary>
    /// <param name="archivePath">The archive file path.</param>
    /// <param name="destinationDirectory">The extraction root.</param>
    /// <param name="cancellationToken">A token to cancel extraction.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    internal static async Task ExtractArchiveFileAsync(
        string archivePath,
        string destinationDirectory,
        CancellationToken cancellationToken)
    {
        var fileInfo = new FileInfo(archivePath);
        using var archive = ArchiveFactory.OpenArchive(fileInfo);
        long remainingAggregateBytes = MaxTotalBytes;
        foreach (var entry in archive.Entries)
        {
            if (entry.IsDirectory)
            {
                continue;
            }

            if (!ArchiveEntryName.IsExtractable(entry.Key))
            {
                continue;
            }

            var targetPath = Path.GetFullPath(Path.Combine(destinationDirectory, entry.Key));
            if (!PathHelper.IsPathWithinDirectory(destinationDirectory, targetPath))
            {
                continue;
            }

            var entryDir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(entryDir))
            {
                Directory.CreateDirectory(entryDir);
            }

            using var entryStream = entry.OpenEntryStream();
            var written = await BoundedArchiveExtractor.CopyEntryToFileAsync(
                entryStream,
                targetPath,
                entry.Key,
                MaxEntryBytes,
                remainingAggregateBytes,
                overwrite: true,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            remainingAggregateBytes -= written;
        }
    }
}
