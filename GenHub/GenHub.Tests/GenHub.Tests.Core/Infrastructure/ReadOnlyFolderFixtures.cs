using System.Diagnostics;

namespace GenHub.Tests.Core.Infrastructure;

/// <summary>
/// Makes temporary test folders read-only the way an outside tool leaves map folders, and undoes it for cleanup.
/// </summary>
internal static class ReadOnlyFolderFixtures
{
    private const UnixFileMode AllWrite = UnixFileMode.UserWrite | UnixFileMode.GroupWrite | UnixFileMode.OtherWrite;

    /// <summary>
    /// Removes write access from a folder and the files directly inside it.
    /// </summary>
    /// <param name="directoryPath">The folder to make read-only.</param>
    public static void MakeReadOnly(string directoryPath)
    {
        foreach (var file in Directory.GetFiles(directoryPath))
        {
            MakeEntryReadOnly(new FileInfo(file));
        }

        MakeEntryReadOnly(new DirectoryInfo(directoryPath));
    }

    /// <summary>
    /// Reports whether the current user lacks write access to a file or folder.
    /// </summary>
    /// <param name="path">The file or folder to inspect.</param>
    /// <returns><c>true</c> when the entry is read-only.</returns>
    public static bool IsReadOnly(string path)
    {
        FileSystemInfo entry = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
        return OperatingSystem.IsWindows()
            ? (entry.Attributes & FileAttributes.ReadOnly) != 0
            : (entry.UnixFileMode & UnixFileMode.UserWrite) == 0;
    }

    /// <summary>
    /// Sets the macOS user immutable flag, which makes permission changes fail for the owner.
    /// </summary>
    /// <param name="path">The file or folder to lock.</param>
    public static void LockImmutable(string path) => RunChflags("uchg", path);

    /// <summary>
    /// Restores write access below a test root so it can be deleted. Clears the macOS immutable flag first.
    /// </summary>
    /// <param name="rootPath">The temporary test root.</param>
    public static void RestoreWritable(string rootPath)
    {
        if (!Directory.Exists(rootPath))
        {
            return;
        }

        if (OperatingSystem.IsMacOS())
        {
            RunChflags("-R", "nouchg", rootPath);
        }

        var entries = new List<FileSystemInfo> { new DirectoryInfo(rootPath) };
        entries.AddRange(new DirectoryInfo(rootPath).EnumerateFileSystemInfos("*", new EnumerationOptions
        {
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
            IgnoreInaccessible = true,
        }));

        foreach (var entry in entries)
        {
            if (OperatingSystem.IsWindows())
            {
                entry.Attributes &= ~FileAttributes.ReadOnly;
            }
            else
            {
                entry.UnixFileMode |= UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
            }
        }
    }

    private static void MakeEntryReadOnly(FileSystemInfo entry)
    {
        if (OperatingSystem.IsWindows())
        {
            entry.Attributes |= FileAttributes.ReadOnly;
        }
        else
        {
            entry.UnixFileMode &= ~AllWrite;
        }
    }

    private static void RunChflags(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("/usr/bin/chflags") { UseShellExecute = false };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("chflags did not start.");
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"chflags {string.Join(' ', arguments)} failed.");
        }
    }
}
