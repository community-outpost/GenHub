using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Constants;
using GenHub.Core.Models.Tools.WorldBuilder;
using GenHub.Core.Services.Tools.WorldBuilder;
using GenHub.Features.Tools.WorldBuilder.Rendering;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.WorldBuilder.ViewModels;

/// <summary>
/// Texture tile swatches and high-resolution minimap generation for WorldBuilder.
/// </summary>
public sealed partial class WorldBuilderViewModel
{
    [ObservableProperty]
    private MapTextureClass? selectedTexture;

    /// <summary>Gets the collection of terrain texture classes defined in the map.</summary>
    public ObservableCollection<MapTextureClass> TextureClasses { get; } = [];

    /// <summary>
    /// Centers the main viewport camera based on a click on the minimap radar.
    /// </summary>
    /// <param name="relativePoint">Relative 0..1 point in minimap space.</param>
    public void CenterFromMinimap(Point relativePoint)
    {
        if (_map == null || _map.Terrain.Width <= 0 || _map.Terrain.Height <= 0)
        {
            return;
        }

        var cellX = (int)(relativePoint.X * _map.Terrain.Width);
        var cellY = (int)(relativePoint.Y * _map.Terrain.Height);
        CenterOnCell(cellX, cellY);
    }

    /// <summary>
    /// Generates a high-resolution minimap render and updates the document preview image.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [RelayCommand(AllowConcurrentExecutions = false)]
    public async Task GenerateMinimapAsync()
    {
        if (_map == null || _map.Terrain.Width <= 0 || _map.Terrain.Height <= 0)
        {
            return;
        }

        var map = _map;
        var options = new MapCanvasRenderOptions
        {
            ViewMode = MapCanvasViewMode.TopDown2D,
            Layers = MapCanvasLayers.Terrain | MapCanvasLayers.Water | MapCanvasLayers.Cliffs | MapCanvasLayers.Blend | MapCanvasLayers.Boundary,
            WaterLevel = WorldBuilderConstants.Canvas.DefaultWaterLevel,
            GridStep = WorldBuilderConstants.Canvas.DefaultGridStep,
        };

        var width = map.Terrain.Width;
        var height = map.Terrain.Height;
        var pixels = await Task.Run(() => WbFallbackPreview.RenderTopDown(map, options).Pixels).ConfigureAwait(false);

        void ApplyMinimap()
        {
            if (_disposed || _map == null || _map != map)
            {
                return;
            }

            var bitmap = new WriteableBitmap(
                new PixelSize(width, height),
                new Vector(96, 96),
                PixelFormats.Bgra8888);

            using (var frame = bitmap.Lock())
            {
                for (var y = 0; y < height; y++)
                {
                    Marshal.Copy(pixels, y * width, frame.Address + (y * frame.RowBytes), width);
                }
            }

            PreviewImage?.Dispose();
            PreviewImage = bitmap;

            notificationService.ShowSuccess(
                localizationService.GetString("Tools.WorldBuilder.Minimap.GeneratedTitle"),
                localizationService.GetString("Tools.WorldBuilder.Minimap.GeneratedMessage"));
        }

        if (Avalonia.Application.Current == null || Dispatcher.UIThread.CheckAccess())
        {
            ApplyMinimap();
        }
        else
        {
            await Dispatcher.UIThread.InvokeAsync(ApplyMinimap);
        }
    }

    private void SyncTextures()
    {
        TextureClasses.Clear();
        if (_map == null)
        {
            SelectedTexture = null;
            return;
        }

        foreach (var tex in _map.Terrain.TextureClasses)
        {
            TextureClasses.Add(tex);
        }

        if (TextureClasses.Count > 0)
        {
            SelectedTexture = TextureClasses[0];
        }
    }

    private void SyncAllCollections()
    {
        SyncObjects();
        SyncWaypoints();
        SyncWaypointLinks();
        SyncTriggers();
        SyncTextures();
        SyncScripts();
        SyncRoadsAndBridges();
        SyncSidesAndTeams();
        SyncEnvironmentAndLighting();
    }
}
