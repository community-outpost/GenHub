using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using GenHub.Core.Constants;
using System;

namespace GenHub.Common.Editors;

/// <summary>
/// Shared pan and zoom canvas host for the document editors.
/// Middle-drag always pans, left-drag pans while <see cref="IsPanMode"/> is set,
/// and Ctrl+mouse wheel zooms anchored at the cursor. Tools only provide the
/// sized canvas content and bind <see cref="Zoom"/> two-way, so pan and zoom
/// behavior is never reimplemented per editor.
/// </summary>
/// <remarks>
/// Canvas QOL such as axis-lock drag, keyboard nudge, and resize handles
/// belongs to the content editors, standardized on the WND canvas patterns;
/// this host stays limited to pan and zoom.
/// </remarks>
public class EditorCanvasControl : ContentControl
{
    /// <summary>
    /// Defines the <see cref="Zoom"/> property.
    /// </summary>
    public static readonly StyledProperty<double> ZoomProperty =
        AvaloniaProperty.Register<EditorCanvasControl, double>(
            nameof(Zoom),
            defaultValue: EditorConstants.ZoomDefault,
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    /// <summary>
    /// Defines the <see cref="MinZoom"/> property.
    /// </summary>
    public static readonly StyledProperty<double> MinZoomProperty =
        AvaloniaProperty.Register<EditorCanvasControl, double>(
            nameof(MinZoom),
            defaultValue: EditorConstants.ZoomMin);

    /// <summary>
    /// Defines the <see cref="MaxZoom"/> property.
    /// </summary>
    public static readonly StyledProperty<double> MaxZoomProperty =
        AvaloniaProperty.Register<EditorCanvasControl, double>(
            nameof(MaxZoom),
            defaultValue: EditorConstants.ZoomMax);

    /// <summary>
    /// Defines the <see cref="WheelZoomFactor"/> property.
    /// </summary>
    public static readonly StyledProperty<double> WheelZoomFactorProperty =
        AvaloniaProperty.Register<EditorCanvasControl, double>(
            nameof(WheelZoomFactor),
            defaultValue: EditorConstants.ZoomWheelFactor);

    /// <summary>
    /// Defines the <see cref="IsPanMode"/> property.
    /// </summary>
    public static readonly StyledProperty<bool> IsPanModeProperty =
        AvaloniaProperty.Register<EditorCanvasControl, bool>(
            nameof(IsPanMode),
            defaultValue: false,
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    private static readonly Cursor PanCursor = new(StandardCursorType.Hand);

    private ScrollViewer? _scrollViewer;
    private bool _isPanning;
    private Point _panStart;
    private Vector _panOrigin;
    private Cursor? _previousCursor;
    private Point _pendingContentAnchor;
    private Point _pendingViewportAnchor;
    private double _pendingZoomScale = double.NaN;
    private Vector? _pendingFrameOffset;

    /// <summary>
    /// Gets or sets the canvas zoom factor.
    /// </summary>
    public double Zoom
    {
        get => GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, value);
    }

    /// <summary>
    /// Gets or sets the minimum canvas zoom factor.
    /// </summary>
    public double MinZoom
    {
        get => GetValue(MinZoomProperty);
        set => SetValue(MinZoomProperty, value);
    }

    /// <summary>
    /// Gets or sets the maximum canvas zoom factor.
    /// </summary>
    public double MaxZoom
    {
        get => GetValue(MaxZoomProperty);
        set => SetValue(MaxZoomProperty, value);
    }

    /// <summary>
    /// Gets or sets the multiplicative zoom factor applied per wheel notch.
    /// </summary>
    public double WheelZoomFactor
    {
        get => GetValue(WheelZoomFactorProperty);
        set => SetValue(WheelZoomFactorProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether left-drag pans the canvas instead of interacting with content.
    /// </summary>
    public bool IsPanMode
    {
        get => GetValue(IsPanModeProperty);
        set => SetValue(IsPanModeProperty, value);
    }

    /// <summary>
    /// Scrolls the canvas to the given content offset on the next layout pass.
    /// </summary>
    /// <param name="offset">The content offset to show.</param>
    public void FrameTo(Vector offset)
    {
        _pendingZoomScale = double.NaN;
        _pendingFrameOffset = offset;
    }

    /// <inheritdoc />
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        DetachScrollViewer();
        _scrollViewer = e.NameScope.Find<ScrollViewer>("PART_Scroll");
        if (_scrollViewer is null)
        {
            return;
        }

        _scrollViewer.PointerPressed += OnScrollPointerPressed;
        _scrollViewer.PointerMoved += OnScrollPointerMoved;
        _scrollViewer.PointerReleased += OnScrollPointerReleased;
        _scrollViewer.PointerCaptureLost += OnScrollPointerCaptureLost;
        _scrollViewer.AddHandler(
            InputElement.PointerWheelChangedEvent,
            OnScrollWheelChanged,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        _scrollViewer.LayoutUpdated += OnScrollLayoutUpdated;
    }

    private void DetachScrollViewer()
    {
        if (_scrollViewer is null)
        {
            return;
        }

        _scrollViewer.PointerPressed -= OnScrollPointerPressed;
        _scrollViewer.PointerMoved -= OnScrollPointerMoved;
        _scrollViewer.PointerReleased -= OnScrollPointerReleased;
        _scrollViewer.PointerCaptureLost -= OnScrollPointerCaptureLost;
        _scrollViewer.RemoveHandler(InputElement.PointerWheelChangedEvent, OnScrollWheelChanged);
        _scrollViewer.LayoutUpdated -= OnScrollLayoutUpdated;
        _scrollViewer = null;
    }

    private void OnScrollPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_scrollViewer is null)
        {
            return;
        }

        var point = e.GetCurrentPoint(_scrollViewer);
        if (!point.Properties.IsMiddleButtonPressed && !(IsPanMode && point.Properties.IsLeftButtonPressed))
        {
            return;
        }

        _isPanning = true;
        _panStart = e.GetPosition(_scrollViewer);
        _panOrigin = _scrollViewer.Offset;
        _previousCursor = Cursor;
        Cursor = PanCursor;
        e.Pointer.Capture(_scrollViewer);
        e.Handled = true;
    }

    private void OnScrollPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isPanning || _scrollViewer is null)
        {
            return;
        }

        var position = e.GetPosition(_scrollViewer);
        _scrollViewer.Offset = _panOrigin - (position - _panStart);
    }

    private void OnScrollPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        EndPan();
    }

    private void OnScrollPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        EndPan();
    }

    private void EndPan()
    {
        if (!_isPanning)
        {
            return;
        }

        _isPanning = false;
        Cursor = _previousCursor;
        _previousCursor = null;
    }

    private void OnScrollWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (_scrollViewer is null || !e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            return;
        }

        // Ctrl+wheel zooms instead of scrolling. The tunnel subscription runs
        // before the scroll viewer pans, so marking handled here suppresses
        // the scroll even mid-range instead of only at the scrollbar limits.
        e.Handled = true;
        if (e.Delta.Y == 0)
        {
            return;
        }

        double factor = e.Delta.Y > 0 ? WheelZoomFactor : 1.0 / WheelZoomFactor;
        double newZoom = Math.Clamp(Zoom * factor, MinZoom, MaxZoom);
        if (Math.Abs(newZoom - Zoom) < 0.0001)
        {
            e.Handled = true;
            return;
        }

        var viewportPoint = e.GetPosition(_scrollViewer);
        _pendingContentAnchor = new Point(
            _scrollViewer.Offset.X + viewportPoint.X,
            _scrollViewer.Offset.Y + viewportPoint.Y);
        _pendingViewportAnchor = viewportPoint;
        _pendingZoomScale = newZoom / Zoom;
        _pendingFrameOffset = null;
        Zoom = newZoom;
        e.Handled = true;
    }

    private void OnScrollLayoutUpdated(object? sender, EventArgs e)
    {
        if (_scrollViewer is null)
        {
            return;
        }

        if (!double.IsNaN(_pendingZoomScale))
        {
            _scrollViewer.Offset = (_pendingContentAnchor * _pendingZoomScale) - _pendingViewportAnchor;
            _pendingZoomScale = double.NaN;
            return;
        }

        if (_pendingFrameOffset is { } offset)
        {
            _pendingFrameOffset = null;
            _scrollViewer.Offset = offset;
        }
    }
}
