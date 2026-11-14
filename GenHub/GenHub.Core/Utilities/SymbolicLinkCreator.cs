using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace GenHub.Core.Utilities;

/// <summary>
/// Creates symbolic links, including unprivileged creation on Windows when Developer Mode
/// is enabled.
/// <para>
/// <see cref="File.CreateSymbolicLink(string, string)"/> does not pass
/// <c>SYMBOLIC_LINK_FLAG_ALLOW_UNPRIVILEGED_CREATE</c>, so on Windows it still requires
/// <c>SeCreateSymbolicLinkPrivilege</c> even when Developer Mode is on. This helper passes
/// the flag and falls back to the privileged form on older Windows versions.
/// </para>
/// </summary>
public static class SymbolicLinkCreator
{
    private const uint SymbolicLinkFlagFile = 0x0;
    private const uint SymbolicLinkFlagDirectory = 0x1;
    private const uint SymbolicLinkFlagAllowUnprivilegedCreate = 0x2;
    private const int ErrorInvalidParameter = 87;
    private const int ErrorAccessDenied = 5;
    private const int ErrorPrivilegeNotHeld = 1314;

    /// <summary>
    /// Creates a symbolic link to a file.
    /// </summary>
    /// <param name="linkPath">The link to create.</param>
    /// <param name="targetPath">The file the link points to.</param>
    /// <exception cref="UnauthorizedAccessException">The process cannot create links.</exception>
    /// <exception cref="IOException">Link creation failed for another reason.</exception>
    public static void CreateFileLink(string linkPath, string targetPath) =>
        Create(linkPath, targetPath, isDirectory: false);

    /// <summary>
    /// Creates a symbolic link to a directory.
    /// </summary>
    /// <param name="linkPath">The link to create.</param>
    /// <param name="targetPath">The directory the link points to.</param>
    /// <exception cref="UnauthorizedAccessException">The process cannot create links.</exception>
    /// <exception cref="IOException">Link creation failed for another reason.</exception>
    public static void CreateDirectoryLink(string linkPath, string targetPath) =>
        Create(linkPath, targetPath, isDirectory: true);

    /// <summary>
    /// Probes whether this process can create a file symbolic link.
    /// </summary>
    /// <returns><c>true</c> when a temporary link can be created and removed.</returns>
    public static bool CanCreateFileLink()
    {
        if (!OperatingSystem.IsWindows())
        {
            return true;
        }

        var probeRoot = Path.Combine(Path.GetTempPath(), "genhub-symlink-probe-" + Guid.NewGuid().ToString("N"));
        var targetPath = probeRoot + ".target";
        var linkPath = probeRoot + ".link";

        try
        {
            File.WriteAllText(targetPath, string.Empty);
            CreateFileLink(linkPath, targetPath);
            return new FileInfo(linkPath).LinkTarget is not null;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        finally
        {
            TryDelete(linkPath);
            TryDelete(targetPath);
        }
    }

    private static void Create(string linkPath, string targetPath, bool isDirectory)
    {
        if (!OperatingSystem.IsWindows())
        {
            if (isDirectory)
            {
                Directory.CreateSymbolicLink(linkPath, targetPath);
            }
            else
            {
                File.CreateSymbolicLink(linkPath, targetPath);
            }

            return;
        }

        if (TryCreateWindows(linkPath, targetPath, isDirectory, allowUnprivileged: true, out var error))
        {
            return;
        }

        // Windows before 10 1703 does not know the unprivileged flag and reports
        // ERROR_INVALID_PARAMETER. Retry with the classic privileged form.
        if (error == ErrorInvalidParameter &&
            TryCreateWindows(linkPath, targetPath, isDirectory, allowUnprivileged: false, out error))
        {
            return;
        }

        if (error is ErrorAccessDenied or ErrorPrivilegeNotHeld)
        {
            throw new UnauthorizedAccessException(
                "The process cannot create symbolic links. Enable Developer Mode or run as an administrator.",
                new Win32Exception(error));
        }

        throw new IOException(
            $"Symbolic link creation failed with Win32 error {error}.",
            new Win32Exception(error));
    }

    [SupportedOSPlatform("windows")]
    private static bool TryCreateWindows(
        string linkPath,
        string targetPath,
        bool isDirectory,
        bool allowUnprivileged,
        out int error)
    {
        var flags = isDirectory ? SymbolicLinkFlagDirectory : SymbolicLinkFlagFile;
        if (allowUnprivileged)
        {
            flags |= SymbolicLinkFlagAllowUnprivilegedCreate;
        }

        if (CreateSymbolicLinkW(linkPath, targetPath, flags))
        {
            error = 0;
            return true;
        }

        error = Marshal.GetLastWin32Error();
        return false;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Best-effort cleanup of a uniquely named probe.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort cleanup of a uniquely named probe.
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateSymbolicLinkW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    [SupportedOSPlatform("windows")]
    private static extern bool CreateSymbolicLinkW(string lpSymlinkFileName, string lpTargetFileName, uint dwFlags);
}
