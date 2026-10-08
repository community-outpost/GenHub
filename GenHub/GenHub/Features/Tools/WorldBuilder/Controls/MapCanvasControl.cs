using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using GenHub.Core.Models.Tools.WorldBuilder;
using System;
using System.Collections.Generic;

namespace GenHub.Features.Tools.WorldBuilder.Controls;

/// <summary>
/// High-performance interactive map canvas control supporting pan, zoom, 3D camera tilt/orbit, and cell drawing.
/// </summary>
public sealed class MapCanvasControl : Control
{
    /// <summary>Defines the <see cref="Source"/> property.</summary>
    public static readonly StyledProperty<WriteableBitmap?> SourceProperty =
        AvaloniaProperty.Register<MapCanvasControl, WriteableBitmap?>("Source");

    /// <summary>Defines the <see cref="Zoom"/> property.</summary>
    public static readonly StyledProperty<double> ZoomProperty =
        AvaloniaProperty.Register<MapCanvasControl, double>("Zoom", 1.0);

    /// <summary>Defines the <see cref="BrushRadiusCells"/> property.</summary>
    public static readonly StyledProperty<double> BrushRadiusCellsProperty =
        AvaloniaProperty.Register<MapCanvasControl, double>("BrushRadiusCells", 3.0);

    /// <summary>Defines the <see cref="ShowBrush"/> property.</summary>
    public static readonly StyledProperty<bool> ShowBrushProperty =
        AvaloniaProperty.Register<MapCanvasControl, bool>("ShowBrush", true);

    /// <summary>Defines the <see cref="ActiveLinePreview"/> property.</summary>
    public static readonly StyledProperty<(Point Start, Point End)?> ActiveLinePreviewProperty =
        AvaloniaProperty.Register<MapCanvasControl, (Point Start, Point End)?>("ActiveLinePreview");

    /// <summary>Defines the <see cref="ViewMode"/> property.</summary>
    public static readonly StyledProperty<MapCanvasViewMode> ViewModeProperty =
        AvaloniaProperty.Register<MapCanvasControl, MapCanvasViewMode>("ViewMode", MapCanvasViewMode.Isometric3D);

    /// <summary>Defines the <see cref="MapWidth"/> property.</summary>
    public static readonly StyledProperty<int> MapWidthProperty =
        AvaloniaProperty.Register<MapCanvasControl, int>("MapWidth", 120);

    /// <summary>Defines the <see cref="MapHeight"/> property.</summary>
    public static readonly StyledProperty<int> MapHeightProperty =
        AvaloniaProperty.Register<MapCanvasControl, int>("MapHeight", 120);

    /// <summary>Defines the <see cref="CameraPitch"/> property.</summary>
    public static readonly StyledProperty<double> CameraPitchProperty =
        AvaloniaProperty.Register<MapCanvasControl, double>("CameraPitch", 45.0, defaultBindingMode: BindingMode.TwoWay);

    /// <summary>Defines the <see cref="CameraYaw"/> property.</summary>
    public static readonly StyledProperty<double> CameraYawProperty =
        AvaloniaProperty.Register<MapCanvasControl, double>("CameraYaw", 45.0, defaultBindingMode: BindingMode.TwoWay);

    /// <summary>Defines the <see cref="TerrainHeights"/> property.</summary>
    public static readonly StyledProperty<IList<byte>?> TerrainHeightsProperty =
        AvaloniaProperty.Register<MapCanvasControl, IList<byte>?>("TerrainHeights");

    private const double MinZoom = 0.2;
    private const double MaxZoom = 16.0;
    private const float IsometricCellScale = 6.0f;
    private const float IsometricHeightScale = 0.35f;

    private Point _panOffset;
    private Point? _mousePosition;
    private Point? _panStart;
    private Point _panOffsetStart;
    private bool _isOrbiting;
    private Point _orbitStart;
    private double _orbitPitchStart;
    private double _orbitYawStart;

    static MapCanvasControl()
    {
        AffectsRender<MapCanvasControl>(
            SourceProperty,
            ZoomProperty,
            BrushRadiusCellsProperty,
            ShowBrushProperty,
            ActiveLinePreviewProperty,
            ViewModeProperty,
            CameraPitchProperty,
            CameraYawProperty,
            TerrainHeightsProperty);
    }

    /// <summary>Raised when a pointer press lands on the canvas.</summary>
    public event Action<CellPointerEventArgs>? CellPressed;

    /// <summary>Raised when the pointer moves over the canvas.</summary>
    public event Action<CellPointerEventArgs>? CellMoved;

    /// <summary>Raised when a pointer is released over the canvas.</summary>
    public event Action<CellPointerEventArgs>? CellReleased;

    /// <summary>Raised when the '[' shortcut is pressed to decrease brush radius.</summary>
    public event Action? BrushRadiusDecreased;

    /// <summary>Raised when the ']' shortcut is pressed to increase brush radius.</summary>
    public event Action? BrushRadiusIncreased;

    /// <summary>Gets or sets the map bitmap.</summary>
    public WriteableBitmap? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    /// <summary>Gets or sets the display zoom factor.</summary>
    public double Zoom
    {
        get => GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, Math.Clamp(value, MinZoom, MaxZoom));
    }

    /// <summary>Gets or sets the brush radius in map cells.</summary>
    public double BrushRadiusCells
    {
        get => GetValue(BrushRadiusCellsProperty);
        set => SetValue(BrushRadiusCellsProperty, Math.Max(0.5, value));
    }

    /// <summary>Gets or sets a value indicating whether the brush circle indicator is shown.</summary>
    public bool ShowBrush
    {
        get => GetValue(ShowBrushProperty);
        set => SetValue(ShowBrushProperty, value);
    }

    /// <summary>Gets or sets the active two-point line preview endpoints in cells.</summary>
    public (Point Start, Point End)? ActiveLinePreview
    {
        get => GetValue(ActiveLinePreviewProperty);
        set => SetValue(ActiveLinePreviewProperty, value);
    }

    /// <summary>Gets or sets the projection view mode.</summary>
    public MapCanvasViewMode ViewMode
    {
        get => GetValue(ViewModeProperty);
        set => SetValue(ViewModeProperty, value);
    }

    /// <summary>Gets or sets the map width in cells.</summary>
    public int MapWidth
    {
        get => GetValue(MapWidthProperty);
        set => SetValue(MapWidthProperty, value);
    }

    /// <summary>Gets or sets the map height in cells.</summary>
    public int MapHeight
    {
        get => GetValue(MapHeightProperty);
        set => SetValue(MapHeightProperty, value);
    }

    /// <summary>Gets or sets the 3D camera pitch in degrees (15-90).</summary>
    public double CameraPitch
    {
        get => GetValue(CameraPitchProperty);
        set => SetValue(CameraPitchProperty, Math.Clamp(value, 15.0, 90.0));
    }

    /// <summary>Gets or sets the 3D camera yaw in degrees (0-360).</summary>
    public double CameraYaw
    {
        get => GetValue(CameraYawProperty);
        set => SetValue(CameraYawProperty, ((value % 360.0) + 360.0) % 360.0);
    }

    /// <summary>Gets or sets the terrain height map for 3D cursor picking and surface projection.</summary>
    public IList<byte>? TerrainHeights
    {
        get => GetValue(TerrainHeightsProperty);
        set => SetValue(TerrainHeightsProperty, value);
    }

    /// <summary>Gets or sets the ruler line endpoints in cells.</summary>
    public (Point Start, Point End)? RulerLine { get; set; }

    /// <summary>Gets the in-progress trigger polygon in cells.</summary>
    public List<Point> TriggerPolygon { get; } = [];

    /// <summary>
    /// Fits the whole map bitmap into the control bounds.
    /// </summary>
    public void ZoomToFit()
    {
        if (Source == null || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        Zoom = Math.Min(Bounds.Width / Source.Size.Width, Bounds.Height / Source.Size.Height);
        _panOffset = new Point(
            (Bounds.Width - (Source.Size.Width * Zoom)) / 2,
            (Bounds.Height - (Source.Size.Height * Zoom)) / 2);
        InvalidateVisual();
    }

    /// <summary>
    /// Centers the viewport on a specific cell.
    /// </summary>
    /// <param name="cellX">The cell X coordinate.</param>
    /// <param name="cellY">The cell Y coordinate.</param>
    public void CenterOnCell(int cellX, int cellY)
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        var screenPoint = CellToPoint(new Point(cellX, cellY));
        var currentCenterX = Bounds.Width / 2.0;
        var currentCenterY = Bounds.Height / 2.0;

        _panOffset = new Point(
            _panOffset.X + (currentCenterX - screenPoint.X),
            _panOffset.Y + (currentCenterY - screenPoint.Y));
        InvalidateVisual();
    }

    /// <summary>
    /// Converts a control point to a map cell.
    /// </summary>
    /// <param name="point">The control point.</param>
    /// <returns>The map cell.</returns>
    public Point PointToCell(Point point)
    {
        var bx = (point.X - _panOffset.X) / Zoom;
        var by = (point.Y - _panOffset.Y) / Zoom;

        if (ViewMode == MapCanvasViewMode.Isometric3D)
        {
            var proj = GetIsometricProjection();
            return RaycastIsometricCell((float)bx, (float)by, proj);
        }

        return new Point(
            Math.Clamp(Math.Round(bx), 0, Math.Max(0, MapWidth - 1)),
            Math.Clamp(Math.Round(by), 0, Math.Max(0, MapHeight - 1)));
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var background = ResolveBrush("SurfaceBackgroundBrush", "SurfaceElevatedBrush");
        if (background != null)
        {
            context.FillRectangle(background, Bounds);
        }

        if (Source != null)
        {
            var dest = new Rect(_panOffset, new Size(Source.Size.Width * Zoom, Source.Size.Height * Zoom));
            context.DrawImage(Source, new Rect(Source.Size), dest);
        }

        var overlay = ResolveBrush("TextPrimary", "AccentBrush");
        if (ShowBrush && _mousePosition != null && overlay != null)
        {
            var radius = BrushRadiusCells * Zoom * 4.0;
            context.DrawEllipse(null, new Pen(overlay, 1.5), _mousePosition.Value, radius, radius);
        }

        var ruler = ResolveBrush("GeneralsAccentBrush", "AccentBrush");
        if (ruler == null)
        {
            return;
        }

        var rulerPen = new Pen(ruler, 2);
        if (ActiveLinePreview != null)
        {
            var p1 = CellToPoint(ActiveLinePreview.Value.Start);
            var p2 = CellToPoint(ActiveLinePreview.Value.End);
            context.DrawLine(rulerPen, p1, p2);
            context.DrawEllipse(ruler, null, p1, 3, 3);
            context.DrawEllipse(ruler, null, p2, 3, 3);
        }

        if (RulerLine != null)
        {
            context.DrawLine(rulerPen, CellToPoint(RulerLine.Value.Start), CellToPoint(RulerLine.Value.End));
        }

        if (TriggerPolygon.Count > 1)
        {
            for (var i = 0; i < TriggerPolygon.Count; i++)
            {
                var a = CellToPoint(TriggerPolygon[i]);
                var b = CellToPoint(TriggerPolygon[(i + 1) % TriggerPolygon.Count]);
                context.DrawLine(rulerPen, a, b);
            }
        }
    }

    /// <inheritdoc />
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        var point = e.GetPosition(this);
        var pointerProps = e.GetCurrentPoint(this).Properties;

        CellPressed?.Invoke(ToCellArgs(point, e));

        if (pointerProps.IsRightButtonPressed || pointerProps.IsMiddleButtonPressed)
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Alt) || pointerProps.IsMiddleButtonPressed)
            {
                _isOrbiting = true;
                _orbitStart = point;
                _orbitPitchStart = CameraPitch;
                _orbitYawStart = CameraYaw;
                e.Pointer.Capture(this);
                e.Handled = true;
                return;
            }

            _panStart = point;
            _panOffsetStart = _panOffset;
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        if (pointerProps.IsLeftButtonPressed)
        {
            e.Pointer.Capture(this);
        }

        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var point = e.GetPosition(this);
        _mousePosition = point;

        if (_isOrbiting)
        {
            var dx = point.X - _orbitStart.X;
            var dy = point.Y - _orbitStart.Y;
            CameraYaw = _orbitYawStart + (dx * 0.4);
            CameraPitch = Math.Clamp(_orbitPitchStart - (dy * 0.4), 15.0, 90.0);
            InvalidateVisual();
            return;
        }

        if (_panStart != null)
        {
            _panOffset = _panOffsetStart + (point - _panStart.Value);
            InvalidateVisual();
            return;
        }

        CellMoved?.Invoke(ToCellArgs(point, e));
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_isOrbiting)
        {
            _isOrbiting = false;
            e.Pointer.Capture(null);
            e.Handled = true;
            return;
        }

        if (_panStart != null)
        {
            _panStart = null;
            e.Pointer.Capture(null);
            e.Handled = true;
            return;
        }

        e.Pointer.Capture(null);
        CellReleased?.Invoke(ToCellArgs(e.GetPosition(this), e));
        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (e.Delta.Y == 0)
        {
            return;
        }

        var point = e.GetPosition(this);
        var before = PointToCell(point);
        Zoom *= e.Delta.Y > 0 ? 1.2 : 1 / 1.2;
        var after = CellToPoint(before);
        _panOffset = new Point(
            _panOffset.X + (point.X - after.X),
            _panOffset.Y + (point.Y - after.Y));
        InvalidateVisual();
        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (TryHandleBrushKey(e) || TryHandleCameraKey(e) || TryHandlePanKey(e))
        {
            e.Handled = true;
        }
    }

    /// <inheritdoc />
    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _mousePosition = null;
        InvalidateVisual();
    }

    private static IBrush? ResolveBrush(string primaryKey, string fallbackKey)
    {
        if (Application.Current?.TryFindResource(primaryKey, out var primary) == true && primary is IBrush primaryBrush)
        {
            return primaryBrush;
        }

        if (Application.Current?.TryFindResource(fallbackKey, out var fallback) == true && fallback is IBrush fallbackBrush)
        {
            return fallbackBrush;
        }

        return null;
    }

    private Point CellToPoint(Point cell)
    {
        if (ViewMode == MapCanvasViewMode.Isometric3D)
        {
            var proj = GetIsometricProjection();
            var dx = (float)cell.X - proj.Cx;
            var dy = (float)cell.Y - proj.Cy;
            var dz = 0f;

            if (TerrainHeights is { Count: > 0 })
            {
                var idx = ((int)cell.Y * MapWidth) + (int)cell.X;
                if (idx >= 0 && idx < TerrainHeights.Count)
                {
                    dz = (TerrainHeights[idx] - 20f) * IsometricHeightScale;
                }
            }

            var x1 = (dx * proj.CosYaw) - (dy * proj.SinYaw);
            var y1 = (dx * proj.SinYaw) + (dy * proj.CosYaw);
            var xCam = x1;
            var yCam = (y1 * proj.SinPitch) - (dz * proj.CosPitch);

            var bx = (xCam * IsometricCellScale) + proj.OriginX;
            var by = (yCam * IsometricCellScale) + proj.OriginY;

            return new Point((bx * Zoom) + _panOffset.X, (by * Zoom) + _panOffset.Y);
        }

        return new Point((cell.X * Zoom) + _panOffset.X, (cell.Y * Zoom) + _panOffset.Y);
    }

    private (float Cx, float Cy, float CosYaw, float SinYaw, float CosPitch, float SinPitch, float OriginX, float OriginY) GetIsometricProjection()
    {
        var pitchRad = (float)(CameraPitch * Math.PI / 180.0);
        var yawRad = (float)(CameraYaw * Math.PI / 180.0);
        var cosYaw = MathF.Cos(yawRad);
        var sinYaw = MathF.Sin(yawRad);
        var cosPitch = MathF.Cos(pitchRad);
        var sinPitch = MathF.Sin(pitchRad);

        var cx = MapWidth * 0.5f;
        var cy = MapHeight * 0.5f;

        var minX = float.MaxValue;
        var minY = float.MaxValue;
        var corners = new (float X, float Y, float Z)[]
        {
            (0, 0, 0), (MapWidth, 0, 0), (MapWidth, MapHeight, 0), (0, MapHeight, 0),
            (0, 0, 255), (MapWidth, 0, 255), (MapWidth, MapHeight, 255), (0, MapHeight, 255),
        };

        foreach (var c in corners)
        {
            var cdx = c.X - cx;
            var cdy = c.Y - cy;
            var cdz = (c.Z - 20f) * IsometricHeightScale;
            var cx1 = (cdx * cosYaw) - (cdy * sinYaw);
            var cy1 = (cdx * sinYaw) + (cdy * cosYaw);
            var cyCam = (cy1 * sinPitch) - (cdz * cosPitch);
            if (cx1 < minX)
            {
                minX = cx1;
            }

            if (cyCam < minY)
            {
                minY = cyCam;
            }
        }

        var originX = (-minX * IsometricCellScale) + 32f;
        var originY = (-minY * IsometricCellScale) + 32f;

        return (cx, cy, cosYaw, sinYaw, cosPitch, sinPitch, originX, originY);
    }

    private Point RaycastIsometricCell(
        float bx,
        float by,
        (float Cx, float Cy, float CosYaw, float SinYaw, float CosPitch, float SinPitch, float OriginX, float OriginY) proj)
    {
        var xCam = (bx - proj.OriginX) / IsometricCellScale;
        var yCam = (by - proj.OriginY) / IsometricCellScale;

        var cellX = proj.Cx;
        var cellY = proj.Cy;
        var dz = 0f;

        for (var iter = 0; iter < 3; iter++)
        {
            var y1 = proj.SinPitch > 0.05f ? (yCam + (dz * proj.CosPitch)) / proj.SinPitch : yCam;
            var x1 = xCam;

            var rdx = (x1 * proj.CosYaw) + (y1 * proj.SinYaw);
            var rdy = (-x1 * proj.SinYaw) + (y1 * proj.CosYaw);

            cellX = Math.Clamp(rdx + proj.Cx, 0f, MathF.Max(0f, MapWidth - 1));
            cellY = Math.Clamp(rdy + proj.Cy, 0f, MathF.Max(0f, MapHeight - 1));

            if (TerrainHeights is { Count: > 0 })
            {
                var idx = ((int)cellY * MapWidth) + (int)cellX;
                if (idx >= 0 && idx < TerrainHeights.Count)
                {
                    dz = (TerrainHeights[idx] - 20f) * IsometricHeightScale;
                }
            }
        }

        return new Point(Math.Round(cellX), Math.Round(cellY));
    }

    private CellPointerEventArgs ToCellArgs(Point point, PointerEventArgs e)
    {
        var cell = PointToCell(point);
        var pointerProps = e.GetCurrentPoint(this).Properties;
        return new CellPointerEventArgs(
            (int)cell.X,
            (int)cell.Y,
            pointerProps.IsLeftButtonPressed,
            pointerProps.IsMiddleButtonPressed,
            pointerProps.IsRightButtonPressed,
            e.KeyModifiers.HasFlag(KeyModifiers.Shift));
    }

    private bool TryHandleBrushKey(KeyEventArgs e)
    {
        if (e.Key is Key.OemOpenBrackets or Key.Oem4)
        {
            BrushRadiusDecreased?.Invoke();
            return true;
        }

        if (e.Key is Key.OemCloseBrackets or Key.Oem6)
        {
            BrushRadiusIncreased?.Invoke();
            return true;
        }

        return false;
    }

    private bool TryHandleCameraKey(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Q:
                CameraYaw = ((CameraYaw - 15.0) + 360.0) % 360.0;
                InvalidateVisual();
                return true;
            case Key.E:
                CameraYaw = (CameraYaw + 15.0) % 360.0;
                InvalidateVisual();
                return true;
            case Key.R or Key.PageUp:
                CameraPitch = Math.Clamp(CameraPitch + 5.0, 15.0, 90.0);
                InvalidateVisual();
                return true;
            case Key.F or Key.PageDown:
                CameraPitch = Math.Clamp(CameraPitch - 5.0, 15.0, 90.0);
                InvalidateVisual();
                return true;
            case Key.Home:
                CameraPitch = 45.0;
                CameraYaw = 45.0;
                InvalidateVisual();
                return true;
            default:
                return false;
        }
    }

    private bool TryHandlePanKey(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Left or Key.A:
                _panOffset = new Point(_panOffset.X + 30, _panOffset.Y);
                InvalidateVisual();
                return true;
            case Key.Right or Key.D:
                _panOffset = new Point(_panOffset.X - 30, _panOffset.Y);
                InvalidateVisual();
                return true;
            case Key.Up or Key.W:
                _panOffset = new Point(_panOffset.X, _panOffset.Y + 30);
                InvalidateVisual();
                return true;
            case Key.Down or Key.S:
                _panOffset = new Point(_panOffset.X, _panOffset.Y - 30);
                InvalidateVisual();
                return true;
            default:
                return false;
        }
    }
}
