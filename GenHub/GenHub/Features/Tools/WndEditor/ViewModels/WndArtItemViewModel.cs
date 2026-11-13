using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace GenHub.Features.Tools.WndEditor.ViewModels;

/// <summary>
/// Represents an art asset entry in the WND editor art library sidebar.
/// </summary>
[SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Instance properties required for Avalonia UI compiled data bindings")]
public sealed partial class WndArtItemViewModel : ObservableObject
{
    [ObservableProperty]
    private Bitmap? _thumbnail;

    /// <summary>
    /// Initializes a new instance of the <see cref="WndArtItemViewModel"/> class.
    /// </summary>
    /// <param name="name">The mapped art image name.</param>
    /// <param name="tooltip">The hover tooltip text.</param>
    /// <param name="thumbnail">Optional cached preview bitmap.</param>
    public WndArtItemViewModel(string name, string tooltip, Bitmap? thumbnail = null)
    {
        Name = name;
        Tooltip = tooltip;
        _thumbnail = thumbnail;
    }

    /// <summary>
    /// Gets the mapped art image name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the hover tooltip text.
    /// </summary>
    public string Tooltip { get; }
}
