using GenHub.Common.Editors;

namespace GenHub.Features.Tools.WndEditor.ViewModels;

/// <summary>
/// Tree node representing a directory or window definition file in the explorer.
/// </summary>
public sealed class WndFileTreeNodeViewModel : EditorFileTreeNodeViewModel
{
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
        EditorFileTreeNodeViewModel? parent = null)
        : base(name, fullPath, isDirectory, isCurrent, parent)
    {
    }
}
