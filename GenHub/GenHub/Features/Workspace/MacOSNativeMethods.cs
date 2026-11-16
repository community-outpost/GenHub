using System;
using System.Runtime.InteropServices;

namespace GenHub.Features.Workspace;

/// <summary>
/// The libc calls GenHub needs on macOS only.
/// <para>
/// Separate from <see cref="UnixNativeMethods"/> by design. That type is restricted to
/// functions whose signatures are identical on Linux and macOS; <c>removexattr</c> is
/// not one of them, because macOS takes a trailing <c>options</c> argument that Linux
/// does not. Declaring it there would be wrong on Linux in a way the compiler cannot
/// catch.
/// </para>
/// </summary>
internal static partial class MacOSNativeMethods
{
    /// <summary>
    /// The extended attribute macOS sets on files that arrived from an untrusted source.
    /// Gatekeeper refuses to execute anything carrying it until the user approves.
    /// </summary>
    private const string QuarantineAttribute = "com.apple.quarantine";

    /// <summary>Act on the symlink itself rather than its target.</summary>
    private const int XattrNoFollow = 0x0001;

    /// <summary>The attribute was not present, POSIX <c>ENOATTR</c> on macOS.</summary>
    private const int ENOATTR = 93;

    /// <summary>
    /// Removes the quarantine attribute from a file, if it carries one.
    /// </summary>
    /// <param name="path">The absolute path of the file to clear.</param>
    /// <returns>
    /// <c>true</c> when the file is known not to be quarantined afterwards — either the
    /// attribute was removed or it was never there. <c>false</c> when the attribute could
    /// not be removed, which leaves the file executable-but-blocked.
    /// </returns>
    /// <remarks>
    /// Returns <c>true</c> unchanged on every non-macOS platform: no other system has this
    /// attribute, so there is nothing to clear and nothing to report.
    /// </remarks>
    internal static bool TryClearQuarantine(string path)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return true;
        }

        // Follow symlinks is wrong here: the workspace entry is what has to be runnable,
        // and clearing a link target would touch a file this workspace may not own.
        if (RemoveExtendedAttribute(path, QuarantineAttribute, XattrNoFollow) == 0)
        {
            return true;
        }

        // Nothing to remove is the common case and not a failure — most files are never
        // quarantined, and a build run from a developer machine never is.
        return Marshal.GetLastPInvokeError() == ENOATTR;
    }

    /// <summary>
    /// Determines whether a file, or the file a symlink resolves to, carries the quarantine
    /// attribute. The resolved file is the one Gatekeeper checks when it is run or loaded.
    /// </summary>
    /// <param name="path">The absolute path of the file to check.</param>
    /// <returns>
    /// <c>true</c> when the file is quarantined, or when the attribute could not be read for
    /// any reason other than its absence. A private copy is the safe answer to an unknown.
    /// Always <c>false</c> off macOS.
    /// </returns>
    internal static bool IsQuarantined(string path)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return false;
        }

        return GetExtendedAttribute(path, QuarantineAttribute, IntPtr.Zero, 0, 0, 0) >= 0
            || Marshal.GetLastPInvokeError() != ENOATTR;
    }

    /// <summary>
    /// Renames a file without replacing an existing destination, <c>renamex_np(2)</c> with
    /// <c>RENAME_EXCL</c>. The rename is atomic and never falls back to copying.
    /// </summary>
    /// <param name="sourcePath">The file to rename.</param>
    /// <param name="destinationPath">The new path.</param>
    /// <returns>0 on success, otherwise the <c>errno</c> value.</returns>
    internal static int RenameNoReplace(string sourcePath, string destinationPath)
    {
        const uint renameExclusive = 0x4;
        return RenameExtended(sourcePath, destinationPath, renameExclusive) == 0 ? 0 : Marshal.GetLastPInvokeError();
    }

    [LibraryImport("libc", EntryPoint = "renamex_np", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int RenameExtended(string sourcePath, string destinationPath, uint flags);

    [LibraryImport("libc", EntryPoint = "getxattr", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint GetExtendedAttribute(string path, string name, IntPtr value, nuint size, uint position, int options);

    [LibraryImport("libc", EntryPoint = "removexattr", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int RemoveExtendedAttribute(string path, string name, int options);
}
