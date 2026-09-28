using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Helpers;

/// <summary>
/// Writes files atomically through a temporary sibling plus move, so a failed,
/// cancelled, or crashed write never leaves a truncated destination behind.
/// </summary>
public static class AtomicFile
{
    private static readonly Encoding Utf8WithoutBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Writes bytes to a file atomically.
    /// </summary>
    /// <param name="path">The destination path.</param>
    /// <param name="bytes">The bytes to write.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public static async Task WriteAllBytesAsync(string path, byte[] bytes, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(bytes);

        string? tempPath = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            tempPath = CreateTempPath(path);
            await File.WriteAllBytesAsync(tempPath, bytes, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(tempPath, path, overwrite: true);
            tempPath = null;
        }
        finally
        {
            DeleteQuietly(tempPath);
        }
    }

    /// <summary>
    /// Writes text to a file atomically using UTF-8 without a byte order mark.
    /// </summary>
    /// <param name="path">The destination path.</param>
    /// <param name="text">The text to write.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public static Task WriteAllTextAsync(string path, string text, CancellationToken cancellationToken = default)
    {
        return WriteAllTextAsync(path, text, Utf8WithoutBom, cancellationToken);
    }

    /// <summary>
    /// Writes text to a file atomically using the specified encoding.
    /// </summary>
    /// <param name="path">The destination path.</param>
    /// <param name="text">The text to write.</param>
    /// <param name="encoding">The text encoding to write with.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public static async Task WriteAllTextAsync(string path, string text, Encoding encoding, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(encoding);

        string? tempPath = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            tempPath = CreateTempPath(path);
            await File.WriteAllTextAsync(tempPath, text, encoding, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(tempPath, path, overwrite: true);
            tempPath = null;
        }
        finally
        {
            DeleteQuietly(tempPath);
        }
    }

    private static string CreateTempPath(string path)
    {
        string fileName = Path.GetFileName(path) + "." + Path.GetRandomFileName() + ".tmp";
        string? directory = Path.GetDirectoryName(path);
        return string.IsNullOrEmpty(directory) ? fileName : Path.Combine(directory, fileName);
    }

    private static void DeleteQuietly(string? tempPath)
    {
        if (tempPath is null)
        {
            return;
        }

        try
        {
            File.Delete(tempPath);
        }
        catch (IOException)
        {
            // Best effort cleanup of the temp file; the write result is already decided.
        }
        catch (UnauthorizedAccessException)
        {
            // Best effort cleanup of the temp file; the write result is already decided.
        }
    }
}
