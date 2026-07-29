using System;
using System.Runtime.InteropServices;

namespace GenHub.Features.Workspace;

/// <summary>
/// The libc calls GenHub needs on Linux and macOS.
/// <para>
/// Deliberately tiny. Only functions whose signatures are identical across both
/// platforms appear here — <c>link</c>, <c>faccessat</c> and <c>geteuid</c> take and
/// return scalars and C strings, so a single declaration is correct on both.
/// </para>
/// <para>
/// <c>stat</c> and friends are deliberately absent. Their <c>struct stat</c> layouts
/// differ between Linux and macOS (and between architectures), so a shared P/Invoke
/// declaration would silently read the wrong offsets. Where volume identity or file
/// metadata is needed, use the BCL (<c>File.GetUnixFileMode</c>,
/// <c>FileInfo</c>) or attempt the operation and interpret <c>errno</c>.
/// </para>
/// </summary>
internal static partial class UnixNativeMethods
{
    /// <summary>Operation not permitted on a cross-device link.</summary>
    internal const int EXDEV = 18;

    /// <summary>The destination path already exists.</summary>
    internal const int EEXIST = 17;

    /// <summary>A component of the path does not exist.</summary>
    internal const int ENOENT = 2;

    /// <summary>Permission denied.</summary>
    internal const int EACCES = 13;

    /// <summary>The filesystem does not support hard links.</summary>
    internal const int EPERM = 1;

    /// <summary>
    /// Creates a hard link, POSIX <c>link(2)</c>.
    /// <para>
    /// .NET has no managed equivalent: <c>File.CreateSymbolicLink</c> exists but there
    /// is no hard-link API, which is why this interop is needed at all.
    /// </para>
    /// </summary>
    /// <param name="existingPath">Path to the existing file.</param>
    /// <param name="newPath">Path of the link to create.</param>
    /// <returns>0 on success, -1 on failure with <c>errno</c> set.</returns>
    [LibraryImport("libc", EntryPoint = "link", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int Link(string existingPath, string newPath);

    /// <summary>
    /// Checks whether the effective process identity may execute a file.
    /// </summary>
    /// <param name="path">The file to inspect.</param>
    /// <returns><c>true</c> when the current effective identity may execute the file.</returns>
    internal static bool CanExecute(string path)
    {
        const int executeMode = 1;
        var currentWorkingDirectory = OperatingSystem.IsMacOS() ? -2 : -100;
        var effectiveIdentity = OperatingSystem.IsMacOS() ? 0x10 : 0x200;

        return FileAccessAt(currentWorkingDirectory, path, executeMode, effectiveIdentity) == 0;
    }

    /// <summary>
    /// Returns the effective user ID, POSIX <c>geteuid(2)</c>.
    /// <para>
    /// Used instead of comparing <c>Environment.UserName</c> to the literal "root",
    /// which is wrong under <c>sudo -E</c> and for any uid-0 account named otherwise.
    /// </para>
    /// </summary>
    /// <returns>The effective user ID; 0 is root.</returns>
    [LibraryImport("libc", EntryPoint = "geteuid")]
    internal static partial uint GetEffectiveUserId();

    [LibraryImport("libc", EntryPoint = "faccessat", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int FileAccessAt(int directoryFileDescriptor, string path, int mode, int flags);
}
