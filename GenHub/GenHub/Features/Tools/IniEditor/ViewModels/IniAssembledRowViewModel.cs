using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace GenHub.Features.Tools.IniEditor.ViewModels;

/// <summary>
/// A single resolved attachment row in the assembled preview (weapon, armor, upgrade,
/// command button, model, or texture reference with its origin and tooltip).
/// </summary>
public sealed partial class IniAssembledRowViewModel : ObservableObject
{
    /// <summary>
    /// Initializes a new instance of the <see cref="IniAssembledRowViewModel"/> class.
    /// </summary>
    /// <param name="label">The row label.</param>
    /// <param name="value">The resolved value display.</param>
    /// <param name="detail">The origin detail (defining file or source).</param>
    /// <param name="tooltip">The full tooltip text.</param>
    /// <param name="textureName">The mapped image name, when the row references a texture.</param>
    public IniAssembledRowViewModel(string label, string value, string? detail, string? tooltip, string? textureName)
    {
        Label = label;
        Value = value;
        Detail = detail;
        Tooltip = tooltip;
        TextureName = textureName;
    }

    /// <summary>
    /// Gets the row label.
    /// </summary>
    public string Label { get; }

    /// <summary>
    /// Gets the resolved value display.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Gets the origin detail (defining file or source).
    /// </summary>
    public string? Detail { get; }

    /// <summary>
    /// Gets the full tooltip text.
    /// </summary>
    public string? Tooltip { get; }

    /// <summary>
    /// Gets the mapped image name, when the row references a texture.
    /// </summary>
    public string? TextureName { get; }

    /// <summary>
    /// Gets a value indicating whether the row references a texture.
    /// </summary>
    public bool HasTexture => TextureName != null;

    /// <summary>
    /// Gets or sets the texture thumbnail, loaded asynchronously.
    /// </summary>
    [ObservableProperty]
    private IImage? _textureThumbnail;
}
