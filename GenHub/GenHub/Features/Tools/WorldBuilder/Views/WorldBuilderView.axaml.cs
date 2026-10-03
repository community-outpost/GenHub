using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GenHub.Core.Constants;
using GenHub.Core.Models.Tools.WorldBuilder;
using GenHub.Features.Tools.WorldBuilder.Controls;
using GenHub.Features.Tools.WorldBuilder.Rendering;
using GenHub.Features.Tools.WorldBuilder.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.WorldBuilder.Views;

/// <summary>
/// View for the WorldBuilder map tool.
/// </summary>
public partial class WorldBuilderView : UserControl
{
    private static readonly TimeSpan TerrainRebuildDebounce = TimeSpan.FromMilliseconds(150);

    private MapCanvasControl? _mapCanvas;
    private WbGlViewport? _glViewport;
    private bool _syncingCamera;
    private DispatcherTimer? _terrainTimer;
    private CancellationTokenSource? _terrainCts;
    private bool _terrainBusy;
    private CancellationTokenSource? _modelsCts;
    private bool _modelsBusy;
    private bool _skipTerrainRefresh;
    private WorldBuilderViewModel? _subscribedVm;
    private int _topZIndex = 100;
    private Border? _draggedWindow;
    private Point _dragStartPoint;
    private Point _dragStartOffset;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorldBuilderView"/> class.
    /// </summary>
    public WorldBuilderView()
    {
        InitializeComponent();
        Focusable = true;
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        DataContextChanged += OnDataContextChanged;
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _mapCanvas = this.FindControl<MapCanvasControl>("MapCanvas");
        if (_mapCanvas != null)
        {
            _mapCanvas.CellPressed += OnCanvasCellPressed;
            _mapCanvas.CellMoved += OnCanvasCellMoved;
            _mapCanvas.CellReleased += OnCanvasCellReleased;
            _mapCanvas.BrushRadiusDecreased += OnBrushRadiusDecreased;
            _mapCanvas.BrushRadiusIncreased += OnBrushRadiusIncreased;
        }

        _glViewport = this.FindControl<WbGlViewport>("GlViewport");
        if (_glViewport != null)
        {
            _glViewport.CellPressed += OnCanvasCellPressed;
            _glViewport.CellMoved += OnCanvasCellMoved;
            _glViewport.CellReleased += OnCanvasCellReleased;
            _glViewport.GlAvailabilityChanged += OnGlAvailabilityChanged;
            _glViewport.CameraChanged += OnViewportCameraChanged;
        }

        if (_subscribedVm == null && DataContext is WorldBuilderViewModel vm)
        {
            _subscribedVm = vm;
            vm.RequestZoomToFit += OnRequestZoomToFit;
            vm.RequestZoomStep += OnRequestZoomStep;
            vm.RequestCenterOnCell += OnRequestCenterOnCell;
            vm.RequestResetView += OnRequestResetView;
            vm.RequestRefreshView += OnRequestRefreshView;
            vm.PropertyChanged += OnViewModelPropertyChanged;
        }

        SyncPreexistingDocument();
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_subscribedVm != null)
        {
            _subscribedVm.RequestZoomToFit -= OnRequestZoomToFit;
            _subscribedVm.RequestZoomStep -= OnRequestZoomStep;
            _subscribedVm.RequestCenterOnCell -= OnRequestCenterOnCell;
            _subscribedVm.RequestResetView -= OnRequestResetView;
            _subscribedVm.RequestRefreshView -= OnRequestRefreshView;
            _subscribedVm.PropertyChanged -= OnViewModelPropertyChanged;
            _subscribedVm = null;
        }

        CancelTerrainRebuild();

        if (_glViewport != null)
        {
            _glViewport.CellPressed -= OnCanvasCellPressed;
            _glViewport.CellMoved -= OnCanvasCellMoved;
            _glViewport.CellReleased -= OnCanvasCellReleased;
            _glViewport.GlAvailabilityChanged -= OnGlAvailabilityChanged;
            _glViewport.CameraChanged -= OnViewportCameraChanged;
            _glViewport = null;
        }

        if (_mapCanvas != null)
        {
            _mapCanvas.CellPressed -= OnCanvasCellPressed;
            _mapCanvas.CellMoved -= OnCanvasCellMoved;
            _mapCanvas.CellReleased -= OnCanvasCellReleased;
            _mapCanvas.BrushRadiusDecreased -= OnBrushRadiusDecreased;
            _mapCanvas.BrushRadiusIncreased -= OnBrushRadiusIncreased;
            _mapCanvas = null;
        }

        base.OnDetachedFromVisualTree(e);
    }

    private static void OnDragOver(object? sender, DragEventArgs e)
    {
        if (e.Data.Contains(DataFormats.Files))
        {
            e.DragEffects = DragDropEffects.Copy;
            e.Handled = true;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
    }

    private static List<string> ExtractDroppedMapPaths(DragEventArgs e)
    {
        var paths = new List<string>();
        var files = e.Data.GetFiles();
        if (files == null)
        {
            return paths;
        }

        foreach (var file in files)
        {
            var localPath = file?.Path?.LocalPath;
            if (!string.IsNullOrEmpty(localPath)
                && string.Equals(Path.GetExtension(localPath), WorldBuilderConstants.FileExtensions.Map, StringComparison.OrdinalIgnoreCase))
            {
                paths.Add(localPath);
            }
        }

        return paths;
    }

    private static T? FindVisualAncestor<T>(Visual? visual)
        where T : Visual
    {
        var current = visual?.GetVisualParent();
        while (current != null)
        {
            if (current is T match)
            {
                return match;
            }

            current = current.GetVisualParent();
        }

        return null;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_subscribedVm != null)
        {
            _subscribedVm.RequestZoomToFit -= OnRequestZoomToFit;
            _subscribedVm.RequestZoomStep -= OnRequestZoomStep;
            _subscribedVm.RequestCenterOnCell -= OnRequestCenterOnCell;
            _subscribedVm.RequestResetView -= OnRequestResetView;
            _subscribedVm.RequestRefreshView -= OnRequestRefreshView;
            _subscribedVm.PropertyChanged -= OnViewModelPropertyChanged;
            _subscribedVm = null;
        }

        if (DataContext is WorldBuilderViewModel vm)
        {
            _subscribedVm = vm;
            vm.RequestZoomToFit += OnRequestZoomToFit;
            vm.RequestZoomStep += OnRequestZoomStep;
            vm.RequestCenterOnCell += OnRequestCenterOnCell;
            vm.RequestResetView += OnRequestResetView;
            vm.RequestRefreshView += OnRequestRefreshView;
            vm.PropertyChanged += OnViewModelPropertyChanged;
            SyncPreexistingDocument();
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Suppress unhandled drag/drop exceptions to protect the UI event loop.")]
    [SuppressMessage("DeepSource", "CS-R1008", Justification = "Suppress unhandled drag/drop exceptions to protect the UI event loop.")]
    private async void OnDrop(object? sender, DragEventArgs e) // skipcq: CS-R1008
    {
        if (DataContext is not WorldBuilderViewModel viewModel || !e.Data.Contains(DataFormats.Files))
        {
            return;
        }

        var paths = ExtractDroppedMapPaths(e);
        if (paths.Count == 0)
        {
            return;
        }

        e.Handled = true;
        try
        {
            await viewModel.OpenMapAsync(paths[0], CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"WorldBuilder drop failed: {ex}");
        }
    }

    private void OnRequestZoomToFit()
    {
        if (_glViewport?.IsVisible == true)
        {
            _glViewport.ZoomToFit();
        }
        else
        {
            _mapCanvas?.ZoomToFit();
        }
    }

    private void OnRequestZoomStep(int direction)
    {
        if (_glViewport?.IsVisible == true)
        {
            _glViewport.ZoomStep(direction);
        }
    }

    private void OnRequestCenterOnCell(int x, int y)
    {
        if (_glViewport?.IsVisible == true)
        {
            _glViewport.CenterOnCell(x, y);
        }
        else
        {
            _mapCanvas?.CenterOnCell(x, y);
        }
    }

    private void OnRequestResetView(WorldBuilderMap map)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => OnRequestResetView(map));
            return;
        }

        SyncViewportToMap(map);
    }

    private void SyncPreexistingDocument()
    {
        if (_glViewport != null && DataContext is WorldBuilderViewModel vm && vm.CurrentMap is WorldBuilderMap map)
        {
            SyncViewportToMap(map);
        }
    }

    private void SyncViewportToMap(WorldBuilderMap map)
    {
        if (_glViewport != null && DataContext is WorldBuilderViewModel vm)
        {
            _glViewport.Map = map;
            _glViewport.ResetView(map, vm.CameraPitch, vm.CameraYaw);
        }

        // The adopt path also triggers a canvas pass; skip its refresh event
        // because this immediate rebuild already covers the new document.
        _skipTerrainRefresh = true;
        _terrainTimer?.Stop();
        _ = RebuildTerrainAsync();
        _ = RebuildModelsAsync();
        RebuildOverlays();
    }

    private void OnRequestRefreshView()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(OnRequestRefreshView);
            return;
        }

        if (_skipTerrainRefresh)
        {
            _skipTerrainRefresh = false;
            return;
        }

        if (_glViewport == null || !_glViewport.GlAvailable)
        {
            return;
        }

        if (_terrainTimer == null)
        {
            _terrainTimer = new DispatcherTimer { Interval = TerrainRebuildDebounce };
            _terrainTimer.Tick += OnTerrainTimerTick;
        }

        _terrainTimer.Stop();
        _terrainTimer.Start();
    }

    private void OnTerrainTimerTick(object? sender, EventArgs e)
    {
        _terrainTimer?.Stop();
        _ = RebuildTerrainAsync();
        _ = RebuildModelsAsync();
        RebuildOverlays();
    }

    private async Task RebuildTerrainAsync()
    {
        if (_glViewport == null || !_glViewport.GlAvailable || _glViewport.Map is not WorldBuilderMap map)
        {
            return;
        }

        if (_terrainBusy)
        {
            OnRequestRefreshView();
            return;
        }

        var renderService = App.Services?.GetService<WbTerrainRenderService>();
        if (renderService == null)
        {
            return;
        }

        _terrainBusy = true;
        if (_terrainCts != null)
        {
            await _terrainCts.CancelAsync().ConfigureAwait(true);
            _terrainCts.Dispose();
        }

        _terrainCts = new CancellationTokenSource();
        var token = _terrainCts.Token;
        try
        {
            var lighting = WbLightingService.SelectSlot(map.Lighting);
            var result = await renderService.BuildAsync(
                map,
                lighting.Ambient,
                lighting.LightDirections,
                lighting.LightDiffuse,
                token).ConfigureAwait(true);
            if (!token.IsCancellationRequested && result.Success && result.Data != null)
            {
                _glViewport?.SetTerrainData(result.Data);
            }
        }
        catch (OperationCanceledException)
        {
            // A newer rebuild superseded this one; the latest data wins.
        }
        finally
        {
            _terrainBusy = false;
        }
    }

    private async Task RebuildModelsAsync()
    {
        if (_glViewport == null || !_glViewport.GlAvailable || _glViewport.Map is not WorldBuilderMap map)
        {
            return;
        }

        if (_modelsBusy)
        {
            OnRequestRefreshView();
            return;
        }

        var modelService = App.Services?.GetService<WbModelRenderService>();
        var roadService = App.Services?.GetService<WbRoadService>();
        var bridgeService = App.Services?.GetService<WbBridgeService>();
        if (modelService == null || roadService == null || bridgeService == null)
        {
            return;
        }

        _modelsBusy = true;
        if (_modelsCts != null)
        {
            await _modelsCts.CancelAsync().ConfigureAwait(true);
            _modelsCts.Dispose();
        }

        _modelsCts = new CancellationTokenSource();
        var token = _modelsCts.Token;
        try
        {
            var draws = new List<WbModelDraw>();
            var models = await modelService.BuildAsync(map, token).ConfigureAwait(true);
            if (models.Success && models.Data != null)
            {
                draws.AddRange(models.Data.Draws);
            }

            var roads = await roadService.BuildRoadsAsync(map, token).ConfigureAwait(true);
            if (roads.Success && roads.Data != null)
            {
                draws.AddRange(roads.Data);
            }

            var bridges = await bridgeService.BuildBridgesAsync(map, token).ConfigureAwait(true);
            if (bridges.Success && bridges.Data != null)
            {
                draws.AddRange(bridges.Data);
            }

            if (!token.IsCancellationRequested)
            {
                _glViewport?.SetModelsData(new WbModelRenderData(draws));
            }
        }
        catch (OperationCanceledException)
        {
            // A newer rebuild superseded this one; the latest data wins.
        }
        finally
        {
            _modelsBusy = false;
        }
    }

    private void RebuildOverlays()
    {
        if (_glViewport == null || !_glViewport.GlAvailable || _glViewport.Map is not WorldBuilderMap map)
        {
            return;
        }

        var waterLevel = DataContext is WorldBuilderViewModel vm ? vm.WaterLevel : 0;
        _glViewport.SetWaterData(WbWaterService.Build(map, waterLevel));
        _glViewport.SetOverlayLines(WbSceneOverlayService.Build(map, _glViewport.RenderOptions.Layers));
    }

    private void CancelTerrainRebuild()
    {
        _terrainTimer?.Stop();
        _terrainTimer = null;
        _terrainCts?.Cancel();
        _terrainCts?.Dispose();
        _terrainCts = null;
        _modelsCts?.Cancel();
        _modelsCts?.Dispose();
        _modelsCts = null;
    }

    private void OnGlAvailabilityChanged(bool available)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => OnGlAvailabilityChanged(available));
            return;
        }

        if (!available && DataContext is WorldBuilderViewModel vm)
        {
            vm.OnRendererFallback(_glViewport?.GlInitError);
        }
    }

    private void OnViewportCameraChanged()
    {
        if (_syncingCamera || _glViewport == null || DataContext is not WorldBuilderViewModel vm)
        {
            return;
        }

        _syncingCamera = true;
        try
        {
            var (yawDegrees, pitchDegrees) = _glViewport.GetOrientation();
            vm.SetCameraFromViewport(yawDegrees, pitchDegrees);
        }
        finally
        {
            _syncingCamera = false;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => OnViewModelPropertyChanged(sender, e));
            return;
        }

        if (_syncingCamera || _glViewport == null || DataContext is not WorldBuilderViewModel vm)
        {
            return;
        }

        if (e.PropertyName == nameof(WorldBuilderViewModel.CameraPitch) || e.PropertyName == nameof(WorldBuilderViewModel.CameraYaw))
        {
            _glViewport.SetOrientation(vm.CameraPitch, vm.CameraYaw);
        }
    }

    private void OnBrushRadiusDecreased() => (DataContext as WorldBuilderViewModel)?.DecreaseBrushRadius();

    private void OnBrushRadiusIncreased() => (DataContext as WorldBuilderViewModel)?.IncreaseBrushRadius();

    private void OnCanvasCellPressed(CellPointerEventArgs e)
    {
        if (DataContext is WorldBuilderViewModel vm)
        {
            vm.HandleCanvasCellPressed(e);
        }
    }

    private void OnCanvasCellMoved(CellPointerEventArgs e)
    {
        if (DataContext is WorldBuilderViewModel vm)
        {
            vm.HandleCanvasCellMoved(e);
        }
    }

    private void OnCanvasCellReleased(CellPointerEventArgs e)
    {
        if (DataContext is WorldBuilderViewModel vm)
        {
            vm.HandleCanvasCellReleased(e);
        }
    }

    private void OnWindowBorderPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border border)
        {
            border.ZIndex = ++_topZIndex;
        }
    }

    private void OnWindowHeaderPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Visual visual)
        {
            var windowBorder = visual as Border ?? FindVisualAncestor<Border>(visual);
            if (windowBorder != null)
            {
                windowBorder.ZIndex = ++_topZIndex;
                _draggedWindow = windowBorder;
                _dragStartPoint = e.GetPosition(this);
                if (windowBorder.RenderTransform is not TranslateTransform)
                {
                    windowBorder.RenderTransform = new TranslateTransform();
                }

                var tt = (TranslateTransform)windowBorder.RenderTransform;
                _dragStartOffset = new Point(tt.X, tt.Y);
                if (sender is IInputElement input)
                {
                    e.Pointer.Capture(input);
                }

                e.Handled = true;
            }
        }
    }

    private void OnWindowHeaderMoved(object? sender, PointerEventArgs e)
    {
        if (_draggedWindow != null && _draggedWindow.RenderTransform is TranslateTransform tt)
        {
            var cur = e.GetPosition(this);
            tt.X = _dragStartOffset.X + (cur.X - _dragStartPoint.X);
            tt.Y = _dragStartOffset.Y + (cur.Y - _dragStartPoint.Y);
            e.Handled = true;
        }
    }

    private void OnWindowHeaderReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_draggedWindow != null)
        {
            _draggedWindow = null;
            if (sender is IInputElement)
            {
                e.Pointer.Capture(null);
            }

            e.Handled = true;
        }
    }
}
