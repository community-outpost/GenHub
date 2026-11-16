using CommunityToolkit.Mvvm.ComponentModel;
using GenHub.Core.Models.Tools.IniEditor;
using System.Collections.ObjectModel;

namespace GenHub.Features.Tools.IniEditor.ViewModels;

/// <summary>
/// Tree node wrapping an INI block for the block explorer.
/// </summary>
public sealed partial class IniTreeNodeViewModel : ObservableObject
{
    /// <summary>
    /// Initializes a new instance of the <see cref="IniTreeNodeViewModel"/> class.
    /// </summary>
    /// <param name="block">The wrapped block.</param>
    /// <param name="parent">The parent node, when nested.</param>
    public IniTreeNodeViewModel(IniBlock block, IniTreeNodeViewModel? parent)
    {
        Block = block;
        Parent = parent;
    }

    /// <summary>
    /// Gets the wrapped block.
    /// </summary>
    public IniBlock Block { get; }

    /// <summary>
    /// Gets the parent node, when nested.
    /// </summary>
    public IniTreeNodeViewModel? Parent { get; }

    /// <summary>
    /// Gets child nodes for nested module sub-blocks.
    /// </summary>
    public ObservableCollection<IniTreeNodeViewModel> Children { get; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether the node is expanded.
    /// </summary>
    [ObservableProperty]
    private bool _isExpanded;

    /// <summary>
    /// Gets the display name for the node.
    /// </summary>
    public string DisplayName => Block.DisplayHeader;

    /// <summary>
    /// Gets the block type badge text.
    /// </summary>
    public string Badge => Block.BlockType;
}
