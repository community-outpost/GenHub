namespace GenHub.Core.Services.Tools.WorldBuilder;

/// <summary>
/// Atomic file writes: stage to a temp file beside the target, then move
/// into place so a crash mid-write never corrupts the destination.
/// </summary>
internal static class AtomicFile
{
    /// <summary>
    /// Writes bytes atomically.
    /// </summary>
    /// <param name="path">Destination path.</param>
    /// <param name="data">Bytes to write.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    internal static async Task WriteBytesAsync(string path, byte[] data, CancellationToken cancellationToken)
    {
        var tempPath = $"{path}.tmp.{Guid.NewGuid():N}";
        try
        {
            await File.WriteAllBytesAsync(tempPath, data, cancellationToken).ConfigureAwait(false);
            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            DeleteTemp(tempPath);
        }
    }

    /// <summary>
    /// Writes text atomically.
    /// </summary>
    /// <param name="path">Destination path.</param>
    /// <param name="text">Text to write.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    internal static async Task WriteTextAsync(string path, string text, CancellationToken cancellationToken)
    {
        var tempPath = $"{path}.tmp.{Guid.NewGuid():N}";
        try
        {
            await File.WriteAllTextAsync(tempPath, text, cancellationToken).ConfigureAwait(false);
            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            DeleteTemp(tempPath);
        }
    }

    private static void DeleteTemp(string tempPath)
    {
        if (!File.Exists(tempPath))
        {
            return;
        }

        try
        {
            File.Delete(tempPath);
        }
        catch (IOException ex)
        {
            // Best-effort temp cleanup after a failed move; the temp file is harmless.
            _ = ex;
        }
        catch (UnauthorizedAccessException ex)
        {
            // Best-effort temp cleanup after a failed move; the temp file is harmless.
            _ = ex;
        }
    }
}
