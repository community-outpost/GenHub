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
    /// Gets or sets the parent node.
    /// </summary>
    [ObservableProperty]
    private FileTreeNode? _parent;

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
    /// Gets or sets the relative path within the project or game directory.
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

            return Extension.ToLowerInvariant() switch
            {
                ".ini" or ".txt" or ".str" => "M14,2H6A2,2 0 0,0 4,4V20A2,2 0 0,0 6,22H18A2,2 0 0,0 20,20V8L14,2M18,20H6V4H13V9H18V20M8,12V14H16V12H8M8,16V18H13V16H8Z",
                ".tga" or ".dds" or ".bmp" or ".png" or ".jpg" => "M8.5,13.5L11,16.5L14.5,12L19,18H5M21,19V5C21,3.89 20.1,3 19,3H5A2,2 0 0,0 3,5V19A2,2 0 0,0 5,21H19A2,2 0 0,0 21,19Z",
                ".w3d" => "M12,2L1,21H23L12,2M12,6L19.53,19H4.47L12,6Z",
                ".big" => "M19,3H5C3.89,3 3,3.89 3,5V19A2,2 0 0,0 5,21H19A2,2 0 0,0 21,19V5C21,3.89 20.1,3 19,3M19,19H5V5H19V19Z",
                ".wav" or ".mp3" => "M12,3V13.55C11.41,13.21 10.73,13 10,13A4,4 0 0,0 6,17A4,4 0 0,0 10,21A4,4 0 0,0 14,17V7H18V3H12Z",
                _ => "M13,9V3.5L18.5,9M6,2C4.89,2 4,2.89 4,4V20A2,2 0 0,0 6,22H18A2,2 0 0,0 20,20V8L14,2H6Z",
            };
        }
    }

    /// <summary>
    /// Gets a value indicating whether this node has a valid status to display.
    /// </summary>
    public bool HasStatus => !IsDirectory && Status != FileStatus.Unknown;

    /// <summary>
    /// Gets the text description of the status.
    /// </summary>
    public string StatusText => Status switch
    {
        FileStatus.Unchanged => "Same as game",
        FileStatus.Modified => $"Modified ({FormatDiff(Size - GameSizeBytes)})",
        FileStatus.New => "New file",
        _ => string.Empty,
    };

    /// <summary>
    /// Gets the color resource key for the status badge.
    /// </summary>
    public string StatusColor => Status switch
    {
        FileStatus.Unchanged => "TextSecondary",
        FileStatus.Modified => "AccentBrush",
        FileStatus.New => "SuccessBrush",
        _ => "Transparent",
    };

    /// <summary>
    /// Formats a byte size into a human-readable string.
    /// </summary>
    /// <param name="bytes">Size in bytes.</param>
    /// <returns>Formatted size string.</returns>
    public static string FormatFileSize(long bytes)
    {
        return bytes switch
        {
            < 1024 => $"{bytes} B",
            < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
            < 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024.0):F1} MB",
            _ => $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB",
        };
    }

    /// <summary>
    /// Creates a FileTreeNode from a file system path.
    /// </summary>
    /// <param name="path">The full path.</param>
    /// <param name="rootPath">The root directory path for relative path calculation.</param>
    /// <returns>A new <see cref="FileTreeNode"/> instance.</returns>
    public static FileTreeNode FromPath(string path, string rootPath)
    {
        var isDir = Directory.Exists(path);
        var relPath = Path.GetRelativePath(rootPath, path).Replace('\\', '/');

        return new FileTreeNode
        {
            Name = Path.GetFileName(path),
            FullPath = path,
            RelativePath = relPath,
            IsDirectory = isDir,
            Extension = isDir ? string.Empty : Path.GetExtension(path),
            Size = isDir ? 0 : new FileInfo(path).Length,
        };
    }

    private static string FormatDiff(long bytesDiff)
    {
        var sign = bytesDiff >= 0 ? "+" : "-";
        return $"{sign}{FormatFileSize(Math.Abs(bytesDiff))}";
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
    Missing,
}
