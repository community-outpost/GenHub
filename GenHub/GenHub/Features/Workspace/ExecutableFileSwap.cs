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
    /// The suffix of the temporary file used while the swap is in flight.
    /// </summary>
    internal const string TemporarySuffix = ".genhub-exec-tmp";

    private const UnixFileMode ExecutableMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
        UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
        UnixFileMode.OtherRead | UnixFileMode.OtherExecute;

    /// <summary>
    /// Swaps the file at <paramref name="targetPath"/> for an executable private copy.
    /// </summary>
    /// <param name="targetPath">The absolute path of the workspace file.</param>
    internal static void MakeExecutable(string targetPath)
    {
        var temporaryPath = targetPath + TemporarySuffix;

        try
        {
            File.Copy(targetPath, temporaryPath, overwrite: true);

            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(temporaryPath, ExecutableMode);
            }

            File.Move(temporaryPath, targetPath, overwrite: true);
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
