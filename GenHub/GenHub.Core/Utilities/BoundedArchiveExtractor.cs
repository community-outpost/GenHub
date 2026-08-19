using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Exceptions;

namespace GenHub.Core.Utilities;

/// <summary>
/// Streams archive entries to disk under an expansion budget. Sizes recorded in archive headers are
/// attacker-controlled, so the budget is measured against the bytes actually decompressed and the
/// copy aborts the moment it is exceeded.
/// </summary>
public static class BoundedArchiveExtractor
{
    /// <summary>
    /// Copies a decompressed archive entry to <paramref name="destinationPath"/>, aborting as soon as the
    /// per-entry cap or the remaining archive-wide budget is exhausted. When <paramref name="overwrite"/>
    /// is set the entry is staged beside its destination and moved into place only once the copy has
    /// completed, so a failure leaves any pre-existing file intact and removes only what this call wrote.
    /// </summary>
    /// <param name="entryStream">The decompressed entry stream to read from.</param>
    /// <param name="destinationPath">The file to write the entry to.</param>
    /// <param name="entryName">The archive-relative entry name, used in failure messages.</param>
    /// <param name="maxEntryBytes">Maximum number of bytes a single entry may expand to.</param>
    /// <param name="remainingAggregateBytes">Bytes still available in the archive-wide budget.</param>
    /// <param name="overwrite">Whether an existing destination file may be replaced.</param>
    /// <param name="cancellationToken">Token used to cancel the copy.</param>
    /// <returns>The number of bytes written.</returns>
    /// <exception cref="ArchiveExpansionLimitExceededException">Thrown when the budget is already exhausted or the entry expands past it.</exception>
    public static async Task<long> CopyEntryToFileAsync(
        Stream entryStream,
        string destinationPath,
        string entryName,
        long maxEntryBytes,
        long remainingAggregateBytes,
        bool overwrite = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entryStream);

        var limit = Math.Min(maxEntryBytes, remainingAggregateBytes);
        if (limit <= 0)
        {
            throw ArchiveExpansionLimitExceededException.ForSpentBudget(entryName);
        }

        var buffer = new byte[IoConstants.DefaultFileBufferSize];
        long written = 0;

        var writePath = overwrite ? BuildStagingPath(destinationPath) : destinationPath;
        var destination = new FileStream(writePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);

        try
        {
            int read = 0;
            while ((read = await entryStream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                written += read;
                if (written > limit)
                {
                    throw new ArchiveExpansionLimitExceededException(entryName, limit);
                }

                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }

            await destination.DisposeAsync();

            if (overwrite)
            {
                File.Move(writePath, destinationPath, overwrite: true);
            }
        }
        catch
        {
            await DisposeQuietlyAsync(destination);
            DeletePartialOutput(writePath);
            throw;
        }

        return written;
    }

    private static string BuildStagingPath(string destinationPath)
    {
        var directory = Path.GetDirectoryName(destinationPath);
        var stagingName = Path.GetRandomFileName() + IoConstants.StagingFileSuffix;

        return string.IsNullOrEmpty(directory) ? stagingName : Path.Combine(directory, stagingName);
    }

    private static async Task DisposeQuietlyAsync(FileStream destination)
    {
        try
        {
            await destination.DisposeAsync();
        }
        catch (IOException)
        {
            // The failure being handled is the one worth surfacing, not a flush that fails after it.
        }
        catch (UnauthorizedAccessException)
        {
            // The failure being handled is the one worth surfacing, not a flush that fails after it.
        }
    }

    private static void DeletePartialOutput(string writePath)
    {
        try
        {
            if (File.Exists(writePath))
            {
                File.Delete(writePath);
            }
        }
        catch (IOException)
        {
            // Best effort cleanup; the original failure is the one worth surfacing.
        }
        catch (UnauthorizedAccessException)
        {
            // Best effort cleanup; the original failure is the one worth surfacing.
        }
    }
}
