using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.ObjectModel;
using System.IO;

namespace GenHub.Features.Tools.ModBuilder.Models;

/// <summary>
/// Represents a file or directory node in the file tree.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("SonarCloud", "S2325:Methods and properties that don't access instance data should be static", Justification = "Bound in XAML data templates")]
public partial class FileTreeNode : ObservableObject
{
    /// <summary>
    /// Gets or sets the display name of the file or directory.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IconData))]
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
    [NotifyPropertyChangedFor(nameof(FormattedSize))]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    [NotifyPropertyChangedFor(nameof(IconData))]
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
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    [NotifyPropertyChangedFor(nameof(StatusColor))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private FileStatus _status = FileStatus.Unknown;

    /// <summary>
    /// Gets or sets the file size in bytes.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormattedSize))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private long _size;

    /// <summary>
    /// Gets or sets the game file size in bytes (for comparison).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
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
    /// Gets the icon geometry data string based on node type and extension.
    /// </summary>
    public string IconData
    {
        get
        {
            if (IsDirectory)
            {
                return "M10,4H4C2.89,4 2,4.89 2,6V18A2,2 0 0,0 4,20H20A2,2 0 0,0 22,18V8C22,6.89 21.1,6 20,6H12L10,4Z";
            }

            var ext = Path.GetExtension(Name).ToLowerInvariant();
            return ext switch
            {
                ".tga" or ".dds" or ".psd" or ".bmp" or ".png" or ".jpg" =>
                    "M8.5,13.5L11,16.5L14.5,12L19,18H5M21,19V5C21,3.89 20.1,3 19,3H5A2,2 0 0,0 3,5V19A2,2 0 0,0 5,21H19A2,2 0 0,0 21,19Z",
                ".big" or ".zip" or ".rar" or ".7z" =>
                    "M14,17H12V15H10V13H12V11H14V13H16V15H14M19,3H5C3.89,3 3,3.89 3,5V19A2,2 0 0,0 5,21H19A2,2 0 0,0 21,19V5C21,3.89 20.1,3 19,3Z",
                _ =>
                    "M14,2H6A2,2 0 0,0 4,4V20A2,2 0 0,0 6,22H18A2,2 0 0,0 20,20V8L14,2M18,20H6V4H13V9H18V20Z",
            };
        }
    }

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
    public string StatusText => Status switch
    {
        FileStatus.New => "New file (not in game)",
        FileStatus.Modified when GameSizeBytes > 0 =>
            $"Modified | Project: {FormatFileSize(Size)} | Game: {FormatFileSize(GameSizeBytes)}",
        FileStatus.Modified => "Modified (different from game)",
        FileStatus.Unchanged => "Unchanged (same as game)",
        FileStatus.Missing => "Missing from project",
        _ => string.Empty
    };

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
    /// <param name="path">The full path of the file or directory.</param>
    /// <param name="rootPath">The root directory path for relative path calculation.</param>
    /// <returns>A new <see cref="FileTreeNode"/> instance.</returns>
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
    /// File is unchanged compared to the game version.
    /// </summary>
    Unchanged,

    /// <summary>
    /// File has been modified compared to the game version.
    /// </summary>
    Modified,

    /// <summary>
    /// File is new and does not exist in the game.
    /// </summary>
    New,

    /// <summary>
    /// File is missing from the project but exists in the game.
    /// </summary>
    Missing
}
