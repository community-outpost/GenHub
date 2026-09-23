using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using GenHub.Core.Constants;
using GenHub.Core.Models.Tools.WndEditor;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace GenHub.Features.Tools.WndEditor.ViewModels;

/// <summary>
/// A selectable rectangle on the preview canvas representing one window.
/// </summary>
[SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Instance properties required for Avalonia UI compiled data bindings")]
[SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Instance properties required for Avalonia UI compiled data bindings")]
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
        IsFallbackLabel = string.IsNullOrWhiteSpace(shortName);
    }

    /// <summary>
    /// Gets the represented window.
    /// </summary>
    public WndWindow Window { get; }

    /// <summary>
    /// Gets a value indicating whether the label falls back to the control type name.
    /// </summary>
    public bool IsFallbackLabel { get; }

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
    [NotifyPropertyChangedFor(nameof(HalfWidthMinusHandle))]
    [NotifyPropertyChangedFor(nameof(WidthMinusHandle))]
    private double _width;

    /// <summary>
    /// Gets or sets the height in device-independent pixels.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HalfHeightMinusHandle))]
    [NotifyPropertyChangedFor(nameof(HeightMinusHandle))]
    private double _height;

#pragma warning disable S2325 // Instance properties required for Avalonia UI compiled data bindings
    /// <summary>
    /// Gets the half-width offset for center resize handles.
    /// </summary>
    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Instance property bound to UI in Avalonia XAML")]
    [SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Instance property bound to UI in Avalonia XAML")]
    public double HalfWidthMinusHandle => Math.Max(0, (Width / 2.0) - 5.0);

    /// <summary>
    /// Gets the half-height offset for middle resize handles.
    /// </summary>
    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Instance property bound to UI in Avalonia XAML")]
    [SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Instance property bound to UI in Avalonia XAML")]
    public double HalfHeightMinusHandle => Math.Max(0, (Height / 2.0) - 5.0);

    /// <summary>
    /// Gets the right offset for east resize handles.
    /// </summary>
    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Instance property bound to UI in Avalonia XAML")]
    [SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Instance property bound to UI in Avalonia XAML")]
    public double WidthMinusHandle => Math.Max(0, Width - 5.0);

    /// <summary>
    /// Gets the bottom offset for south resize handles.
    /// </summary>
    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Instance property bound to UI in Avalonia XAML")]
    [SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Instance property bound to UI in Avalonia XAML")]
    public double HeightMinusHandle => Math.Max(0, Height - 5.0);
#pragma warning restore S2325

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
    [NotifyPropertyChangedFor(nameof(ShowPrimaryNameTag))]
    [NotifyPropertyChangedFor(nameof(ShowFallbackNameTag))]
    [NotifyPropertyChangedFor(nameof(CanvasVisible))]
    private bool _isSelected;

    /// <summary>
    /// Gets or sets whether the engine would hide the window (HIDDEN status flag).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanvasVisible))]
    private bool _isPreviewHidden;

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
    [SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Instance property bound to UI in Avalonia XAML")]
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
    [SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Instance property bound to UI in Avalonia XAML")]
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
    [SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Instance property bound to UI in Avalonia XAML")]
    public bool HasBorderOverlay => BorderOverlay != null;

    /// <summary>
    /// Gets or sets the overlaid control text, or null when the window declares none.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasContentText))]
    [NotifyPropertyChangedFor(nameof(ShowNameTag))]
    [NotifyPropertyChangedFor(nameof(ShowPrimaryNameTag))]
    [NotifyPropertyChangedFor(nameof(ShowFallbackNameTag))]
    private string? _contentText;

    /// <summary>
    /// Gets a value indicating whether control text is available.
    /// </summary>
    [SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Instance property bound to UI in Avalonia XAML")]
    public bool HasContentText => !string.IsNullOrEmpty(ContentText);

    /// <summary>
    /// Gets a value indicating whether the window-name tag shows (for text controls without content, or on selection).
    /// </summary>
    public bool ShowNameTag => (Window.ControlType == WndControlType.StaticText && !HasContentText && !HasImage) || IsSelected;

    /// <summary>
    /// Gets a value indicating whether the named tag shows in the primary style.
    /// </summary>
    public bool ShowPrimaryNameTag => ShowNameTag && !IsFallbackLabel;

    /// <summary>
    /// Gets a value indicating whether the fallback control-type tag shows muted.
    /// </summary>
    public bool ShowFallbackNameTag => ShowNameTag && IsFallbackLabel;

    /// <summary>
    /// Gets a value indicating whether the item shows on the canvas.
    /// Engine-hidden windows stay invisible unless selected for editing.
    /// </summary>
    [SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Instance property bound to UI in Avalonia XAML")]
    public bool CanvasVisible => !IsPreviewHidden || IsSelected;

    /// <summary>
    /// Gets or sets the control text font family.
    /// </summary>
    [ObservableProperty]
    private FontFamily? _contentFontFamily;

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
    /// Gets or sets the control text padding, shifted right when a glyph shows.
    /// </summary>
    [ObservableProperty]
    private Thickness _contentTextPadding = new(4, 2);

    /// <summary>
    /// Gets or sets the check box or radio button glyph, or null when unresolved.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGlyph))]
    private Bitmap? _glyphImage;

    /// <summary>
    /// Gets a value indicating whether a glyph is available.
    /// </summary>
    [SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Instance property bound to UI in Avalonia XAML")]
    public bool HasGlyph => GlyphImage != null;

    /// <summary>
    /// Gets or sets the glyph width in device-independent pixels.
    /// </summary>
    [ObservableProperty]
    private double _glyphWidth;

    /// <summary>
    /// Gets or sets the glyph height in device-independent pixels.
    /// </summary>
    [ObservableProperty]
    private double _glyphHeight;

    /// <summary>
    /// Gets or sets the sub-gadget art overlays positioned inside the item.
    /// </summary>
    [ObservableProperty]
    private IReadOnlyList<WndCanvasOverlayViewModel> _overlays = [];

    /// <summary>
    /// Gets or sets the item opacity, dimming windows the engine would hide.
    /// </summary>
    [ObservableProperty]
    private double _canvasOpacity = 1.0;
}
