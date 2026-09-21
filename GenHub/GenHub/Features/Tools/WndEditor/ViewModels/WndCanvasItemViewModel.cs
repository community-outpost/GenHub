using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using GenHub.Core.Constants;
using GenHub.Core.Models.Tools.WndEditor;

namespace GenHub.Features.Tools.WndEditor.ViewModels;

/// <summary>
/// A selectable rectangle on the preview canvas representing one window.
/// </summary>
public sealed partial class WndCanvasItemViewModel : ObservableObject
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WndCanvasItemViewModel"/> class.
    /// </summary>
    /// <param name="window">The represented window.</param>
    public WndCanvasItemViewModel(WndWindow window)
    {
        Window = window;
        var shortName = WndDecoratedName.Parse(window.GetProperty(WndConstants.PropertyKeys.Name)).ShortName;
        _label = string.IsNullOrWhiteSpace(shortName) ? window.ControlTypeName : shortName;
    }

    /// <summary>
    /// Gets the represented window.
    /// </summary>
    public WndWindow Window { get; }

    /// <summary>
    /// Gets or sets the canvas X position in device-independent pixels.
    /// </summary>
    [ObservableProperty]
    private double _x;

    /// <summary>
    /// Gets or sets the canvas Y position in device-independent pixels.
    /// </summary>
    [ObservableProperty]
    private double _y;

    /// <summary>
    /// Gets or sets the width in device-independent pixels.
    /// </summary>
    [ObservableProperty]
    private double _width;

    /// <summary>
    /// Gets or sets the height in device-independent pixels.
    /// </summary>
    [ObservableProperty]
    private double _height;

    /// <summary>
    /// Gets or sets the label shown inside the rectangle.
    /// </summary>
    [ObservableProperty]
    private string _label;

    /// <summary>
    /// Gets or sets whether the item is selected.
    /// </summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>
    /// Gets or sets the game asset preview image, or null when unresolved.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasImage))]
    private Bitmap? _image;

    /// <summary>
    /// Gets a value indicating whether a preview image is available.
    /// </summary>
    public bool HasImage => Image != null;
}
