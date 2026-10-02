using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Constants;
using GenHub.Core.Models.Tools.WorldBuilder;
using GenHub.Core.Services.Tools.WorldBuilder;
using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace GenHub.Features.Tools.WorldBuilder.ViewModels;

/// <summary>
/// Road networks, bridges, lighting, environment, and skirmish team editing for WorldBuilder.
/// </summary>
public sealed partial class WorldBuilderViewModel
{
    [ObservableProperty]
    private string selectedRoadType = "PavedRoad";

    [ObservableProperty]
    private bool isRoadAngledCorner;

    [ObservableProperty]
    private bool isRoadTightCorner;

    [ObservableProperty]
    private RoadSegment? selectedRoadSegment;

    [ObservableProperty]
    private string selectedBridgeTemplate = "BridgeConcrete";

    [ObservableProperty]
    private BridgeSegment? selectedBridgeSegment;

    [ObservableProperty]
    private string selectedFenceTemplate = "Fence";

    [ObservableProperty]
    private double fenceSpacing = 20.0;

    [ObservableProperty]
    private int selectedTimeOfDayIndex;

    [ObservableProperty]
    private float sunLightColorR = 1.0f;

    [ObservableProperty]
    private float sunLightColorG = 0.95f;

    [ObservableProperty]
    private float sunLightColorB = 0.85f;

    [ObservableProperty]
    private float ambientLightColorR = 0.35f;

    [ObservableProperty]
    private float ambientLightColorG = 0.35f;

    [ObservableProperty]
    private float ambientLightColorB = 0.4f;

    [ObservableProperty]
    private int waterLevel = WorldBuilderConstants.Canvas.DefaultWaterLevel;

    [ObservableProperty]
    private string selectedWeather = WorldBuilderConstants.WeatherPresets.Normal;

    [ObservableProperty]
    private MapSideEntry? selectedPlayerSide;

    [ObservableProperty]
    private MapTeamEntry? selectedSkirmishTeam;

    [ObservableProperty]
    private string newTeamName = string.Empty;

    [ObservableProperty]
    private string newSideName = string.Empty;

    [ObservableProperty]
    private string selectedTeamName = string.Empty;

    [ObservableProperty]
    private string selectedTeamOwner = string.Empty;

    [ObservableProperty]
    private string selectedSideDisplayName = string.Empty;

    [ObservableProperty]
    private bool selectedSideIsHuman;

    [ObservableProperty]
    private string selectedSideFaction = string.Empty;

    /// <summary>Gets the list of available road templates.</summary>
    public ObservableCollection<string> AvailableRoadTypes { get; } =
    [
        "PavedRoad",
        "DirtRoad",
        "CobblestoneRoad",
        "TwoLaneRoad",
        "Highway",
        "DirtTrack",
        "Path",
    ];

    /// <summary>Gets the list of available bridge templates.</summary>
    public ObservableCollection<string> AvailableBridgeTemplates { get; } =
    [
        "BridgeWood",
        "BridgeConcrete",
        "BridgeSuspension",
        "BridgeDrawbridge",
        "BridgeRailway",
    ];

    /// <summary>Gets the road segments present on the map.</summary>
    public ObservableCollection<RoadSegment> RoadSegments { get; } = [];

    /// <summary>Gets the bridges present on the map.</summary>
    public ObservableCollection<BridgeSegment> BridgeSegments { get; } = [];

    /// <summary>Gets the player sides configured on the map.</summary>
    public ObservableCollection<MapSideEntry> PlayerSides { get; } = [];

    /// <summary>Gets the skirmish teams configured on the map.</summary>
    public ObservableCollection<MapTeamEntry> SkirmishTeams { get; } = [];

    /// <summary>Gets the weather options.</summary>
    public ObservableCollection<string> WeatherOptions { get; } =
    [
        WorldBuilderConstants.WeatherPresets.Normal,
        WorldBuilderConstants.WeatherPresets.Snow,
    ];

    /// <summary>
    /// Deletes the currently selected or specified road segment.
    /// </summary>
    /// <param name="segment">Optional segment to delete.</param>
    [RelayCommand]
    public void DeleteRoadSegment(RoadSegment? segment = null)
    {
        var target = segment ?? SelectedRoadSegment;
        if (_map == null || target == null)
        {
            return;
        }

        var idx = RoadSegments.IndexOf(target);
        if (idx < 0)
        {
            return;
        }

        _undoService.Checkpoint(_map);
        if (MapOverlayTools.DeleteRoadSegment(_map, idx))
        {
            SyncRoadsAndBridges();
            IsDirty = true;
            RefreshCanvasBitmap();
            UpdateUndoState();
        }
    }

    /// <summary>
    /// Deletes the currently selected or specified bridge.
    /// </summary>
    /// <param name="bridge">Optional bridge to delete.</param>
    [RelayCommand]
    public void DeleteBridge(BridgeSegment? bridge = null)
    {
        var target = bridge ?? SelectedBridgeSegment;
        if (_map == null || target == null)
        {
            return;
        }

        var idx = BridgeSegments.IndexOf(target);
        if (idx < 0)
        {
            return;
        }

        _undoService.Checkpoint(_map);
        if (MapOverlayTools.DeleteBridge(_map, idx))
        {
            SyncRoadsAndBridges();
            IsDirty = true;
            RefreshCanvasBitmap();
            UpdateUndoState();
        }
    }

    /// <summary>
    /// Applies lighting and weather adjustments to the map document.
    /// </summary>
    [RelayCommand]
    public void ApplyLighting()
    {
        if (_map == null)
        {
            return;
        }

        _undoService.Checkpoint(_map);

        // Engine enum GameType.h: TIME_OF_DAY_INVALID=0, MORNING=1, AFTERNOON=2, EVENING=3, NIGHT=4
        _map.Lighting.TimeOfDay = Math.Clamp(SelectedTimeOfDayIndex + 1, 1, 4);

        // Engine enum GameType.h: WEATHER_NORMAL=0, WEATHER_SNOWY=1
        var weatherInt = string.Equals(SelectedWeather, WorldBuilderConstants.WeatherPresets.Snow, StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        _map.World.Set(new MapDictValue(
            WorldBuilderConstants.DictKeys.Weather,
            WorldBuilderConstants.DictValueType.Int,
            IntValue: weatherInt));

        IsDirty = true;
        SyncSunFromLighting();
        RefreshCanvasBitmap();
        UpdateUndoState();
    }

    /// <summary>
    /// Updates the global water table elevation.
    /// </summary>
    /// <param name="level">The new water elevation in cells.</param>
    [RelayCommand]
    public void SetWaterLevel(int level)
    {
        WaterLevel = Math.Clamp(level, 0, WorldBuilderConstants.Terrain.MaxHeight);
        RefreshCanvasBitmap();
    }

    /// <summary>
    /// Adds a new skirmish team.
    /// </summary>
    [RelayCommand]
    public void AddTeam()
    {
        if (_map == null || string.IsNullOrWhiteSpace(NewTeamName))
        {
            return;
        }

        var team = new MapTeamEntry();
        team.Properties.Set(new MapDictValue(
            WorldBuilderConstants.DictKeys.TeamName,
            WorldBuilderConstants.DictValueType.AsciiString,
            StringValue: NewTeamName.Trim()));

        _undoService.Checkpoint(_map);
        _map.Teams.Add(team);
        SyncSidesAndTeams();
        SelectedSkirmishTeam = team;
        NewTeamName = string.Empty;
        IsDirty = true;
        UpdateUndoState();
    }

    /// <summary>
    /// Applies the edited name and owner to the selected skirmish team.
    /// </summary>
    [RelayCommand]
    public void ApplyTeamProperties()
    {
        if (_map == null || SelectedSkirmishTeam == null || string.IsNullOrWhiteSpace(SelectedTeamName))
        {
            return;
        }

        _undoService.Checkpoint(_map);
        SelectedSkirmishTeam.Properties.Set(new MapDictValue(
            WorldBuilderConstants.DictKeys.TeamName,
            WorldBuilderConstants.DictValueType.AsciiString,
            StringValue: SelectedTeamName.Trim()));
        SelectedSkirmishTeam.Properties.Set(new MapDictValue(
            WorldBuilderConstants.DictKeys.TeamOwner,
            WorldBuilderConstants.DictValueType.AsciiString,
            StringValue: SelectedTeamOwner.Trim()));
        SyncSidesAndTeams();
        IsDirty = true;
        UpdateUndoState();
    }

    /// <summary>
    /// Deletes the selected skirmish team.
    /// </summary>
    [RelayCommand]
    public void DeleteTeam()
    {
        if (_map == null || SelectedSkirmishTeam == null)
        {
            return;
        }

        _undoService.Checkpoint(_map);
        _map.Teams.Remove(SelectedSkirmishTeam);
        SyncSidesAndTeams();
        IsDirty = true;
        UpdateUndoState();
    }

    /// <summary>
    /// Adds a new player side.
    /// </summary>
    [RelayCommand]
    public void AddSide()
    {
        if (_map == null || string.IsNullOrWhiteSpace(NewSideName))
        {
            return;
        }

        var name = NewSideName.Trim();
        var side = new MapSideEntry();
        side.Properties.Set(new MapDictValue(
            WorldBuilderConstants.DictKeys.PlayerName,
            WorldBuilderConstants.DictValueType.AsciiString,
            StringValue: name));
        side.Properties.Set(new MapDictValue(
            WorldBuilderConstants.DictKeys.PlayerIsHuman,
            WorldBuilderConstants.DictValueType.Bool,
            IntValue: 0));
        side.Properties.Set(new MapDictValue(
            WorldBuilderConstants.DictKeys.PlayerDisplayName,
            WorldBuilderConstants.DictValueType.UnicodeString,
            StringValue: name));
        side.Properties.Set(new MapDictValue(
            WorldBuilderConstants.DictKeys.PlayerFaction,
            WorldBuilderConstants.DictValueType.AsciiString,
            StringValue: string.Empty));
        side.Properties.Set(new MapDictValue(
            WorldBuilderConstants.DictKeys.PlayerEnemies,
            WorldBuilderConstants.DictValueType.AsciiString,
            StringValue: string.Empty));
        side.Properties.Set(new MapDictValue(
            WorldBuilderConstants.DictKeys.PlayerAllies,
            WorldBuilderConstants.DictValueType.AsciiString,
            StringValue: string.Empty));

        _undoService.Checkpoint(_map);
        _map.Sides.Add(side);
        SyncSidesAndTeams();
        SelectedPlayerSide = side;
        NewSideName = string.Empty;
        IsDirty = true;
        UpdateUndoState();
    }

    /// <summary>
    /// Applies the edited display name, human flag, and faction to the selected side.
    /// </summary>
    [RelayCommand]
    public void ApplySideProperties()
    {
        if (_map == null || SelectedPlayerSide == null)
        {
            return;
        }

        _undoService.Checkpoint(_map);
        SelectedPlayerSide.Properties.Set(new MapDictValue(
            WorldBuilderConstants.DictKeys.PlayerDisplayName,
            WorldBuilderConstants.DictValueType.UnicodeString,
            StringValue: SelectedSideDisplayName));
        SelectedPlayerSide.Properties.Set(new MapDictValue(
            WorldBuilderConstants.DictKeys.PlayerIsHuman,
            WorldBuilderConstants.DictValueType.Bool,
            IntValue: SelectedSideIsHuman ? 1 : 0));
        SelectedPlayerSide.Properties.Set(new MapDictValue(
            WorldBuilderConstants.DictKeys.PlayerFaction,
            WorldBuilderConstants.DictValueType.AsciiString,
            StringValue: SelectedSideFaction.Trim()));
        SyncSidesAndTeams();
        IsDirty = true;
        UpdateUndoState();
    }

    /// <summary>
    /// Deletes the selected player side.
    /// </summary>
    [RelayCommand]
    public void DeleteSide()
    {
        if (_map == null || SelectedPlayerSide == null)
        {
            return;
        }

        _undoService.Checkpoint(_map);
        _map.Sides.Remove(SelectedPlayerSide);
        SyncSidesAndTeams();
        IsDirty = true;
        UpdateUndoState();
    }

    private void SyncRoadsAndBridges()
    {
        RoadSegments.Clear();
        BridgeSegments.Clear();

        if (_map == null)
        {
            return;
        }

        foreach (var road in MapOverlayTools.GetRoadSegments(_map))
        {
            RoadSegments.Add(road);
        }

        foreach (var bridge in MapOverlayTools.GetBridges(_map))
        {
            BridgeSegments.Add(bridge);
        }
    }

    private void SyncSidesAndTeams()
    {
        PlayerSides.Clear();
        SkirmishTeams.Clear();

        if (_map == null)
        {
            return;
        }

        foreach (var side in _map.Sides)
        {
            PlayerSides.Add(side);
        }

        foreach (var team in _map.Teams)
        {
            SkirmishTeams.Add(team);
        }
    }

    private void SyncEnvironmentAndLighting()
    {
        if (_map == null)
        {
            return;
        }

        // Engine enum: 1=Morning, 2=Afternoon, 3=Evening, 4=Night
        SelectedTimeOfDayIndex = _map.Lighting.TimeOfDay >= 1
            ? Math.Clamp(_map.Lighting.TimeOfDay - 1, 0, 3)
            : 0;

        // Upstream stores weather key as Int (0 = Normal, 1 = Snowy), backwards-compatible with string
        var weatherDict = _map.World.Find(WorldBuilderConstants.DictKeys.Weather);
        if (weatherDict?.Type == WorldBuilderConstants.DictValueType.Int)
        {
            SelectedWeather = weatherDict.IntValue == 1
                ? WorldBuilderConstants.WeatherPresets.Snow
                : WorldBuilderConstants.WeatherPresets.Normal;
        }
        else
        {
            var weatherStr = _map.World.GetString(
                WorldBuilderConstants.DictKeys.Weather,
                WorldBuilderConstants.WeatherPresets.Normal);
            SelectedWeather = string.Equals(weatherStr, WorldBuilderConstants.WeatherPresets.Snow, StringComparison.OrdinalIgnoreCase)
                ? WorldBuilderConstants.WeatherPresets.Snow
                : WorldBuilderConstants.WeatherPresets.Normal;
        }
    }

    partial void OnSelectedSkirmishTeamChanged(MapTeamEntry? value)
    {
        if (value != null)
        {
            SelectedTeamName = value.Properties.GetString(WorldBuilderConstants.DictKeys.TeamName, string.Empty);
            SelectedTeamOwner = value.Properties.GetString(WorldBuilderConstants.DictKeys.TeamOwner, string.Empty);
        }
    }

    partial void OnSelectedPlayerSideChanged(MapSideEntry? value)
    {
        if (value != null)
        {
            SelectedSideDisplayName = value.Properties.GetString(WorldBuilderConstants.DictKeys.PlayerDisplayName, string.Empty);
            SelectedSideIsHuman = value.Properties.GetBool(WorldBuilderConstants.DictKeys.PlayerIsHuman, false);
            SelectedSideFaction = value.Properties.GetString(WorldBuilderConstants.DictKeys.PlayerFaction, string.Empty);
        }
    }
}
