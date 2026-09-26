using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.ObjectModel;

namespace GenHub.Features.Tools.WndEditor.ViewModels;

/// <summary>
/// Tree node representing a directory or window definition file in the explorer.
/// </summary>
public sealed partial class WndFileTreeNodeViewModel : ObservableObject
{
    private readonly ObservableCollection<WndFileTreeNodeViewModel> _children = [];

    [ObservableProperty]
    private bool _isExpanded = true;

    [ObservableProperty]
    private bool _isCurrent;

    /// <summary>
    /// Initializes a new instance of the <see cref="WndFileTreeNodeViewModel"/> class.
    /// </summary>
    /// <param name="name">The display name of the directory or file.</param>
    /// <param name="fullPath">The full path on disk.</param>
    /// <param name="isDirectory">Whether this node is a directory.</param>
    /// <param name="isCurrent">Whether this file is currently open in the editor.</param>
    /// <param name="parent">The parent directory node, or null for root.</param>
    public WndFileTreeNodeViewModel(
        string name,
        string fullPath,
        bool isDirectory,
        bool isCurrent = false,
        WndFileTreeNodeViewModel? parent = null)
    {
        Name = name;
        FullPath = fullPath;
        IsDirectory = isDirectory;
        _isCurrent = isCurrent;
        Parent = parent;
        Children = new(_children);
    }

    /// <summary>
    /// Gets the file name with extension if this is a file, or the directory name.
    /// </summary>
    public string FileName => System.IO.Path.GetFileName(FullPath);

    /// <summary>
    /// Gets the display name of the directory or file (without extension for files).
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the full path to the directory or file.
    /// </summary>
    public string FullPath { get; }

    /// <summary>
    /// Gets a value indicating whether this node represents a directory.
    /// </summary>
    public bool IsDirectory { get; }

    /// <summary>
    /// Gets a value indicating whether this node represents a file.
    /// </summary>
    public bool IsFile => !IsDirectory;

    /// <summary>
    /// Gets the parent node, or null for root nodes.
    /// </summary>
    public WndFileTreeNodeViewModel? Parent { get; }

    /// <summary>
    /// Gets the child nodes.
    /// </summary>
    public ReadOnlyObservableCollection<WndFileTreeNodeViewModel> Children { get; }

    /// <summary>
    /// Adds a child node to this directory node.
    /// </summary>
    /// <param name="child">The child node to add.</param>
    public void AddChild(WndFileTreeNodeViewModel child)
    {
        ArgumentNullException.ThrowIfNull(child);
        _children.Add(child);
    }
}
