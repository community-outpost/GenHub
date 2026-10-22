using System;
using System.Runtime.InteropServices;

namespace GenHub.Features.Workspace;

/// <summary>
/// The libc calls GenHub needs on Linux only.
/// <para>
/// Separate from <see cref="UnixNativeMethods"/> because <c>renameat2</c> does not exist on macOS.
/// </para>
/// </summary>
internal static partial class LinuxNativeMethods
{
    /// <summary>
    /// Renames a file without replacing an existing destination, <c>renameat2(2)</c> with
    /// <c>RENAME_NOREPLACE</c>. The rename is atomic and never falls back to copying.
    /// </summary>
    /// <param name="sourcePath">The file to rename.</param>
    /// <param name="destinationPath">The new path.</param>
    /// <returns>0 on success, otherwise the <c>errno</c> value.</returns>
    /// <exception cref="EntryPointNotFoundException">The C library predates <c>renameat2</c>.</exception>
    internal static int RenameNoReplace(string sourcePath, string destinationPath)
    {
        const int currentWorkingDirectory = -100;
        const uint renameNoReplace = 0x1;
        return RenameAt2(currentWorkingDirectory, sourcePath, currentWorkingDirectory, destinationPath, renameNoReplace) == 0
            ? 0
            : Marshal.GetLastPInvokeError();
    }

    [LibraryImport("libc", EntryPoint = "renameat2", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int RenameAt2(int sourceDirectoryFileDescriptor, string sourcePath, int destinationDirectoryFileDescriptor, string destinationPath, uint flags);
}
