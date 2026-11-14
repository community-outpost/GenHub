using CommunityToolkit.Mvvm.ComponentModel;
using GenHub.Core.Helpers;
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
    /// Gets or sets a value indicating whether this node is a synthetic type-group header.
    /// Group headers organize large files and carry no editable block.
    /// </summary>
    [ObservableProperty]
    private bool _isGroupHeader;

    /// <summary>
    /// Gets or sets the number of blocks inside a group header.
    /// </summary>
    [ObservableProperty]
    private int _groupCount;

    /// <summary>
    /// Gets the display name for the node.
    /// </summary>
    public string DisplayName => IsGroupHeader
        ? $"{Block.BlockType} ({GroupCount})"
        : Block.DisplayHeader;

    /// <summary>
    /// Gets the compact row label without the redundant block-type prefix,
    /// since the badge already shows the type.
    /// </summary>
    public string ShortName
    {
        get
        {
            if (IsGroupHeader)
            {
                return Block.BlockType;
            }

            if (!string.IsNullOrEmpty(Block.Name))
            {
                return Block.Name;
            }

            return Block.DisplayHeader;
        }
    }

    /// <summary>
    /// Gets the block type badge text.
    /// </summary>
    public string Badge => Block.BlockType;

    /// <summary>
    /// Gets the icon kind representing this block type.
    /// </summary>
    public string IconKind => IsGroupHeader
        ? "FolderOutline"
        : IniBlockIconHelper.GetIconKind(Block.BlockType);

    /// <summary>
    /// Gets a value indicating whether the node can expand to show children.
    /// Group headers always expand; block nodes expand when they carry nested modules.
    /// </summary>
    public bool HasSubItems => IsGroupHeader || Block.Children.Count > 0;

    /// <summary>
    /// Creates a synthetic collapsible header grouping blocks of one type.
    /// </summary>
    /// <param name="blockType">The grouped block type.</param>
    /// <param name="count">The number of grouped blocks.</param>
    /// <returns>A group header node.</returns>
    public static IniTreeNodeViewModel CreateGroupHeader(string blockType, int count)
    {
        var node = new IniTreeNodeViewModel(new IniBlock { BlockType = blockType }, null)
        {
            IsGroupHeader = true,
            GroupCount = count,
            IsExpanded = true,
        };
        return node;
    }
}
