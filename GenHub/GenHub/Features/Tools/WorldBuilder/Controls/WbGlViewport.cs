// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using GenHub.Core.Models.Tools.WorldBuilder;
using GenHub.Features.Tools.WorldBuilder.Rendering;
using Silk.NET.OpenGL;
using System;
using System.Numerics;
using System.Threading;

namespace GenHub.Features.Tools.WorldBuilder.Controls;

/// <summary>
/// GPU viewport for the WorldBuilder 3D view. Hosts the shared Avalonia GL
/// context, drives <see cref="Rendering.WbRenderer"/>, and translates pointer
/// input into orbit/pan/zoom plus cell events through
/// <see cref="Rendering.WbPicking"/>. When no GL context is available the
/// control reports <see cref="GlAvailable"/> false and the 2D fallback canvas
/// takes over with a warning toast.
/// </summary>
public sealed class WbGlViewport : OpenGlControlBase, IDisposable
{
    /// <summary>
    /// Temporary default camera pitch in degrees until GameData.ini framing
    /// tunables are wired from the INI database.
    /// </summary>
    public const float DefaultCameraPitchDegrees = 37.5f;

    /// <summary>
    /// Default eye height above ground in feet (engine m_maxCameraHeight default).
    /// </summary>
    public const float DefaultCameraHeightFeet = 300.0f;

    /// <summary>
    /// Raw wheel delta of one detent notch, matching the pointer wheel handler.
    /// </summary>
    public const float WheelNotchDelta = 120.0f;

    /// <summary>
    /// Defines the <see cref="Map"/> property.
    /// </summary>
    public static readonly StyledProperty<WorldBuilderMap?> MapProperty =
        AvaloniaProperty.Register<WbGlViewport, WorldBuilderMap?>(nameof(Map));

    /// <summary>
    /// Defines the <see cref="RenderOptions"/> property.
    /// </summary>
    public static readonly StyledProperty<MapCanvasRenderOptions> RenderOptionsProperty =
        AvaloniaProperty.Register<WbGlViewport, MapCanvasRenderOptions>(nameof(RenderOptions), new MapCanvasRenderOptions());

    private readonly WbCamera _camera = new();
    private WbRenderer? _renderer;
    private bool _glAvailable = true;
    private bool _glReported;
    private bool _disposed;
    private Point? _panStart;
    private Point? _orbitStart;
    private WbTerrainRenderData? _pendingTerrain;
    private long _pendingTerrainVersion;
    private WbModelRenderData? _pendingModels;
    private long _pendingModelsVersion;
    private WbWaterData? _pendingWater;
    private long _pendingWaterVersion;
    private WbOverlayLines? _pendingLines;
    private long _pendingLinesVersion;

    /// <summary>
    /// Finalizes an instance of the <see cref="WbGlViewport"/> class.
    /// </summary>
    ~WbGlViewport()
    {
        Dispose(false);
    }

    /// <summary>
    /// Raised when GL availability changes.
    /// </summary>
    public event Action<bool>? GlAvailabilityChanged;

    /// <summary>
    /// Raised when a pointer press maps onto the map.
    /// </summary>
    public event Action<CellPointerEventArgs>? CellPressed;

    /// <summary>
    /// Raised when the pointer moves over the map.
    /// </summary>
    public event Action<CellPointerEventArgs>? CellMoved;

    /// <summary>
    /// Raised when the pointer is released over the map.
    /// </summary>
    public event Action<CellPointerEventArgs>? CellReleased;

    /// <summary>
    /// Raised when orbiting, panning, or zooming changes the camera.
    /// </summary>
    public event Action? CameraChanged;

    /// <summary>
    /// Gets or sets the map document.
    /// </summary>
    public WorldBuilderMap? Map
    {
        get => GetValue(MapProperty);
        set => SetValue(MapProperty, value);
    }

    /// <summary>
    /// Gets or sets the render options.
    /// </summary>
    public MapCanvasRenderOptions RenderOptions
    {
        get => GetValue(RenderOptionsProperty);
        set => SetValue(RenderOptionsProperty, value);
    }

    /// <summary>
    /// Gets the orbit camera.
    /// </summary>
    public WbCamera Camera => _camera;

    /// <summary>
    /// Gets a value indicating whether a GL context is available for rendering.
    /// </summary>
    public bool GlAvailable => _glAvailable;

    /// <summary>
    /// Gets the GL init failure detail when <see cref="GlAvailable"/> is false.
    /// </summary>
    public string? GlInitError { get; private set; }

    /// <summary>
    /// Fits the whole map into the viewport.
    /// </summary>
    public void ZoomToFit()
    {
        var map = Map;
        if (map == null || map.Terrain.Width <= 0 || map.Terrain.Height <= 0)
        {
            return;
        }

        _camera.CenterCells = new Vector2(map.Terrain.Width / 2.0f, map.Terrain.Height / 2.0f);
        _camera.BorderSize = map.Terrain.BorderSize;
        _camera.TerrainGroundZ = WbPicking.SampleHeight(map.Terrain, _camera.CenterCells.X, _camera.CenterCells.Y);
        var diagonal = MathF.Sqrt((map.Terrain.Width * map.Terrain.Width) + (map.Terrain.Height * map.Terrain.Height)) * 10.0f;
        var distance = (diagonal / 2.0f) / MathF.Tan((_camera.FieldOfViewDegrees * MathF.PI / 180.0f) / 2.0f);
        var direction = _camera.Offset.LengthSquared() > float.Epsilon ? Vector3.Normalize(_camera.Offset) : new Vector3(0.0f, -0.7f, 0.7f);
        _camera.Offset = direction * Math.Max(WbCamera.MinLookDistance, distance * 1.1f);
        _camera.WheelOffset = 0.0f;
        RequestNextFrameRendering();
    }

    /// <summary>
    /// Centers the viewport on a specific cell.
    /// </summary>
    /// <param name="cellX">The cell X coordinate.</param>
    /// <param name="cellY">The cell Y coordinate.</param>
    public void CenterOnCell(int cellX, int cellY)
    {
        _camera.CenterCells = new Vector2(cellX, cellY);
        RequestNextFrameRendering();
    }

    /// <summary>
    /// Dollies the camera one wheel notch: positive zooms in, negative zooms out.
    /// </summary>
    /// <param name="direction">The zoom direction.</param>
    public void ZoomStep(int direction)
    {
        _camera.Dolly(direction >= 0 ? WheelNotchDelta : -WheelNotchDelta);
        RequestNextFrameRendering();
        CameraChanged?.Invoke();
    }

    /// <summary>
    /// Applies the shared pitch/yaw orientation, preserving the current eye
    /// distance. VM yaw 45 maps to the zero orbit angle.
    /// </summary>
    /// <param name="pitchDegrees">The camera pitch in degrees.</param>
    /// <param name="yawDegrees">The camera yaw in degrees.</param>
    public void SetOrientation(double pitchDegrees, double yawDegrees)
    {
        var length = _camera.Offset.Length();
        if (length <= float.Epsilon)
        {
            length = DefaultCameraHeightFeet * 2.0f;
        }

        var pitch = Math.Clamp((float)pitchDegrees, 15.0f, 90.0f) * MathF.PI / 180.0f;
        _camera.Offset = new Vector3(0.0f, -length * MathF.Cos(pitch), length * MathF.Sin(pitch));
        _camera.YawRawRadians = _camera.YawRadians = (float)((45.0 - yawDegrees) * Math.PI / 180.0);
        RequestNextFrameRendering();
    }

    /// <summary>
    /// Reads the shared pitch/yaw orientation back from the camera.
    /// </summary>
    /// <returns>The yaw and pitch in degrees.</returns>
    public (double YawDegrees, double PitchDegrees) GetOrientation()
    {
        var yaw = 45.0 - (_camera.YawRadians * 180.0 / Math.PI);
        yaw = ((yaw % 360.0) + 360.0) % 360.0;
        var horizontal = new Vector2(_camera.Offset.X, _camera.Offset.Y).Length();
        var pitch = Math.Atan2(_camera.Offset.Z, Math.Max(0.001f, horizontal)) * 180.0 / Math.PI;
        return (yaw, pitch);
    }

    /// <summary>
    /// Resets the camera for a freshly loaded map.
    /// </summary>
    /// <param name="map">The map document.</param>
    /// <param name="pitchDegrees">The camera pitch in degrees.</param>
    /// <param name="yawDegrees">The camera yaw in degrees.</param>
    public void ResetView(WorldBuilderMap map, double pitchDegrees, double yawDegrees)
    {
        ArgumentNullException.ThrowIfNull(map);
        _camera.BorderSize = map.Terrain.BorderSize;
        _camera.CenterCells = new Vector2(map.Terrain.Width / 2.0f, map.Terrain.Height / 2.0f);
        _camera.TerrainGroundZ = WbPicking.SampleHeight(map.Terrain, _camera.CenterCells.X, _camera.CenterCells.Y);
        _camera.GroundLevel = _camera.TerrainGroundZ;
        _camera.Reset(_camera.CenterCells, _camera.GroundLevel, DefaultCameraHeightFeet, DefaultCameraPitchDegrees, 0.0f);
        SetOrientation(pitchDegrees, yawDegrees);
        CameraChanged?.Invoke();
    }

    /// <summary>
    /// Stages terrain render data for upload on the next frame.
    /// </summary>
    /// <param name="data">The render data, or null to clear the terrain.</param>
    public void SetTerrainData(WbTerrainRenderData? data)
    {
        _pendingTerrain = data;
        Volatile.Write(ref _pendingTerrainVersion, _pendingTerrainVersion + 1);
        RequestNextFrameRendering();
    }

    /// <summary>
    /// Stages model render data for upload on the next frame.
    /// </summary>
    /// <param name="data">The render data, or null to clear the models.</param>
    public void SetModelsData(WbModelRenderData? data)
    {
        _pendingModels = data;
        Volatile.Write(ref _pendingModelsVersion, _pendingModelsVersion + 1);
        RequestNextFrameRendering();
    }

    /// <summary>
    /// Stages water render data for upload on the next frame.
    /// </summary>
    /// <param name="data">The render data, or null to clear the water.</param>
    public void SetWaterData(WbWaterData? data)
    {
        _pendingWater = data;
        Volatile.Write(ref _pendingWaterVersion, _pendingWaterVersion + 1);
        RequestNextFrameRendering();
    }

    /// <summary>
    /// Stages overlay lines for upload on the next frame.
    /// </summary>
    /// <param name="data">The render data, or null to clear the lines.</param>
    public void SetOverlayLines(WbOverlayLines? data)
    {
        _pendingLines = data;
        Volatile.Write(ref _pendingLinesVersion, _pendingLinesVersion + 1);
        RequestNextFrameRendering();
    }

    /// <summary>
    /// Converts a control point to a map cell through the heightfield.
    /// </summary>
    /// <param name="point">The control point.</param>
    /// <returns>The map cell.</returns>
    public Point PointToCell(Point point)
    {
        var map = Map;
        if (map == null || map.Terrain.Width <= 0 || map.Terrain.Height <= 0 || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return new Point(0, 0);
        }

        var (origin, direction) = WbPicking.ScreenPointToRay(
            new Vector2((float)point.X, (float)point.Y),
            new Vector2((float)Bounds.Width, (float)Bounds.Height),
            _camera.ViewMatrix(),
            _camera.ProjectionMatrix((float)(Bounds.Width / Bounds.Height)));
        var (_, far) = _camera.ClipPlanes();
        var hit = WbPicking.IntersectTerrain(origin, direction, map.Terrain, far)
            ?? WbPicking.IntersectGroundPlane(origin, direction, _camera.TerrainGroundZ);
        if (hit == null)
        {
            return new Point(0, 0);
        }

        var cell = WbPicking.WorldToCell(hit.Value, map.Terrain.BorderSize, map.Terrain.Width, map.Terrain.Height);
        return new Point(cell.X, cell.Y);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == MapProperty || change.Property == RenderOptionsProperty)
        {
            RequestNextFrameRendering();
        }
    }

    /// <inheritdoc />
    protected override void OnOpenGlInit(GlInterface gl)
    {
        base.OnOpenGlInit(gl);
        try
        {
            var bindings = GL.GetApi(gl.GetProcAddress);
            var created = WbRenderer.Create(bindings);
            if (created.Success && created.Data != null)
            {
                _renderer = created.Data;
                GlInitError = null;
                ReportGlAvailability(true);
            }
            else
            {
                GlInitError = created.FirstError ?? "Renderer creation failed.";
                System.Diagnostics.Debug.WriteLine($"WorldBuilder GL init failed: {GlInitError}");
                ReportGlAvailability(false);
            }
        }
        catch (InvalidOperationException ex)
        {
            GlInitError = ex.Message;
            System.Diagnostics.Debug.WriteLine($"WorldBuilder GL init threw: {ex}");
            ReportGlAvailability(false);
        }
        catch (NotSupportedException ex)
        {
            GlInitError = ex.Message;
            System.Diagnostics.Debug.WriteLine($"WorldBuilder GL init threw: {ex}");
            ReportGlAvailability(false);
        }
        catch (ArgumentException ex)
        {
            GlInitError = ex.Message;
            System.Diagnostics.Debug.WriteLine($"WorldBuilder GL init threw: {ex}");
            ReportGlAvailability(false);
        }
    }

    /// <inheritdoc />
    protected override void OnOpenGlRender(GlInterface gl, int fb)
    {
        if (_renderer == null || Map == null)
        {
            return;
        }

        var scaling = VisualRoot is TopLevel topLevel ? topLevel.RenderScaling : 1.0;
        var pixelWidth = (int)Math.Round(Bounds.Width * scaling);
        var pixelHeight = (int)Math.Round(Bounds.Height * scaling);
        _renderer.SetViewport(pixelWidth, pixelHeight);
        var terrain = _pendingTerrain;
        var terrainVersion = Volatile.Read(ref _pendingTerrainVersion);
        var models = _pendingModels;
        var modelsVersion = Volatile.Read(ref _pendingModelsVersion);
        var water = _pendingWater;
        var waterVersion = Volatile.Read(ref _pendingWaterVersion);
        var lines = _pendingLines;
        var linesVersion = Volatile.Read(ref _pendingLinesVersion);
        _renderer.SetTerrainData(terrain, terrainVersion);
        _renderer.SetModels(models, modelsVersion);
        _renderer.SetWater(water, waterVersion);
        _renderer.SetOverlayLines(lines, linesVersion);
        var size = new Vector2((float)Bounds.Width, (float)Bounds.Height);
        _renderer.Render(_camera, size, Map.Terrain, RenderOptions);
    }

    /// <inheritdoc />
    protected override void OnOpenGlDeinit(GlInterface gl)
    {
        base.OnOpenGlDeinit(gl);
        _renderer?.Dispose();
        _renderer = null;
    }

    /// <inheritdoc />
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        var props = e.GetCurrentPoint(this).Properties;
        CellPressed?.Invoke(ToCellArgs(e));
        if (props.IsRightButtonPressed || props.IsMiddleButtonPressed)
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Alt) || props.IsMiddleButtonPressed)
            {
                _orbitStart = e.GetPosition(this);
            }
            else
            {
                _panStart = e.GetPosition(this);
            }

            e.Pointer.Capture(this);
        }
        else if (props.IsLeftButtonPressed)
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
        if (_orbitStart != null)
        {
            _camera.Orbit((float)(point.X - _orbitStart.Value.X) * 0.005f);
            _orbitStart = point;
            RequestNextFrameRendering();
            CameraChanged?.Invoke();
            return;
        }

        if (_panStart != null)
        {
            PanByPixels(point - _panStart.Value);
            _panStart = point;
            RequestNextFrameRendering();
            CameraChanged?.Invoke();
            return;
        }

        CellMoved?.Invoke(ToCellArgs(e));
    }

    /// <inheritdoc />
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _orbitStart = null;
        _panStart = null;
        e.Pointer.Capture(null);
        CellReleased?.Invoke(ToCellArgs(e));
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

        _camera.Dolly((float)e.Delta.Y * WheelNotchDelta);
        RequestNextFrameRendering();
        CameraChanged?.Invoke();
        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 10 : 2;
        switch (e.Key)
        {
            case Key.Left:
                _camera.Pan(new Vector2(-step, 0));
                break;
            case Key.Right:
                _camera.Pan(new Vector2(step, 0));
                break;
            case Key.Up:
                _camera.Pan(new Vector2(0, -step));
                break;
            case Key.Down:
                _camera.Pan(new Vector2(0, step));
                break;
            default:
                // Unhandled keys leave the camera alone and skip the re-render.
                return;
        }

        RequestNextFrameRendering();
        CameraChanged?.Invoke();
        e.Handled = true;
    }

    private void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (disposing)
        {
            _renderer?.Dispose();
            _renderer = null;
        }
    }

    private void ReportGlAvailability(bool available)
    {
        if (_glReported && _glAvailable == available)
        {
            return;
        }

        _glAvailable = available;
        _glReported = true;
        GlAvailabilityChanged?.Invoke(available);
    }

    private void PanByPixels(Avalonia.Vector deltaPixels)
    {
        var (eye, target) = _camera.ComputeEyeTarget();
        var distance = (target - eye).Length();
        var worldPerPixel = (2.0f * distance * MathF.Tan((_camera.FieldOfViewDegrees * MathF.PI / 180.0f) / 2.0f))
            / (float)Math.Max(1.0, Bounds.Height);
        var forward = target - eye;
        forward.Z = 0.0f;
        if (forward.LengthSquared() < float.Epsilon)
        {
            forward = -Vector3.UnitY;
        }

        forward = Vector3.Normalize(forward);
        var right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitZ));
        var world = (-right * (float)deltaPixels.X) + (forward * (float)deltaPixels.Y);
        _camera.Pan(new Vector2(world.X, world.Y) * worldPerPixel / 10.0f);
    }

    private CellPointerEventArgs ToCellArgs(PointerEventArgs e)
    {
        var cell = PointToCell(e.GetPosition(this));
        var props = e.GetCurrentPoint(this).Properties;
        return new CellPointerEventArgs(
            (int)cell.X,
            (int)cell.Y,
            props.IsLeftButtonPressed,
            props.IsMiddleButtonPressed,
            props.IsRightButtonPressed,
            e.KeyModifiers.HasFlag(KeyModifiers.Shift));
    }
}
