using CommunityToolkit.Mvvm.ComponentModel;
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
        _label = window.Name?.Trim('"') ?? window.ControlTypeName;
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
    /// Updates the rendered geometry.
    /// </summary>
    /// <param name="x">The canvas X position.</param>
    /// <param name="y">The canvas Y position.</param>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    public void UpdateGeometry(double x, double y, double width, double height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }
}
