using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.ObjectModel;
using System.IO;

namespace GenHub.Features.Tools.ModBuilder.Models;

/// <summary>
/// Represents a file or directory node in the file tree.
/// </summary>
public partial class FileTreeNode : ObservableObject
{
    /// <summary>
    /// Gets or sets the display name of the file or directory.
    /// </summary>
    [ObservableProperty]
    private string _name = string.Empty;

    /// <summary>
    /// Gets or sets the full path to the file or directory.
    /// </summary>
    [ObservableProperty]
    private string _fullPath = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether this node represents a directory.
    /// </summary>
    [ObservableProperty]
    private bool _isDirectory;

    /// <summary>
    /// Gets or sets a value indicating whether this node is expanded.
    /// </summary>
    [ObservableProperty]
    private bool _isExpanded;

    /// <summary>
    /// Gets or sets a value indicating whether this node is selected.
    /// </summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>
    /// Gets or sets the file status (New, Modified, Unchanged, etc.).
    /// </summary>
    [ObservableProperty]
    private FileStatus _status = FileStatus.Unknown;

    /// <summary>
    /// Gets or sets the file size in bytes.
    /// </summary>
    [ObservableProperty]
    private long _size;

    /// <summary>
    /// Gets or sets the game file size in bytes (for comparison).
    /// </summary>
    [ObservableProperty]
    private long _gameSizeBytes;

    /// <summary>
    /// Gets or sets the last modified date.
    /// </summary>
    [ObservableProperty]
    private DateTime _modifiedDate;

    /// <summary>
    /// Gets or sets the relative path from the root directory.
    /// </summary>
    [ObservableProperty]
    private string _relativePath = string.Empty;

    /// <summary>
    /// Gets or sets the file extension.
    /// </summary>
    [ObservableProperty]
    private string _extension = string.Empty;

    /// <summary>
    /// Gets the collection of child nodes.
    /// </summary>
    public ObservableCollection<FileTreeNode> Children { get; } = [];

    /// <summary>
    /// Gets a value indicating whether this node has children.
    /// </summary>
    public bool HasChildren => Children.Count > 0;

    /// <summary>
    /// Gets the formatted file size string.
    /// </summary>
    public string FormattedSize => IsDirectory ? string.Empty : FormatFileSize(Size);

    /// <summary>
    /// Gets the status color based on file status.
    /// </summary>
    public string StatusColor => Status switch
    {
        FileStatus.New => "#4CAF50",      // Green - new file
        FileStatus.Modified => "#F44336", // Red - modified file
        FileStatus.Unchanged => "#9E9E9E", // Gray - unchanged
        FileStatus.Missing => "#FF9800",   // Orange - missing
        _ => "Transparent"
    };

    /// <summary>
    /// Gets the status text description with size comparison.
    /// </summary>
    public string StatusText
    {
        get
        {
            return Status switch
            {
                FileStatus.New => "New file (not in game)",
                FileStatus.Modified when GameSizeBytes > 0 =>
                    $"Modified | Project: {FormatFileSize(Size)} | Game: {FormatFileSize(GameSizeBytes)}",
                FileStatus.Modified => "Modified (different from game)",
                FileStatus.Unchanged => "Unchanged (same as game)",
                FileStatus.Missing => "Missing from project",
                _ => string.Empty
            };
        }
    }

    /// <summary>
    /// Gets a value indicating whether this node has a visible status indicator.
    /// </summary>
    public bool HasStatus => Status != FileStatus.Unknown && !IsDirectory;

    /// <summary>
    /// Formats a file size in bytes to a human-readable string.
    /// </summary>
    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024)
            return $"{bytes} B";
        if (bytes < 1024 * 1024)
            return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024)
            return $"{bytes / (1024.0 * 1024.0):F1} MB";
        return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
    }

    /// <summary>
    /// Creates a FileTreeNode from a file system path.
    /// </summary>
    public static FileTreeNode FromPath(string path, string rootPath)
    {
        var isDirectory = Directory.Exists(path);
        var info = isDirectory ? (FileSystemInfo)new DirectoryInfo(path) : new FileInfo(path);

        return new FileTreeNode
        {
            Name = info.Name,
            FullPath = path,
            IsDirectory = isDirectory,
            Size = isDirectory ? 0 : ((FileInfo)info).Length,
            ModifiedDate = info.LastWriteTime,
            RelativePath = Path.GetRelativePath(rootPath, path),
            Extension = isDirectory ? string.Empty : Path.GetExtension(path).TrimStart('.')
        };
    }
}

/// <summary>
/// Represents the status of a file in the project.
/// </summary>
public enum FileStatus
{
    /// <summary>
    /// Status is unknown or not yet determined.
    /// </summary>
    Unknown,

    /// <summary>
    /// File is new and doesn't exist in the game installation.
    /// </summary>
    New,

    /// <summary>
    /// File has been modified compared to the game installation.
    /// </summary>
    Modified,

    /// <summary>
    /// File is unchanged from the game installation.
    /// </summary>
    Unchanged,

    /// <summary>
    /// File is missing from the project but exists in game.
    /// </summary>
    Missing
}
