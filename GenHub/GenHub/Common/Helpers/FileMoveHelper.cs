using GenHub.Features.Workspace;
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace GenHub.Common.Helpers;

/// <summary>
/// Moves files so that a failed move leaves only the source behind.
/// </summary>
internal static class FileMoveHelper
{
    private const int EPERM = 1;
    private const int ENOENT = 2;
    private const int EACCES = 13;
    private const int EXDEV = 18;
    private const int EINVAL = 22;
    private const int LinuxENOSYS = 38;
    private const int MacENOTSUP = 45;
    private const int LinuxEOPNOTSUPP = 95;

    /// <summary>
    /// Moves a file to a path that does not exist yet, without ever replacing an existing destination
    /// and without ever leaving a copy behind.
    /// <para>
    /// On Unix, <see cref="File.Move(string, string)"/> copies a file it cannot link and then deletes the source.
    /// When that delete fails, the copy stays behind. This method renames atomically with the no-replace flag instead.
    /// When the file system does not support that rename, it falls back to <see cref="MoveByLink"/>, which never copies.
    /// A file that can be neither renamed nor linked is not moved, so moving across volumes is not supported.
    /// On Windows a same-volume <see cref="File.Move(string, string)"/> is already a plain rename.
    /// </para>
    /// </summary>
    /// <param name="sourcePath">The file to move.</param>
    /// <param name="destinationPath">The new path. It must not exist.</param>
    /// <exception cref="IOException">The destination exists or the move failed.</exception>
    /// <exception cref="UnauthorizedAccessException">The move was not permitted.</exception>
    public static void MoveWithoutResidue(string sourcePath, string destinationPath)
    {
        if (OperatingSystem.IsWindows())
        {
            File.Move(sourcePath, destinationPath);
            return;
        }

        var fullSourcePath = Path.GetFullPath(sourcePath);
        var fullDestinationPath = Path.GetFullPath(destinationPath);
        var error = TryRenameNoReplace(fullSourcePath, fullDestinationPath);
        if (error == 0)
        {
            return;
        }

        if (error is null or EXDEV or EINVAL or LinuxENOSYS or MacENOTSUP or LinuxEOPNOTSUPP)
        {
            MoveByLink(fullSourcePath, fullDestinationPath);
            return;
        }

        throw CreateException(error.Value, sourcePath, destinationPath);
    }

    /// <summary>
    /// Moves a file on Unix by hard-linking it to the new path and then unlinking the source.
    /// <c>link(2)</c> fails when the destination exists, so nothing is replaced, and it never copies.
    /// When the source cannot be unlinked, the link this call created is removed again, so only the source remains.
    /// </summary>
    /// <param name="sourcePath">The file to move.</param>
    /// <param name="destinationPath">The new path. It must not exist.</param>
    /// <exception cref="IOException">The destination exists, the file system has no hard links, or the move failed.</exception>
    /// <exception cref="UnauthorizedAccessException">The move was not permitted.</exception>
    internal static void MoveByLink(string sourcePath, string destinationPath)
    {
        if (UnixNativeMethods.Link(sourcePath, destinationPath) != 0)
        {
            throw CreateException(Marshal.GetLastPInvokeError(), sourcePath, destinationPath);
        }

        try
        {
            File.Delete(sourcePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try
            {
                File.Delete(destinationPath);
            }
            catch (IOException)
            {
                // The source is still in place. The caller rethrows the original failure.
            }
            catch (UnauthorizedAccessException)
            {
                // The source is still in place. The caller rethrows the original failure.
            }

            throw;
        }
    }

    private static int? TryRenameNoReplace(string sourcePath, string destinationPath)
    {
        try
        {
            if (OperatingSystem.IsMacOS())
            {
                return MacOSNativeMethods.RenameNoReplace(sourcePath, destinationPath);
            }

            if (OperatingSystem.IsLinux())
            {
                return LinuxNativeMethods.RenameNoReplace(sourcePath, destinationPath);
            }
        }
        catch (EntryPointNotFoundException)
        {
            // The C library has no no-replace rename.
        }

        return null;
    }

    private static Exception CreateException(int error, string sourcePath, string destinationPath)
    {
        var message = $"Could not move '{sourcePath}' to '{destinationPath}': {Marshal.GetPInvokeErrorMessage(error)}";
        return error switch
        {
            EPERM or EACCES => new UnauthorizedAccessException(message),
            ENOENT => new FileNotFoundException(message, sourcePath),
            _ => new IOException(message),
        };
    }
}
