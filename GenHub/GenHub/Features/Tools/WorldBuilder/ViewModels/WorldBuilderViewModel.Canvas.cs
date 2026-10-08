using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Constants;
using GenHub.Core.Models.Tools.WorldBuilder;
using GenHub.Core.Services.Tools.WorldBuilder;
using GenHub.Features.Tools.WorldBuilder.Controls;
using GenHub.Features.Tools.WorldBuilder.Rendering;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.WorldBuilder.ViewModels;

/// <summary>
/// Canvas viewport, brush manipulation, and layer rendering for WorldBuilder.
/// </summary>
public sealed partial class WorldBuilderViewModel
{
    private readonly MapUndoService _undoService = new();
    private bool _applyingViewportCamera;
    private bool _isDrawingStroke;
    private (int X, int Y)? _twoPointStart;
    private int _renderPending;
    private int _isRendering;
    private Task? _renderTask;

    [ObservableProperty]
    private WriteableBitmap? canvasBitmap;

    [ObservableProperty]
    private MapCanvasTool selectedCanvasTool = MapCanvasTool.Pointer;

    [ObservableProperty]
    private MapCanvasViewMode viewMode = MapCanvasViewMode.Isometric3D;

    [ObservableProperty]
    private bool isWireframe;

    [ObservableProperty]
    private float sunPitch = 45f;

    [ObservableProperty]
    private float sunYaw = 45f;

    [ObservableProperty]
    private double cameraPitch = 45.0;

    [ObservableProperty]
    private double cameraYaw = 45.0;

    [ObservableProperty]
    private bool showAllObjectLabels;

    [ObservableProperty]
    private double brushRadius = 3.0;

    [ObservableProperty]
    private int brushIntensity = 5;

    [ObservableProperty]
    private int brushHeight = 20;

    [ObservableProperty]
    private double zoom = 1.0;

    [ObservableProperty]
    private bool showTerrainLayer = true;

    [ObservableProperty]
    private bool showWaterLayer = true;

    [ObservableProperty]
    private bool showCliffsLayer = true;

    [ObservableProperty]
    private bool showBlendLayer = true;

    [ObservableProperty]
    private bool showGridLayer;

    [ObservableProperty]
    private bool showObjectsLayer = true;

    [ObservableProperty]
    private bool showWaypointsLayer = true;

    [ObservableProperty]
    private bool showTriggersLayer = true;

    [ObservableProperty]
    private bool showBoundaryLayer = true;

    [ObservableProperty]
    private bool showRoadsLayer = true;

    [ObservableProperty]
    private bool showBridgesLayer = true;

    [ObservableProperty]
    private (Point Start, Point End)? activeLinePreview;

    [ObservableProperty]
    private IList<byte>? mapTerrainHeights;

    [ObservableProperty]
    private bool renderer3DAvailable = true;

    [ObservableProperty]
    private bool is3DViewportVisible;

    [ObservableProperty]
    private MapCanvasRenderOptions renderOptions = new();

    [ObservableProperty]
    private bool canUndo;

    [ObservableProperty]
    private bool canRedo;

    /// <summary>
    /// Selects an active editing tool.
    /// </summary>
    /// <param name="tool">The tool to select.</param>
    [RelayCommand]
    public void SelectTool(MapCanvasTool tool)
    {
        SelectedCanvasTool = tool;
    }

    /// <summary>
    /// Toggles between 3D Isometric and 2D Top-Down views.
    /// </summary>
    [RelayCommand]
    public void Toggle3DView()
    {
        ViewMode = ViewMode == MapCanvasViewMode.Isometric3D ? MapCanvasViewMode.TopDown2D : MapCanvasViewMode.Isometric3D;
        RefreshCanvasBitmap();
    }

    /// <summary>
    /// Toggles terrain wireframe visualization.
    /// </summary>
    [RelayCommand]
    public void ToggleWireframe()
    {
        IsWireframe = !IsWireframe;
        RefreshCanvasBitmap();
    }

    /// <summary>
    /// Rotates the 3D camera left.
    /// </summary>
    [RelayCommand]
    public void RotateCameraLeft()
    {
        CameraYaw = ((CameraYaw - 15.0) + 360.0) % 360.0;
    }

    /// <summary>
    /// Rotates the 3D camera right.
    /// </summary>
    [RelayCommand]
    public void RotateCameraRight()
    {
        CameraYaw = (CameraYaw + 15.0) % 360.0;
    }

    /// <summary>
    /// Tilts the 3D camera up.
    /// </summary>
    [RelayCommand]
    public void TiltCameraUp()
    {
        CameraPitch = Math.Clamp(CameraPitch + 5.0, 15.0, 90.0);
    }

    /// <summary>
    /// Tilts the 3D camera down.
    /// </summary>
    [RelayCommand]
    public void TiltCameraDown()
    {
        CameraPitch = Math.Clamp(CameraPitch - 5.0, 15.0, 90.0);
    }

    /// <summary>
    /// Resets 3D camera to default isometric perspective.
    /// </summary>
    [RelayCommand]
    public void ResetCamera()
    {
        CameraPitch = 45.0;
        CameraYaw = 45.0;
    }

    /// <summary>
    /// Increases brush radius.
    /// </summary>
    [RelayCommand]
    public void IncreaseBrushRadius()
    {
        BrushRadius = Math.Min(32.0, BrushRadius + 1.0);
    }

    /// <summary>
    /// Decreases brush radius.
    /// </summary>
    [RelayCommand]
    public void DecreaseBrushRadius()
    {
        BrushRadius = Math.Max(1.0, BrushRadius - 1.0);
    }

    /// <summary>
    /// Reverts the most recent map edit.
    /// </summary>
    [RelayCommand]
    public void Undo()
    {
        if (_map == null || !_undoService.CanUndo)
        {
            return;
        }

        if (_undoService.Undo(_map))
        {
            _map.Terrain.CliffState = MapCliffComputer.ComputeCliffState(_map.Terrain.Heights, _map.Terrain.Width, _map.Terrain.Height, WorldBuilderConstants.Terrain.CliffToolSlopeLimitWorldZ);
            MapWidth = _map.Terrain.Width;
            MapHeight = _map.Terrain.Height;
            MapTerrainHeights = _map.Terrain.Heights;
            IsDirty = _map.IsDirty;
            RefreshCanvasBitmap();
            SyncAllCollections();
            UpdateUndoState();
        }
    }

    /// <summary>
    /// Reapplies the most recently undone edit.
    /// </summary>
    [RelayCommand]
    public void Redo()
    {
        if (_map == null || !_undoService.CanRedo)
        {
            return;
        }

        if (_undoService.Redo(_map))
        {
            _map.Terrain.CliffState = MapCliffComputer.ComputeCliffState(_map.Terrain.Heights, _map.Terrain.Width, _map.Terrain.Height, WorldBuilderConstants.Terrain.CliffToolSlopeLimitWorldZ);
            MapWidth = _map.Terrain.Width;
            MapHeight = _map.Terrain.Height;
            MapTerrainHeights = _map.Terrain.Heights;
            IsDirty = _map.IsDirty;
            RefreshCanvasBitmap();
            SyncAllCollections();
            UpdateUndoState();
        }
    }

    /// <summary>
    /// Zooms in on the canvas.
    /// </summary>
    [RelayCommand]
    public void ZoomIn()
    {
        if (Is3DViewportVisible)
        {
            RequestZoomStep?.Invoke(1);
            return;
        }

        Zoom = Math.Min(5.0, Zoom * 1.25);
    }

    /// <summary>
    /// Zooms out from the canvas.
    /// </summary>
    [RelayCommand]
    public void ZoomOut()
    {
        if (Is3DViewportVisible)
        {
            RequestZoomStep?.Invoke(-1);
            return;
        }

        Zoom = Math.Max(0.1, Zoom / 1.25);
    }

    /// <summary>
    /// Zooms and centers canvas to fit map bounds.
    /// </summary>
    [RelayCommand]
    public void ZoomFit()
    {
        Zoom = 1.0;
        RequestZoomToFit?.Invoke();
    }

    /// <summary>
    /// Handles pointer pressed events on the map canvas.
    /// </summary>
    /// <param name="e">Pointer arguments.</param>
    public void HandleCanvasCellPressed(CellPointerEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (_map == null || !IsInsideMap(e.CellX, e.CellY))
        {
            return;
        }

        if (e.IsRightButton)
        {
            CancelTwoPointStroke();
            return;
        }

        if (e.IsLeftButton)
        {
            HandleLeftPress(e);
        }
    }

    /// <summary>
    /// Handles pointer moved events on the map canvas.
    /// </summary>
    /// <param name="e">Pointer arguments.</param>
    public void HandleCanvasCellMoved(CellPointerEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (_map == null || !IsInsideMap(e.CellX, e.CellY))
        {
            return;
        }

        if (_twoPointStart != null)
        {
            ActiveLinePreview = (new Point(_twoPointStart.Value.X, _twoPointStart.Value.Y), new Point(e.CellX, e.CellY));
            return;
        }

        if (!_isDrawingStroke)
        {
            return;
        }

        if (e.IsLeftButton && IsContinuousTool(SelectedCanvasTool))
        {
            ApplyContinuousTool(e.CellX, e.CellY);
        }
    }

    /// <summary>
    /// Handles the GL viewport reporting no usable context: falls back to the
    /// 2D canvas with a one-time warning toast.
    /// </summary>
    /// <param name="detail">The GL init failure detail, when known.</param>
    public void OnRendererFallback(string? detail = null)
    {
        if (!Renderer3DAvailable)
        {
            return;
        }

        Renderer3DAvailable = false;
        if (!string.IsNullOrWhiteSpace(detail))
        {
            logger.LogWarning("WorldBuilder 3D renderer unavailable, falling back to 2D: {Detail}", detail);
        }
        else
        {
            logger.LogWarning("WorldBuilder 3D renderer unavailable, falling back to 2D");
        }

        notificationService.ShowWarning(
            localizationService.GetString("Tools.WorldBuilder.Renderer.Fallback.Title"),
            localizationService.GetString("Tools.WorldBuilder.Renderer.Fallback.Message"),
            NotificationDurations.Medium);
    }

    /// <summary>
    /// Applies orbit/pan/zoom performed in the 3D viewport back to the shared
    /// camera properties. Canvas re-rendering is suppressed because the
    /// viewport already repainted itself; the 2D bitmap refreshes on view-mode
    /// switches instead.
    /// </summary>
    /// <param name="yawDegrees">The camera yaw in degrees.</param>
    /// <param name="pitchDegrees">The camera pitch in degrees.</param>
    [SuppressMessage("Minor Code Smell", "S2325", Justification = "Assigns instance observable camera properties; cannot be static.")]
    public void SetCameraFromViewport(double yawDegrees, double pitchDegrees)
    {
        _applyingViewportCamera = true;
        try
        {
            CameraYaw = yawDegrees;
            CameraPitch = pitchDegrees;
        }
        finally
        {
            _applyingViewportCamera = false;
        }
    }

    /// <summary>
    /// Handles pointer released events on the map canvas.
    /// </summary>
    /// <param name="e">Pointer arguments.</param>
    public void HandleCanvasCellReleased(CellPointerEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (_isDrawingStroke && _map != null)
        {
            _isDrawingStroke = false;
            Summary = mapService.Summarize(_map);
            UpdateUndoState();
        }
    }

    /// <summary>
    /// Rerenders the map canvas bitmap asynchronously with frame coalescing according to the current render options.
    /// </summary>
    public void RefreshCanvasBitmap()
    {
        if (_map == null || _map.Terrain.Width <= 0 || _map.Terrain.Height <= 0)
        {
            if (Avalonia.Application.Current == null || Dispatcher.UIThread.CheckAccess())
            {
                CanvasBitmap?.Dispose();
                CanvasBitmap = null;
            }
            else
            {
                Dispatcher.UIThread.Post(() =>
                {
                    CanvasBitmap?.Dispose();
                    CanvasBitmap = null;
                });
            }

            return;
        }

        Interlocked.Exchange(ref _renderPending, 1);
        if (Interlocked.CompareExchange(ref _isRendering, 1, 0) != 0)
        {
            return;
        }

        _renderTask = ExecuteRenderPassAsync();
    }

    /// <summary>
    /// Test hook: completes when no render pass is running or queued, so test
    /// teardown can join fire-and-forget render work before the headless
    /// dispatcher is reset for the next test.
    /// </summary>
    /// <returns>A task that completes when rendering is idle.</returns>
    internal async Task RenderIdleAsync()
    {
        while (true)
        {
            var task = _renderTask;
            if (task != null)
            {
                try
                {
                    await task.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // A pass cancelled by dispatcher shutdown still releases its gate.
                }
            }

            if (_isRendering == 0 && _renderPending == 0 && _renderTask == task)
            {
                return;
            }
        }
    }

    private static bool IsContinuousTool(MapCanvasTool tool)
    {
        return tool is MapCanvasTool.MoundUp
            or MapCanvasTool.MoundDown
            or MapCanvasTool.Smooth
            or MapCanvasTool.Plateau
            or MapCanvasTool.TilePaint;
    }

    private static bool IsTwoPointTool(MapCanvasTool tool)
    {
        return tool is MapCanvasTool.Road
            or MapCanvasTool.Bridge
            or MapCanvasTool.Fence
            or MapCanvasTool.Ramp
            or MapCanvasTool.Border
            or MapCanvasTool.Ruler
            or MapCanvasTool.WaterArea;
    }

    private async Task ExecuteRenderPassAsync()
    {
        var map = _map;
        if (map == null || _disposed)
        {
            Interlocked.Exchange(ref _isRendering, 0);
            return;
        }

        Interlocked.Exchange(ref _renderPending, 0);
        var (options, renderBitmap) = await PublishRenderOptionsAsync().ConfigureAwait(false);
        try
        {
            // The 2D bitmap is hidden behind the GPU viewport in 3D mode, so skip
            // the full CPU pass and only refresh the 3D scene. The bitmap is
            // re-rendered on the next switch back to the 2D canvas.
            if (renderBitmap)
            {
                await RenderBitmapAsync(map, options).ConfigureAwait(false);
            }

            RaiseRefreshView();
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IndexOutOfRangeException)
        {
            logger.LogWarning(ex, "Failed to complete background map render pass");
        }
        finally
        {
            // Release the gate before re-checking pending: a request arriving
            // between the release and the check either observes the free gate
            // and starts its own pass, or is observed here and re-queued.
            Interlocked.Exchange(ref _isRendering, 0);
            if (!_disposed && _map != null && Interlocked.Exchange(ref _renderPending, 0) == 1)
            {
                RefreshCanvasBitmap();
            }
        }
    }

    private async Task<(MapCanvasRenderOptions Options, bool RenderBitmap)> PublishRenderOptionsAsync()
    {
        if (_disposed)
        {
            return (BuildRenderOptions(), !Is3DViewportVisible);
        }

        // BuildRenderOptions reads bound properties and RenderOptions raises
        // PropertyChanged into Avalonia controls, so both must run on the UI
        // thread. Re-queued passes run on the thread pool after ConfigureAwait(false).
        if (Avalonia.Application.Current == null || Dispatcher.UIThread.CheckAccess())
        {
            var direct = BuildRenderOptions();
            RenderOptions = direct;
            return (direct, !Is3DViewportVisible);
        }

        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var built = BuildRenderOptions();
            RenderOptions = built;
            return (built, !Is3DViewportVisible);
        });
    }

    private async Task RenderBitmapAsync(WorldBuilderMap map, MapCanvasRenderOptions options)
    {
        var (pixels, width, height) = await Task.Run(() =>
        {
            var (p, w, h) = WbFallbackPreview.RenderTopDown(map, options);
            return (p, w, h);
        }).ConfigureAwait(false);

        if (_disposed)
        {
            return;
        }

        if (Avalonia.Application.Current == null || Dispatcher.UIThread.CheckAccess())
        {
            ApplyPixels(map, pixels, width, height);
        }
        else
        {
            await Dispatcher.UIThread.InvokeAsync(() => ApplyPixels(map, pixels, width, height));
        }
    }

    private void ApplyPixels(WorldBuilderMap map, int[] pixels, int width, int height)
    {
        if (_disposed || _map == null || _map != map)
        {
            return;
        }

        if (CanvasBitmap == null || CanvasBitmap.PixelSize.Width != width || CanvasBitmap.PixelSize.Height != height)
        {
            CanvasBitmap?.Dispose();
            CanvasBitmap = new WriteableBitmap(
                new PixelSize(width, height),
                new Vector(96, 96),
                PixelFormats.Bgra8888);
        }

        using (var frame = CanvasBitmap.Lock())
        {
            for (var y = 0; y < height; y++)
            {
                Marshal.Copy(pixels, y * width, frame.Address + (y * frame.RowBytes), width);
            }
        }

        OnPropertyChanged(nameof(CanvasBitmap));
    }

    private void RaiseRefreshView()
    {
        if (_disposed || RequestRefreshView == null)
        {
            return;
        }

        if (Avalonia.Application.Current == null || Dispatcher.UIThread.CheckAccess())
        {
            RequestRefreshView?.Invoke();
        }
        else
        {
            Dispatcher.UIThread.Post(() => RequestRefreshView?.Invoke());
        }
    }

    private void UpdateUndoState()
    {
        CanUndo = _undoService.CanUndo;
        CanRedo = _undoService.CanRedo;
    }

    private bool IsInsideMap(int cellX, int cellY)
    {
        return _map != null
            && cellX >= 0
            && cellY >= 0
            && cellX < _map.Terrain.Width
            && cellY < _map.Terrain.Height;
    }

    private void CancelTwoPointStroke()
    {
        if (_twoPointStart != null)
        {
            _twoPointStart = null;
            ActiveLinePreview = null;
        }
    }

    private void HandleLeftPress(CellPointerEventArgs e)
    {
        if (_map == null)
        {
            return;
        }

        if (IsTwoPointTool(SelectedCanvasTool))
        {
            HandleTwoPointPress(e);
            return;
        }

        _undoService.Checkpoint(_map);
        _isDrawingStroke = true;
        ApplyToolAtCell(e.CellX, e.CellY);
        UpdateUndoState();
    }

    private void HandleTwoPointPress(CellPointerEventArgs e)
    {
        if (_map == null)
        {
            return;
        }

        if (_twoPointStart == null)
        {
            _twoPointStart = (e.CellX, e.CellY);
            ActiveLinePreview = (new Point(e.CellX, e.CellY), new Point(e.CellX, e.CellY));
            return;
        }

        var isRuler = SelectedCanvasTool == MapCanvasTool.Ruler;
        if (!isRuler)
        {
            _undoService.Checkpoint(_map);
        }

        ApplyTwoPointTool(_twoPointStart.Value.X, _twoPointStart.Value.Y, e.CellX, e.CellY);
        _twoPointStart = null;
        ActiveLinePreview = null;

        if (!isRuler)
        {
            UpdateUndoState();
        }
    }

    private void ApplyContinuousTool(int cellX, int cellY)
    {
        if (_map == null)
        {
            return;
        }

        var radius = (int)Math.Max(1, Math.Round(BrushRadius));
        switch (SelectedCanvasTool)
        {
            case MapCanvasTool.MoundUp:
                MapTerrainTools.ApplyMound(_map, cellX, cellY, radius, BrushIntensity);
                break;

            case MapCanvasTool.MoundDown:
                MapTerrainTools.ApplyMound(_map, cellX, cellY, radius, -BrushIntensity);
                break;

            case MapCanvasTool.Smooth:
                MapTerrainTools.ApplySmooth(_map, cellX, cellY, radius);
                break;

            case MapCanvasTool.Plateau:
                MapTerrainTools.ApplyPlateau(_map, cellX, cellY, radius, BrushHeight);
                break;

            case MapCanvasTool.TilePaint:
                if (SelectedTexture != null)
                {
                    MapTerrainTools.PaintTile(_map, cellX, cellY, radius, SelectedTexture);
                }

                break;

            case MapCanvasTool.TileFloodFill:
                if (SelectedTexture != null)
                {
                    MapTerrainTools.ApplyFloodFill(_map, cellX, cellY, SelectedTexture);
                }

                break;

            default:
                // Non-continuous tools are dispatched by their own handlers.
                return;
        }

        _map.Terrain.CliffState = MapCliffComputer.ComputeCliffState(_map.Terrain.Heights, _map.Terrain.Width, _map.Terrain.Height, WorldBuilderConstants.Terrain.CliffToolSlopeLimitWorldZ);
        MapTerrainHeights = _map.Terrain.Heights;
        IsDirty = true;
        RefreshCanvasBitmap();
    }

    private (float X, float Y) SnapCell(int cellX, int cellY)
    {
        var cellSize = WorldBuilderConstants.Terrain.CellSize;
        if (_map == null)
        {
            return (cellX * cellSize, cellY * cellSize);
        }

        return MapOverlayTools.SnapRoadPoint(_map, cellX * cellSize, cellY * cellSize, cellSize / 2.0f);
    }

    private void ApplyTwoPointTool(int startX, int startY, int endX, int endY)
    {
        if (_map == null)
        {
            return;
        }

        switch (SelectedCanvasTool)
        {
            case MapCanvasTool.Road:
                ApplyRoadSpan(startX, startY, endX, endY);
                break;

            case MapCanvasTool.Bridge:
                ApplyBridgeSpan(startX, startY, endX, endY);
                break;

            case MapCanvasTool.Fence:
                ApplyFenceSpan(startX, startY, endX, endY);
                break;

            case MapCanvasTool.Ramp:
                ApplyRampSpan(startX, startY, endX, endY);
                break;

            case MapCanvasTool.Border:
                ApplyBorderSpan(startX, startY, endX, endY);
                break;

            case MapCanvasTool.WaterArea:
                ApplyWaterAreaSpan(startX, startY, endX, endY);
                break;

            case MapCanvasTool.Ruler:
                ApplyRulerSpan(startX, startY, endX, endY);
                break;

            default:
                // Continuous tools never reach the two-point dispatcher.
                break;
        }
    }

    private void ApplyRoadSpan(int startX, int startY, int endX, int endY)
    {
        if (_map == null)
        {
            return;
        }

        var roadStart = SnapCell(startX, startY);
        var roadEnd = SnapCell(endX, endY);
        MapOverlayTools.AddRoadSegment(
            _map,
            new RoadSegment(
                SelectedRoadType,
                roadStart.X,
                roadStart.Y,
                0f,
                roadEnd.X,
                roadEnd.Y,
                0f,
                IsRoadAngledCorner,
                IsRoadTightCorner));
        SyncRoadsAndBridges();
        IsDirty = true;
        RefreshCanvasBitmap();
    }

    private void ApplyBridgeSpan(int startX, int startY, int endX, int endY)
    {
        if (_map == null)
        {
            return;
        }

        var bridgeStart = SnapCell(startX, startY);
        var bridgeEnd = SnapCell(endX, endY);
        MapOverlayTools.AddBridge(
            _map,
            SelectedBridgeTemplate,
            bridgeStart.X,
            bridgeStart.Y,
            bridgeEnd.X,
            bridgeEnd.Y);
        SyncRoadsAndBridges();
        IsDirty = true;
        RefreshCanvasBitmap();
    }

    private void ApplyFenceSpan(int startX, int startY, int endX, int endY)
    {
        if (_map == null)
        {
            return;
        }

        var fenceCell = WorldBuilderConstants.Terrain.CellSize;
        MapOverlayTools.ApplyFence(
            _map,
            SelectedFenceTemplate,
            startX * fenceCell,
            startY * fenceCell,
            endX * fenceCell,
            endY * fenceCell,
            (float)Math.Max(1.0, FenceSpacing));
        IsDirty = true;
        RefreshCanvasBitmap();
    }

    private void ApplyRampSpan(int startX, int startY, int endX, int endY)
    {
        if (_map == null)
        {
            return;
        }

        var rampWidth = (int)Math.Max(1, Math.Round(BrushRadius));
        MapTerrainTools.ApplyRamp(_map, startX, startY, endX, endY, rampWidth);
        _map.Terrain.CliffState = MapCliffComputer.ComputeCliffState(_map.Terrain.Heights, _map.Terrain.Width, _map.Terrain.Height, WorldBuilderConstants.Terrain.CliffToolSlopeLimitWorldZ);
        MapTerrainHeights = _map.Terrain.Heights;
        IsDirty = true;
        RefreshCanvasBitmap();
    }

    private void ApplyBorderSpan(int startX, int startY, int endX, int endY)
    {
        if (_map == null)
        {
            return;
        }

        MapTerrainTools.SetPlayableBoundary(
            _map,
            Math.Min(startX, endX),
            Math.Min(startY, endY),
            Math.Max(startX, endX),
            Math.Max(startY, endY));
        IsDirty = true;
        RefreshCanvasBitmap();
    }

    private void ApplyWaterAreaSpan(int startX, int startY, int endX, int endY)
    {
        if (_map == null)
        {
            return;
        }

        var minX = Math.Min(startX, endX);
        var maxX = Math.Max(startX, endX);
        var minY = Math.Min(startY, endY);
        var maxY = Math.Max(startY, endY);
        var cellSize = WorldBuilderConstants.Terrain.CellSize;
        var level = WaterLevel > 0 ? WaterLevel : WorldBuilderConstants.Canvas.DefaultWaterLevel;
        var z = (int)(level * WorldBuilderConstants.Terrain.HeightScale);
        var waterPoints = new (int X, int Y, int Z)[]
        {
            ((int)(minX * cellSize), (int)(minY * cellSize), z),
            ((int)(maxX * cellSize), (int)(minY * cellSize), z),
            ((int)(maxX * cellSize), (int)(maxY * cellSize), z),
            ((int)(minX * cellSize), (int)(maxY * cellSize), z),
        };
        MapOverlayTools.AddWaterArea(_map, $"WaterArea_{_map.Triggers.Count + 1}", waterPoints, isRiver: false);
        IsDirty = true;
        RefreshCanvasBitmap();
    }

    private void ApplyRulerSpan(int startX, int startY, int endX, int endY)
    {
        var dx = (endX - startX) * WorldBuilderConstants.Terrain.CellSize;
        var dy = (endY - startY) * WorldBuilderConstants.Terrain.CellSize;
        var dist = Math.Sqrt((dx * dx) + (dy * dy));
        var title = localizationService.GetString("Tools.WorldBuilder.Ruler.Title");
        var msg = localizationService.GetString("Tools.WorldBuilder.Ruler.DistanceMessage", dist);
        notificationService.ShowInfo(title, msg);
    }

    private void ApplyToolAtCell(int cellX, int cellY)
    {
        if (_map == null)
        {
            return;
        }

        switch (SelectedCanvasTool)
        {
            case MapCanvasTool.MoundUp:
            case MapCanvasTool.MoundDown:
            case MapCanvasTool.Smooth:
            case MapCanvasTool.Plateau:
            case MapCanvasTool.TilePaint:
            case MapCanvasTool.TileFloodFill:
                ApplyContinuousTool(cellX, cellY);
                break;

            case MapCanvasTool.Eyedropper:
                ApplyEyedropperTool(cellX, cellY);
                break;

            case MapCanvasTool.Waypoint:
                ApplyWaypointTool(cellX, cellY);
                break;

            case MapCanvasTool.ObjectPlace:
                ApplyObjectPlaceTool(cellX, cellY);
                break;

            case MapCanvasTool.ObjectErase:
                EraseObjectAtCell(cellX, cellY);
                break;

            case MapCanvasTool.Pointer:
                SelectObjectAtCell(cellX, cellY);
                break;

            default:
                // Sculpt and two-point tools are dispatched by their own handlers.
                break;
        }
    }

    private void ApplyEyedropperTool(int cellX, int cellY)
    {
        if (_map == null)
        {
            return;
        }

        var cellIndex = (cellY * _map.Terrain.Width) + cellX;
        if (cellIndex >= 0 && cellIndex < _map.Terrain.Heights.Count)
        {
            BrushHeight = _map.Terrain.Heights[cellIndex];
        }

        if (cellIndex >= 0 && cellIndex < _map.Terrain.TileIndices.Count)
        {
            var tileId = _map.Terrain.TileIndices[cellIndex];
            var classIndex = MapTerrainTools.GetTextureClassFromNdx(_map.Terrain, tileId);
            if (classIndex >= 0 && classIndex < _map.Terrain.TextureClasses.Count)
            {
                SelectedTexture = _map.Terrain.TextureClasses[classIndex];
            }
        }
    }

    private void ApplyWaypointTool(int cellX, int cellY)
    {
        if (_map == null)
        {
            return;
        }

        var waypointPos = MapCoordinates.CellCenterToWorld(_map.Terrain.BorderSize, cellX, cellY);
        var wp = MapOverlayTools.AddWaypoint(_map, waypointPos.X, waypointPos.Y);
        if (SelectedWaypoint != null)
        {
            var prevId = SelectedWaypoint.Properties.GetInt(WorldBuilderConstants.DictKeys.WaypointId, -1);
            var newId = wp.Properties.GetInt(WorldBuilderConstants.DictKeys.WaypointId, -1);
            if (prevId >= 0 && newId >= 0 && prevId != newId)
            {
                MapOverlayTools.LinkWaypoints(_map, prevId, newId);
            }
        }

        SyncWaypoints();
        SelectedWaypoint = wp;
        IsDirty = true;
        RefreshCanvasBitmap();
    }

    private void ApplyObjectPlaceTool(int cellX, int cellY)
    {
        if (_map == null)
        {
            return;
        }

        var template = string.IsNullOrWhiteSpace(SelectedObjectTemplate) ? WorldBuilderConstants.Objects.DefaultTemplate : SelectedObjectTemplate;
        var objectPos = MapCoordinates.CellCenterToWorld(_map.Terrain.BorderSize, cellX, cellY);
        var obj = MapOverlayTools.PlaceObject(_map, template, objectPos.X, objectPos.Y);
        SyncObjects();
        SelectedObject = obj;
        IsDirty = true;
        RefreshCanvasBitmap();
    }

    private void EraseObjectAtCell(int cellX, int cellY)
    {
        if (_map == null)
        {
            return;
        }

        var erasePos = MapCoordinates.CellCenterToWorld(_map.Terrain.BorderSize, cellX, cellY);
        var worldX = erasePos.X;
        var worldY = erasePos.Y;
        var threshold = Math.Max(BrushRadius * WorldBuilderConstants.Terrain.CellSize, 25f);

        var target = _map.Objects
            .Where(o => Math.Abs(o.X - worldX) <= threshold && Math.Abs(o.Y - worldY) <= threshold)
            .OrderBy(o => ((o.X - worldX) * (o.X - worldX)) + ((o.Y - worldY) * (o.Y - worldY)))
            .FirstOrDefault();

        if (target != null)
        {
            MapOverlayTools.DeleteObject(_map, target);
            SyncObjects();
            SyncWaypoints();
            IsDirty = true;
            RefreshCanvasBitmap();
        }
    }

    private void SelectObjectAtCell(int cellX, int cellY)
    {
        if (_map == null)
        {
            return;
        }

        var selectPos = MapCoordinates.CellCenterToWorld(_map.Terrain.BorderSize, cellX, cellY);
        var worldX = selectPos.X;
        var worldY = selectPos.Y;
        const float threshold = 35f;

        var target = _map.Objects
            .Where(o => Math.Abs(o.X - worldX) <= threshold && Math.Abs(o.Y - worldY) <= threshold)
            .OrderBy(o => ((o.X - worldX) * (o.X - worldX)) + ((o.Y - worldY) * (o.Y - worldY)))
            .FirstOrDefault();

        if (target != null)
        {
            var isWaypoint = target.Properties.Find(WorldBuilderConstants.DictKeys.WaypointId) != null;
            if (isWaypoint)
            {
                SelectedWaypoint = target;
            }
            else
            {
                SelectedObject = target;
            }

            RefreshCanvasBitmap();
        }
    }

    private MapCanvasRenderOptions BuildRenderOptions()
    {
        var layers = MapCanvasLayers.None;
        if (ShowTerrainLayer)
        {
            layers |= MapCanvasLayers.Terrain;
        }

        if (ShowWaterLayer)
        {
            layers |= MapCanvasLayers.Water;
        }

        if (ShowCliffsLayer)
        {
            layers |= MapCanvasLayers.Cliffs;
        }

        if (ShowBlendLayer)
        {
            layers |= MapCanvasLayers.Blend;
        }

        if (ShowGridLayer)
        {
            layers |= MapCanvasLayers.Grid;
        }

        if (ShowObjectsLayer)
        {
            layers |= MapCanvasLayers.Objects;
        }

        if (ShowWaypointsLayer)
        {
            layers |= MapCanvasLayers.Waypoints;
        }

        if (ShowTriggersLayer)
        {
            layers |= MapCanvasLayers.Triggers;
        }

        if (ShowBoundaryLayer)
        {
            layers |= MapCanvasLayers.Boundary;
        }

        if (ShowRoadsLayer)
        {
            layers |= MapCanvasLayers.Roads;
        }

        if (ShowBridgesLayer)
        {
            layers |= MapCanvasLayers.Bridges;
        }

        return new MapCanvasRenderOptions
        {
            Layers = layers,
            WaterLevel = WaterLevel,
            GridStep = WorldBuilderConstants.Canvas.DefaultGridStep,
            SelectedObjectName = SelectedObject?.Name ?? SelectedWaypoint?.Name,
            SelectedTeamName = SelectedSkirmishTeam?.Properties.GetString(WorldBuilderConstants.DictKeys.TeamName, string.Empty),
            ViewMode = ViewMode,
            Wireframe = IsWireframe,
            SunPitch = SunPitch,
            SunYaw = SunYaw,
            CameraPitch = (float)CameraPitch,
            CameraYaw = (float)CameraYaw,
            ShowAllObjectLabels = ShowAllObjectLabels,
        };
    }

    partial void OnShowTerrainLayerChanged(bool value) => RefreshCanvasBitmap();

    partial void OnShowWaterLayerChanged(bool value) => RefreshCanvasBitmap();

    partial void OnShowCliffsLayerChanged(bool value) => RefreshCanvasBitmap();

    partial void OnShowBlendLayerChanged(bool value) => RefreshCanvasBitmap();

    partial void OnShowGridLayerChanged(bool value) => RefreshCanvasBitmap();

    partial void OnShowObjectsLayerChanged(bool value) => RefreshCanvasBitmap();

    partial void OnShowWaypointsLayerChanged(bool value) => RefreshCanvasBitmap();

    partial void OnShowTriggersLayerChanged(bool value) => RefreshCanvasBitmap();

    partial void OnShowBoundaryLayerChanged(bool value) => RefreshCanvasBitmap();

    partial void OnShowRoadsLayerChanged(bool value) => RefreshCanvasBitmap();

    partial void OnShowBridgesLayerChanged(bool value) => RefreshCanvasBitmap();

    partial void OnViewModeChanged(MapCanvasViewMode value)
    {
        UpdateViewportVisibility();
        RefreshCanvasBitmap();
    }

    partial void OnRenderer3DAvailableChanged(bool value)
    {
        UpdateViewportVisibility();
        RefreshCanvasBitmap();
    }

    partial void OnHasDocumentChanged(bool value) => UpdateViewportVisibility();

    [SuppressMessage("Minor Code Smell", "S2325", Justification = "Assigns instance observable visibility state; cannot be static.")]
    private void UpdateViewportVisibility()
    {
        Is3DViewportVisible = HasDocument && Renderer3DAvailable && ViewMode == MapCanvasViewMode.Isometric3D;
    }

    partial void OnIsWireframeChanged(bool value) => RefreshCanvasBitmap();

    private void SyncSunFromLighting()
    {
        if (_map == null)
        {
            return;
        }

        var setup = WbLightingService.SelectSlot(_map.Lighting);
        SunPitch = Math.Clamp(setup.SunPitchDegrees, 10.0f, 85.0f);
        var yaw = setup.SunYawDegrees % 360.0f;
        SunYaw = yaw < 0 ? yaw + 360.0f : yaw;
    }

    partial void OnSunPitchChanged(float value) => RefreshCanvasBitmap();

    partial void OnSunYawChanged(float value) => RefreshCanvasBitmap();

    partial void OnCameraPitchChanged(double value)
    {
        if (!_applyingViewportCamera)
        {
            RefreshCanvasBitmap();
        }
    }

    partial void OnCameraYawChanged(double value)
    {
        if (!_applyingViewportCamera)
        {
            RefreshCanvasBitmap();
        }
    }

    partial void OnShowAllObjectLabelsChanged(bool value) => RefreshCanvasBitmap();
}
