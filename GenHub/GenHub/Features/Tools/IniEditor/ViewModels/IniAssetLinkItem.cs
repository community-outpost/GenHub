using Avalonia.Media;

namespace GenHub.Features.Tools.IniEditor.ViewModels;

/// <summary>
/// A model, texture, or effect asset linked to the selected block for the canvas preview.
/// Selecting an entry navigates to the owning module node when one exists.
/// </summary>
public sealed class IniAssetLinkItem
{
    /// <summary>
    /// Gets the asset label such as the model or texture name.
    /// </summary>
    public string Label { get; }

    /// <summary>
    /// Gets the asset detail such as the owning module or field key.
    /// </summary>
    public string Detail { get; }

    /// <summary>
    /// Gets the icon kind representing the asset type.
    /// </summary>
    public string IconKind { get; }

    /// <summary>
    /// Gets the thumbnail image when the asset is a resolved texture.
    /// </summary>
    public IImage? Thumbnail { get; }

    /// <summary>
    /// Gets the tree node to select when navigating to the asset owner.
    /// </summary>
    public IniTreeNodeViewModel? OwnerNode { get; }

    /// <summary>
    /// Gets a value indicating whether the entry navigates to an owning node.
    /// </summary>
    public bool CanNavigate => OwnerNode != null;

    /// <summary>
    /// Initializes a new instance of the <see cref="IniAssetLinkItem"/> class.
    /// </summary>
    /// <param name="label">The asset label.</param>
    /// <param name="detail">The asset detail.</param>
    /// <param name="iconKind">The icon kind.</param>
    /// <param name="thumbnail">The optional thumbnail.</param>
    /// <param name="ownerNode">The optional owner node.</param>
    public IniAssetLinkItem(string label, string detail, string iconKind, IImage? thumbnail, IniTreeNodeViewModel? ownerNode)
    {
        Label = label;
        Detail = detail;
        IconKind = iconKind;
        Thumbnail = thumbnail;
        OwnerNode = ownerNode;
    }
}
