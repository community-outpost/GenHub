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
    /// Moves a file to a path that does not exist yet, without ever replacing an existing destination.
    /// <para>
    /// On Unix, <see cref="File.Move(string, string)"/> copies a file it cannot link and then deletes the source.
    /// When that delete fails, the copy stays behind. This method renames atomically with the no-replace flag instead,
    /// so a failed move leaves nothing to clean up. When the file system does not support that rename, it falls back
    /// to <see cref="File.Move(string, string)"/> and never deletes the destination, because it cannot prove that this
    /// call created it. On Windows a same-volume <see cref="File.Move(string, string)"/> is already a plain rename.
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

        var error = TryRenameNoReplace(Path.GetFullPath(sourcePath), Path.GetFullPath(destinationPath));
        if (error == 0)
        {
            return;
        }

        if (error is null or EXDEV or EINVAL or LinuxENOSYS or MacENOTSUP or LinuxEOPNOTSUPP)
        {
            File.Move(sourcePath, destinationPath);
            return;
        }

        throw CreateException(error.Value, sourcePath, destinationPath);
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
