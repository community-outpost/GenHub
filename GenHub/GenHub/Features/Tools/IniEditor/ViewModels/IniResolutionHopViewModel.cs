using GenHub.Core.Helpers;

namespace GenHub.Features.Tools.IniEditor.ViewModels;

/// <summary>
/// A single hop in the cross-file model resolution path shown on the preview canvas.
/// </summary>
/// <param name="BlockType">The hop block type.</param>
/// <param name="Name">The hop block name.</param>
/// <param name="FilePath">The hop source file, or null for the open document.</param>
/// <param name="IsExternal">Whether the hop lives outside the open document.</param>
/// <param name="IsReverseHop">Whether the hop was reached through a reverse (used-by) edge.</param>
/// <param name="IsFirst">Whether the hop is the selected root block.</param>
public sealed record IniResolutionHopViewModel(
    string BlockType,
    string Name,
    string? FilePath,
    bool IsExternal,
    bool IsReverseHop,
    bool IsFirst)
{
    /// <summary>
    /// Gets the display title of the hop.
    /// </summary>
    public string Title => string.IsNullOrWhiteSpace(Name) ? BlockType : Name;

    /// <summary>
    /// Gets the icon kind representing the hop block type.
    /// </summary>
    public string IconKind => IniBlockIconHelper.GetIconKind(BlockType);

    /// <summary>
    /// Gets the direction icon kind for the hop edge.
    /// </summary>
    public string DirectionIconKind => IsReverseHop ? "ArrowLeft" : "ArrowRight";
}
