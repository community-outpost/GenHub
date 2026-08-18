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
    /// per-entry cap or the remaining archive-wide budget is exhausted. The partially written file is
    /// deleted before the failure is surfaced.
    /// </summary>
    /// <param name="entryStream">The decompressed entry stream to read from.</param>
    /// <param name="destinationPath">The file to write the entry to.</param>
    /// <param name="entryName">The archive-relative entry name, used in failure messages.</param>
    /// <param name="maxEntryBytes">Maximum number of bytes a single entry may expand to.</param>
    /// <param name="remainingAggregateBytes">Bytes still available in the archive-wide budget.</param>
    /// <param name="overwrite">Whether an existing destination file may be replaced.</param>
    /// <param name="cancellationToken">Token used to cancel the copy.</param>
    /// <returns>The number of bytes written.</returns>
    /// <exception cref="ArchiveExpansionLimitExceededException">Thrown when the entry expands past its budget.</exception>
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
        var buffer = new byte[IoConstants.DefaultFileBufferSize];
        long written = 0;

        var mode = overwrite ? FileMode.Create : FileMode.CreateNew;
        var destination = new FileStream(destinationPath, mode, FileAccess.Write, FileShare.None);

        try
        {
            int read;
            while ((read = await entryStream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                written += read;
                if (written > limit)
                {
                    throw new ArchiveExpansionLimitExceededException(entryName, limit);
                }

                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
        }
        catch
        {
            await destination.DisposeAsync();
            DeletePartialOutput(destinationPath);
            throw;
        }

        await destination.DisposeAsync();
        return written;
    }

    private static void DeletePartialOutput(string destinationPath)
    {
        try
        {
            if (File.Exists(destinationPath))
            {
                File.Delete(destinationPath);
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
