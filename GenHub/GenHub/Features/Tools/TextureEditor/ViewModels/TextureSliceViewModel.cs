using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using GenHub.Core.Helpers;
using GenHub.Core.Models.Tools.TextureEditor;
using System;
using System.Diagnostics.CodeAnalysis;

namespace GenHub.Features.Tools.TextureEditor.ViewModels;

/// <summary>
/// Editable view model for one MappedImage slice in the Texture Editor canvas.
/// </summary>
public sealed partial class TextureSliceViewModel : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private int _left;

    [ObservableProperty]
    private int _top;

    [ObservableProperty]
    private int _right;

    [ObservableProperty]
    private int _bottom;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private IImage? _thumbnail;

    private int _textureWidth;
    private int _textureHeight;
    private double _zoom = 1;
    private string _textureFileName = string.Empty;
    private string _status = GenHub.Core.Constants.TextureEditorConstants.DefaultStatus;

    /// <summary>
    /// Initializes a new instance of the <see cref="TextureSliceViewModel"/> class.
    /// </summary>
    /// <param name="definition">The initial mapped image definition.</param>
    public TextureSliceViewModel(MappedImageDefinition definition)
    {
        UpdateFrom(definition);
    }

    /// <summary>
    /// Gets the slice width in pixels using exclusive SAGE edges.
    /// </summary>
    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Reads source-generated coordinate instance state and is bound from XAML.")]
    public int Width => Right - Left;

    /// <summary>
    /// Gets the slice height in pixels using exclusive SAGE edges.
    /// </summary>
    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Reads source-generated coordinate instance state and is bound from XAML.")]
    public int Height => Bottom - Top;

    /// <summary>
    /// Gets the display X of the overlay rectangle.
    /// </summary>
    public double DisplayX => Left * _zoom;

    /// <summary>
    /// Gets the display Y of the overlay rectangle.
    /// </summary>
    public double DisplayY => Top * _zoom;

    /// <summary>
    /// Gets the display width of the overlay rectangle.
    /// </summary>
    public double DisplayWidth => Width * _zoom;

    /// <summary>
    /// Gets the display height of the overlay rectangle.
    /// </summary>
    public double DisplayHeight => Height * _zoom;

    /// <summary>
    /// Gets the half-width offset for center resize handles.
    /// </summary>
    public double HalfWidthMinusHandle => CanvasResizeHelper.CenterHandleOffset(DisplayWidth);

    /// <summary>
    /// Gets the half-height offset for middle resize handles.
    /// </summary>
    public double HalfHeightMinusHandle => CanvasResizeHelper.CenterHandleOffset(DisplayHeight);

    /// <summary>
    /// Gets the right offset for east resize handles.
    /// </summary>
    public double WidthMinusHandle => CanvasResizeHelper.EndHandleOffset(DisplayWidth);

    /// <summary>
    /// Gets the bottom offset for south resize handles.
    /// </summary>
    public double HeightMinusHandle => CanvasResizeHelper.EndHandleOffset(DisplayHeight);

    /// <summary>
    /// Gets a value indicating whether coordinates are ordered and inside the texture bounds.
    /// </summary>
    public bool IsWithinTexture =>
        Left >= 0 && Top >= 0 &&
        Right > Left && Bottom > Top &&
        _textureWidth > 0 && _textureHeight > 0 &&
        Right <= _textureWidth && Bottom <= _textureHeight;

    /// <summary>
    /// Gets a value indicating whether a 1px alpha guard border fits inside the texture.
    /// </summary>
    public bool HasGuardBorder =>
        Left > 0 && Top > 0 &&
        Right > Left && Bottom > Top &&
        _textureWidth > 0 && _textureHeight > 0 &&
        Right < _textureWidth && Bottom < _textureHeight;

    /// <summary>
    /// Updates editable state from a mapped image definition.
    /// </summary>
    /// <param name="definition">The mapped image definition.</param>
    public void UpdateFrom(MappedImageDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        _textureFileName = definition.TextureFileName;
        _textureWidth = definition.TextureWidth;
        _textureHeight = definition.TextureHeight;
        _status = definition.Status;
        Name = definition.Name;
        Left = definition.Left;
        Top = definition.Top;
        Right = definition.Right;
        Bottom = definition.Bottom;
        RefreshDerived();
    }

    /// <summary>
    /// Updates the zoom factor used for overlay display coordinates.
    /// </summary>
    /// <param name="zoom">The zoom factor.</param>
    public void UpdateZoom(double zoom)
    {
        _zoom = zoom;
        OnPropertyChanged(nameof(DisplayX));
        OnPropertyChanged(nameof(DisplayY));
        OnPropertyChanged(nameof(DisplayWidth));
        OnPropertyChanged(nameof(DisplayHeight));
        OnPropertyChanged(nameof(HalfWidthMinusHandle));
        OnPropertyChanged(nameof(HalfHeightMinusHandle));
        OnPropertyChanged(nameof(WidthMinusHandle));
        OnPropertyChanged(nameof(HeightMinusHandle));
    }

    /// <summary>
    /// Updates the texture dimensions used for validation.
    /// </summary>
    /// <param name="textureFileName">The texture file name.</param>
    /// <param name="textureWidth">The texture width.</param>
    /// <param name="textureHeight">The texture height.</param>
    public void UpdateTexture(string textureFileName, int textureWidth, int textureHeight)
    {
        _textureFileName = textureFileName;
        _textureWidth = textureWidth;
        _textureHeight = textureHeight;
        RefreshDerived();
    }

    /// <summary>
    /// Builds a mapped image definition from the current editable state.
    /// </summary>
    /// <returns>The mapped image definition.</returns>
    public MappedImageDefinition ToDefinition() => new(
        Name,
        _textureFileName,
        _textureWidth,
        _textureHeight,
        Left,
        Top,
        Right,
        Bottom,
        _status);

    partial void OnLeftChanged(int value) => RefreshDerived();

    partial void OnTopChanged(int value) => RefreshDerived();

    partial void OnRightChanged(int value) => RefreshDerived();

    partial void OnBottomChanged(int value) => RefreshDerived();

    private void RefreshDerived()
    {
        OnPropertyChanged(nameof(Width));
        OnPropertyChanged(nameof(Height));
        OnPropertyChanged(nameof(DisplayX));
        OnPropertyChanged(nameof(DisplayY));
        OnPropertyChanged(nameof(DisplayWidth));
        OnPropertyChanged(nameof(DisplayHeight));
        OnPropertyChanged(nameof(HalfWidthMinusHandle));
        OnPropertyChanged(nameof(HalfHeightMinusHandle));
        OnPropertyChanged(nameof(WidthMinusHandle));
        OnPropertyChanged(nameof(HeightMinusHandle));
        OnPropertyChanged(nameof(IsWithinTexture));
        OnPropertyChanged(nameof(HasGuardBorder));
    }
}
