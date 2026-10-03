using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Constants;
using GenHub.Core.Models.Tools.WorldBuilder;
using GenHub.Core.Services.Tools.WorldBuilder;
using System;
using System.Linq;

namespace GenHub.Features.Tools.WorldBuilder.ViewModels;

/// <summary>
/// Floating windows, dialogs, and tools visibility orchestration for WorldBuilder.
/// </summary>
public sealed partial class WorldBuilderViewModel
{
    [ObservableProperty]
    private bool isObjectPropertiesOpen;

    [ObservableProperty]
    private bool isScriptEditorOpen;

    [ObservableProperty]
    private bool isTeamsOpen;

    [ObservableProperty]
    private bool isEnvironmentOpen;

    [ObservableProperty]
    private bool isMinimapOpen;

    [ObservableProperty]
    private bool isMapGenOpen;

    [ObservableProperty]
    private bool isLayersOpen;

    [ObservableProperty]
    private bool isRoadsOpen;

    [ObservableProperty]
    private bool isMapInfoOpen;

    /// <summary>Raised when the viewport should fit the canvas.</summary>
    public event Action? RequestZoomToFit;

    /// <summary>Raised when the 3D viewport should dolly one notch: positive zooms in, negative zooms out.</summary>
    public event Action<int>? RequestZoomStep;

    /// <summary>Raised when the viewport should center on a cell.</summary>
    public event Action<int, int>? RequestCenterOnCell;

    /// <summary>Raised when a new document is adopted and the 3D view should reset.</summary>
    public event Action<WorldBuilderMap>? RequestResetView;

    /// <summary>Raised when a canvas render pass completes and the 3D terrain should rebuild.</summary>
    public event Action? RequestRefreshView;

    /// <summary>Toggles the Object Properties floating window.</summary>
    [RelayCommand]
    public void ToggleObjectProperties() => IsObjectPropertiesOpen = !IsObjectPropertiesOpen;

    /// <summary>Toggles the Script Editor floating window.</summary>
    [RelayCommand]
    public void ToggleScriptEditor() => IsScriptEditorOpen = !IsScriptEditorOpen;

    /// <summary>Toggles the Skirmish Teams and Sides floating window.</summary>
    [RelayCommand]
    public void ToggleTeams() => IsTeamsOpen = !IsTeamsOpen;

    /// <summary>Toggles the Environment and Lighting floating window.</summary>
    [RelayCommand]
    public void ToggleEnvironment() => IsEnvironmentOpen = !IsEnvironmentOpen;

    /// <summary>Toggles the Minimap floating window.</summary>
    [RelayCommand]
    public void ToggleMinimap() => IsMinimapOpen = !IsMinimapOpen;

    /// <summary>Toggles the Map Generator dialog.</summary>
    [RelayCommand]
    public void ToggleMapGen() => IsMapGenOpen = !IsMapGenOpen;

    /// <summary>Toggles the Layers panel.</summary>
    [RelayCommand]
    public void ToggleLayers() => IsLayersOpen = !IsLayersOpen;

    /// <summary>Toggles the Roads and Bridges floating window.</summary>
    [RelayCommand]
    public void ToggleRoads() => IsRoadsOpen = !IsRoadsOpen;

    /// <summary>Toggles the Map Summary and Validation floating window.</summary>
    [RelayCommand]
    public void ToggleMapInfo() => IsMapInfoOpen = !IsMapInfoOpen;

    /// <summary>Opens the new map generator dialog.</summary>
    [RelayCommand]
    public void NewMap()
    {
        IsMapGenOpen = true;
    }

    /// <summary>Centers and fits the canvas within the viewport.</summary>
    [RelayCommand]
    public void ZoomToFit()
    {
        RequestZoomToFit?.Invoke();
    }

    /// <summary>Centers the viewport on a cell coordinate.</summary>
    /// <param name="cellX">Cell X coordinate.</param>
    /// <param name="cellY">Cell Y coordinate.</param>
    public void CenterOnCell(int cellX, int cellY)
    {
        RequestCenterOnCell?.Invoke(cellX, cellY);
    }
}
