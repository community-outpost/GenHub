namespace GenHub.Core.Helpers;

/// <summary>
/// Clears read-only state so GenHub can modify content it manages.
/// </summary>
public static class WriteAccessHelper
{
    /// <summary>
    /// Makes a directory and every file and subdirectory inside it writable by the current user.
    /// On Unix the owner write bit is added. On Windows the read-only attribute is cleared.
    /// Symbolic links are skipped so nothing outside the directory is touched.
    /// </summary>
    /// <param name="directoryPath">The directory to make writable.</param>
    /// <exception cref="UnauthorizedAccessException">The current user may not change the permissions.</exception>
    /// <exception cref="IOException">The permissions could not be changed.</exception>
    public static void EnsureDirectoryWritable(string directoryPath)
    {
        var root = new DirectoryInfo(directoryPath);
        if (!root.Exists || root.LinkTarget != null)
        {
            return;
        }

        EnsureEntryWritable(root);

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
            IgnoreInaccessible = false,
        };

        foreach (var entry in root.EnumerateFileSystemInfos("*", options))
        {
            EnsureEntryWritable(entry);
        }
    }

    /// <summary>
    /// Makes a single file writable by the current user. Symbolic links are skipped.
    /// </summary>
    /// <param name="filePath">The file to make writable.</param>
    /// <exception cref="UnauthorizedAccessException">The current user may not change the permissions.</exception>
    /// <exception cref="IOException">The permissions could not be changed.</exception>
    public static void EnsureFileWritable(string filePath)
    {
        var file = new FileInfo(filePath);
        if (file.Exists && file.LinkTarget == null)
        {
            EnsureEntryWritable(file);
        }
    }

    private static void EnsureEntryWritable(FileSystemInfo entry)
    {
        if (OperatingSystem.IsWindows())
        {
            if ((entry.Attributes & FileAttributes.ReadOnly) != 0)
            {
                entry.Attributes &= ~FileAttributes.ReadOnly;
            }

            return;
        }

        var mode = entry.UnixFileMode;
        var required = UnixFileMode.UserWrite;
        if (entry is DirectoryInfo)
        {
            required |= UnixFileMode.UserExecute;
        }

        if ((mode & required) != required)
        {
            entry.UnixFileMode = mode | required;
        }
    }
}
