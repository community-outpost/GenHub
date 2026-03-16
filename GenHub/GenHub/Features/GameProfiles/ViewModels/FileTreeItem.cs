using System;
using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;

namespace GenHub.Features.GameProfiles.ViewModels;

/// <summary>
/// Represents an item in the file tree view.
/// </summary>
public partial class FileTreeItem : ObservableObject
{
    /// <summary>
    /// Gets or sets the name of the file or directory.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether this item is a file.
    /// </summary>
    public bool IsFile { get; set; }

    /// <summary>
    /// Gets or sets the full path of the file or directory.
    /// </summary>
    public string FullPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the children of this item (for directories).
    /// </summary>
    public ObservableCollection<FileTreeItem> Children { get; set; } = [];

    /// <summary>
    /// Gets a value indicating whether this file is an executable (.exe).
    /// </summary>
    public bool IsExecutable => IsFile && Path.GetExtension(Name).Equals(".exe", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets a value indicating whether this item is selected as the executable.
    /// </summary>
    [ObservableProperty]
    private bool _isSelectedExecutable;
}
