using System;
using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using GenHub.Core.Utilities;

namespace GenHub.Features.GameProfiles.ViewModels;

/// <summary>
/// Represents an item in the file tree view.
/// </summary>
public partial class FileTreeItem : ObservableObject
{
    /// <summary>
    /// Gets or sets the name of the file or directory.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsExecutable))]
    private string _name = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether this item is a file.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsExecutable))]
    private bool _isFile;

    /// <summary>
    /// Gets or sets the full path of the file or directory.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsExecutable))]
    private string _fullPath = string.Empty;

    /// <summary>
    /// Gets or sets the children of this item (for directories).
    /// </summary>
    public ObservableCollection<FileTreeItem> Children { get; set; } = [];

    /// <summary>
    /// Gets a value indicating whether this file is an executable (.exe).
    /// </summary>
    public bool IsExecutable => IsFile && ExecutableFileClassifier.IsLegacyLaunchCandidate(
        Name, string.IsNullOrEmpty(FullPath) ? null : FullPath);

    /// <summary>
    /// Gets or sets a value indicating whether this item is selected as the executable.
    /// </summary>
    [ObservableProperty]
    private bool _isSelectedExecutable;
}
