using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using GenHub.Core.Helpers;
using GenHub.Core.Models.Tools.IniEditor;
using System.Collections.Generic;

namespace GenHub.Features.Tools.IniEditor.ViewModels;

/// <summary>
/// An overview card representation of a block on the shared editor canvas.
/// </summary>
public sealed partial class IniCanvasCardViewModel : ObservableObject
{
    [ObservableProperty]
    private IImage? _portrait;

    /// <summary>
    /// Gets or sets a value indicating whether the card is highlighted by the tree or 3D selection.
    /// </summary>
    [ObservableProperty]
    private bool _isHighlighted;

    /// <summary>
    /// Gets the underlying INI block.
    /// </summary>
    public IniBlock Block { get; }

    /// <summary>
    /// Gets the display title of the block.
    /// </summary>
    public string Title { get; }

    /// <summary>
    /// Gets the block type name.
    /// </summary>
    public string BlockType { get; }

    /// <summary>
    /// Gets the faction or side of the block, if available.
    /// </summary>
    public string? Side { get; }

    /// <summary>
    /// Gets the fallback icon kind shown when no portrait thumbnail is available.
    /// </summary>
    public string IconKind => IniBlockIconHelper.GetIconKind(BlockType);

    /// <summary>
    /// Gets the list of vital statistics to display on the card.
    /// </summary>
    public IReadOnlyList<CanvasVitalItem> Vitals { get; }

    /// <summary>
    /// Gets the source file backing the block, or null when it lives in the open document.
    /// </summary>
    public string? SourceFile { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="IniCanvasCardViewModel"/> class.
    /// </summary>
    /// <param name="block">The underlying INI block.</param>
    /// <param name="title">The title for the card.</param>
    /// <param name="blockType">The block type identifier.</param>
    /// <param name="side">The optional faction or side.</param>
    /// <param name="portrait">The optional portrait bitmap.</param>
    /// <param name="vitals">The list of vital items.</param>
    /// <param name="sourceFile">The source file backing the block, or null when it lives in the open document.</param>
    public IniCanvasCardViewModel(IniBlock block, string title, string blockType, string? side, IImage? portrait, IReadOnlyList<CanvasVitalItem> vitals, string? sourceFile = null)
    {
        Block = block;
        Title = title;
        BlockType = blockType;
        Side = side;
        _portrait = portrait;
        Vitals = vitals;
        SourceFile = sourceFile;
    }
}
