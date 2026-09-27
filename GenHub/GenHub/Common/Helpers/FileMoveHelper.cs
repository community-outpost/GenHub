using GenHub.Features.Workspace;
using System;
using System.IO;

namespace GenHub.Common.Helpers;

/// <summary>
/// Moves files so that a failed move leaves only the source behind.
/// </summary>
internal static class FileMoveHelper
{
    /// <summary>
    /// Moves a file to a path that does not exist yet. On Unix, <see cref="File.Move(string, string)"/> copies the file
    /// when it cannot link it and then deletes the source. When that delete fails, the copy is removed here before the
    /// exception is rethrown, so the source is the only file left.
    /// </summary>
    /// <param name="sourcePath">The file to move.</param>
    /// <param name="destinationPath">The new path. It must not exist.</param>
    /// <exception cref="IOException">The destination exists or the move failed.</exception>
    /// <exception cref="UnauthorizedAccessException">The move was not permitted.</exception>
    public static void MoveWithoutResidue(string sourcePath, string destinationPath)
    {
        if (File.Exists(destinationPath) || Directory.Exists(destinationPath))
        {
            throw new IOException($"The destination '{destinationPath}' already exists.");
        }

        try
        {
            File.Move(sourcePath, destinationPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (File.Exists(sourcePath) && File.Exists(destinationPath))
            {
                RemovePartialCopy(destinationPath);
            }

            throw;
        }
    }

    private static void RemovePartialCopy(string path)
    {
        try
        {
            File.Delete(path);
            return;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The copy carries the source's flags, so an immutable source leaves an immutable copy.
            if (!MacOSNativeMethods.TryClearFileFlags(path))
            {
                return;
            }
        }

        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The caller rethrows the original move failure.
        }
    }
}
