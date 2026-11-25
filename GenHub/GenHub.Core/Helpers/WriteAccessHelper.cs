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
    /// When an entry cannot be changed, the entries already changed are put back before the exception is thrown.
    /// </summary>
    /// <param name="directoryPath">The directory to make writable.</param>
    /// <returns>The entries that were changed, for <see cref="RestoreWriteAccess"/>.</returns>
    /// <exception cref="UnauthorizedAccessException">The current user may not change the permissions.</exception>
    /// <exception cref="IOException">The permissions could not be changed.</exception>
    public static IReadOnlyList<WriteAccessChange> EnsureDirectoryWritable(string directoryPath)
    {
        var changes = new List<WriteAccessChange>();
        var root = new DirectoryInfo(directoryPath);
        if (!root.Exists || root.LinkTarget != null)
        {
            return changes;
        }

        try
        {
            EnsureEntryWritable(root, changes);

            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                AttributesToSkip = FileAttributes.ReparsePoint,
                IgnoreInaccessible = false,
            };

            foreach (var entry in root.EnumerateFileSystemInfos("*", options))
            {
                EnsureEntryWritable(entry, changes);
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            RestoreWriteAccess(changes);
            throw;
        }

        return changes;
    }

    /// <summary>
    /// Makes a single file writable by the current user. Symbolic links are skipped.
    /// </summary>
    /// <param name="filePath">The file to make writable.</param>
    /// <returns>The entries that were changed, for <see cref="RestoreWriteAccess"/>.</returns>
    /// <exception cref="UnauthorizedAccessException">The current user may not change the permissions.</exception>
    /// <exception cref="IOException">The permissions could not be changed.</exception>
    public static IReadOnlyList<WriteAccessChange> EnsureFileWritable(string filePath)
    {
        var changes = new List<WriteAccessChange>();
        var file = new FileInfo(filePath);
        if (file.Exists && file.LinkTarget == null)
        {
            EnsureEntryWritable(file, changes);
        }

        return changes;
    }

    /// <summary>
    /// Puts back the permissions recorded by <see cref="EnsureDirectoryWritable"/> or <see cref="EnsureFileWritable"/>.
    /// Entries that no longer exist or cannot be changed are skipped.
    /// </summary>
    /// <param name="changes">The recorded changes.</param>
    public static void RestoreWriteAccess(IReadOnlyList<WriteAccessChange> changes)
    {
        // Children first, so a directory loses write or search access only after everything inside it is done.
        for (var i = changes.Count - 1; i >= 0; i--)
        {
            var change = changes[i];
            FileSystemInfo entry = change.IsDirectory ? new DirectoryInfo(change.Path) : new FileInfo(change.Path);
            try
            {
                if (!entry.Exists || entry.LinkTarget != null)
                {
                    continue;
                }

                if (OperatingSystem.IsWindows())
                {
                    entry.Attributes = change.OriginalAttributes;
                }
                else
                {
                    entry.UnixFileMode = change.OriginalMode;
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                // Best effort: the entry keeps the write access it was given.
            }
        }
    }

    private static void EnsureEntryWritable(FileSystemInfo entry, List<WriteAccessChange> changes)
    {
        var isDirectory = entry is DirectoryInfo;
        if (OperatingSystem.IsWindows())
        {
            var attributes = entry.Attributes;
            if ((attributes & FileAttributes.ReadOnly) != 0)
            {
                entry.Attributes = attributes & ~FileAttributes.ReadOnly;
                changes.Add(new WriteAccessChange(entry.FullName, isDirectory, attributes, UnixFileMode.None));
            }

            return;
        }

        var mode = entry.UnixFileMode;
        var required = UnixFileMode.UserWrite;
        if (isDirectory)
        {
            required |= UnixFileMode.UserRead | UnixFileMode.UserExecute;
        }

        if ((mode & required) != required)
        {
            entry.UnixFileMode = mode | required;
            changes.Add(new WriteAccessChange(entry.FullName, isDirectory, default, mode));
        }
    }
}
