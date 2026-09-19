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

    [LibraryImport("libc", EntryPoint = "removexattr", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int RemoveExtendedAttribute(string path, string name, int options);
}
