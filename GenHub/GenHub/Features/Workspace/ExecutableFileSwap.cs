using System;
using System.IO;

namespace GenHub.Features.Workspace;

/// <summary>
/// Replaces a workspace file with a private copy that carries the Unix execute bit.
/// <para>
/// The private copy is what keeps the content store safe: a hard-linked workspace file
/// shares its inode — and therefore its file mode — with the stored blob, so setting the
/// execute bit in place would change that blob for every profile referencing the hash.
/// Copying first gives the workspace its own inode, and because the copy is made
/// executable before it atomically replaces the destination, the file is never
/// observable as missing or present-but-not-executable.
/// </para>
/// <para>
/// Meant for Unix; callers gate on the operating system. Windows has no execute bit,
/// so the mode change is skipped there defensively.
/// </para>
/// </summary>
internal static class ExecutableFileSwap
{
    /// <summary>
    /// The recognisable marker in the name of the temporary file used while the swap is
    /// in flight. A fresh GUID follows it, so concurrent swaps of the same file can
    /// never collide with each other or with a stale leftover.
    /// </summary>
    internal const string TemporaryMarker = ".genhub-exec-tmp-";

    private const UnixFileMode ExecutableMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
        UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
        UnixFileMode.OtherRead | UnixFileMode.OtherExecute;

    /// <summary>
    /// Swaps the file at <paramref name="targetPath"/> for an executable private copy.
    /// </summary>
    /// <param name="targetPath">The absolute path of the workspace file.</param>
    /// <returns>
    /// <c>true</c> when the resulting file is known not to be quarantined. <c>false</c>
    /// means the file is executable but macOS may still refuse to run it, which callers
    /// should report — it is the difference between a working profile and a game that
    /// will not start for a reason nothing else explains.
    /// </returns>
    internal static bool MakeExecutable(string targetPath)
    {
        var temporaryPath = targetPath + TemporaryMarker + Guid.NewGuid().ToString("N");

        try
        {
            // The GUID makes the name unique by construction; copying without overwrite
            // turns the impossible collision into an exception instead of a clobber.
            File.Copy(targetPath, temporaryPath);

            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(temporaryPath, ExecutableMode);
            }

            // On macOS the execute bit alone is not enough. A GenHub that was itself
            // downloaded carries com.apple.quarantine, and macOS propagates that to the
            // files it writes — so the engine binary lands quarantined and Gatekeeper
            // refuses to run it. The user sees GenHub start normally and the game fail,
            // which is a hard failure to attribute. Clearing it here, on the private copy
            // GenHub just created, keeps that invisible to them.
            //
            // Cleared before the swap for the same reason the mode is: the destination is
            // never observable in a half-prepared state.
            var quarantineCleared = MacOSNativeMethods.TryClearQuarantine(temporaryPath);

            File.Move(temporaryPath, targetPath, overwrite: true);

            // Not an exception: the file is materialized and correct, and failing the
            // whole swap would be worse than a file that needs one manual command. The
            // caller logs it so the cause is recoverable from the logs when a launch is
            // later refused.
            return quarantineCleared;
        }
        catch
        {
            // The move is the last step, so a failure means the temporary copy may still
            // exist. Remove it: the original entry is untouched and correct, and a stray
            // temporary file would otherwise be reported by workspace validation.
            try
            {
                File.Delete(temporaryPath);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            throw;
        }
    }
}
