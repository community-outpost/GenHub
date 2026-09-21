using Avalonia.Media;
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
    [NotifyPropertyChangedFor(nameof(ShowNameTag))]
    private bool _isSelected;

    /// <summary>
    /// Gets or sets the game asset preview image, or null when unresolved.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasImage))]
    [NotifyPropertyChangedFor(nameof(ShowFill))]
    private Bitmap? _image;

    /// <summary>
    /// Gets a value indicating whether a preview image is available.
    /// </summary>
    public bool HasImage => Image != null;

    /// <summary>
    /// Gets or sets the draw-data background fill, or null for the default wireframe.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFill))]
    [NotifyPropertyChangedFor(nameof(ShowFill))]
    private IBrush? _fillOverlay;

    /// <summary>
    /// Gets a value indicating whether a background fill is available.
    /// </summary>
    public bool HasFill => FillOverlay != null;

    /// <summary>
    /// Gets a value indicating whether the fill shows (hidden once an image resolves).
    /// </summary>
    public bool ShowFill => HasFill && !HasImage;

    /// <summary>
    /// Gets or sets the draw-data border tint, or null for the default wireframe border.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBorderOverlay))]
    private IBrush? _borderOverlay;

    /// <summary>
    /// Gets a value indicating whether a border tint is available.
    /// </summary>
    public bool HasBorderOverlay => BorderOverlay != null;

    /// <summary>
    /// Gets or sets the overlaid control text, or null when the window declares none.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasContentText))]
    [NotifyPropertyChangedFor(nameof(ShowNameTag))]
    private string? _contentText;

    /// <summary>
    /// Gets a value indicating whether control text is available.
    /// </summary>
    public bool HasContentText => !string.IsNullOrEmpty(ContentText);

    /// <summary>
    /// Gets a value indicating whether the window-name tag shows (always without text, otherwise on selection).
    /// </summary>
    public bool ShowNameTag => !HasContentText || IsSelected;

    /// <summary>
    /// Gets or sets the control text brush.
    /// </summary>
    [ObservableProperty]
    private IBrush? _contentTextBrush;

    /// <summary>
    /// Gets or sets the control text size in device-independent pixels.
    /// </summary>
    [ObservableProperty]
    private double _contentFontSize = WndConstants.Editor.DefaultFontSize;

    /// <summary>
    /// Gets or sets the control text weight.
    /// </summary>
    [ObservableProperty]
    private FontWeight _contentFontWeight = FontWeight.Normal;

    /// <summary>
    /// Gets or sets the control text alignment.
    /// </summary>
    [ObservableProperty]
    private TextAlignment _contentTextAlignment = TextAlignment.Center;

    /// <summary>
    /// Gets or sets the item opacity, dimming windows the engine would hide.
    /// </summary>
    [ObservableProperty]
    private double _canvasOpacity = 1.0;
}
