using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Constants;
using GenHub.Core.Models.Tools.WorldBuilder;
using GenHub.Core.Services.Tools.WorldBuilder;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace GenHub.Features.Tools.WorldBuilder.ViewModels;

/// <summary>
/// Map objects, waypoint network, and trigger layout operations for WorldBuilder.
/// </summary>
public sealed partial class WorldBuilderViewModel
{
    private static readonly string[] DefaultTemplates =
    [
        WorldBuilderConstants.Objects.DefaultTemplate,
        "CivilianBuilding02",
        "SupplyDock",
        "SupplyWarehouse",
        "AmericaVehicleCrusader",
        "ChinaTankBattleMaster",
        "GLAVehicleTechnical",
        "TreePine01",
        "TreeOak01",
    ];

    [ObservableProperty]
    private MapObjectEntry? selectedObject;

    [ObservableProperty]
    private string objectFilterText = string.Empty;

    [ObservableProperty]
    private string selectedObjectTemplate = WorldBuilderConstants.Objects.DefaultTemplate;

    [ObservableProperty]
    private string selectedObjectName = string.Empty;

    [ObservableProperty]
    private string selectedObjectTeam = WorldBuilderConstants.Objects.NeutralTeam;

    [ObservableProperty]
    private string selectedObjectScript = "<none>";

    [ObservableProperty]
    private int selectedObjectHealthPercent = 100;

    [ObservableProperty]
    private string selectedObjectAggressiveness = WorldBuilderConstants.Objects.Normal;

    private int _selectedObjectAggressivenessIndex = 1;

    [ObservableProperty]
    private string selectedObjectVeterancy = WorldBuilderConstants.Objects.Normal;

    private int _selectedObjectVeterancyIndex;

    [ObservableProperty]
    private bool selectedObjectEnabled = true;

    [ObservableProperty]
    private bool selectedObjectIndestructible;

    [ObservableProperty]
    private bool selectedObjectSelectable = true;

    [ObservableProperty]
    private int selectedObjectHitPoints;

    [ObservableProperty]
    private bool selectedObjectUnsellable;

    [ObservableProperty]
    private bool selectedObjectTargetable = true;

    [ObservableProperty]
    private bool selectedObjectRecruitableAI = true;

    [ObservableProperty]
    private bool selectedObjectPowered = true;

    [ObservableProperty]
    private float selectedObjectStoppingDistance;

    [ObservableProperty]
    private float selectedObjectVisionDistance;

    [ObservableProperty]
    private float selectedObjectShroudClearingDistance;

    [ObservableProperty]
    private string selectedObjectWeather = WorldBuilderConstants.WeatherPresets.Normal;

    [ObservableProperty]
    private string selectedObjectTime = string.Empty;

    [ObservableProperty]
    private float selectedObjectScale = 1f;

    [ObservableProperty]
    private bool selectedObjectScaleEnabled;

    [ObservableProperty]
    private string selectedObjectSound = string.Empty;

    [ObservableProperty]
    private bool selectedObjectSoundCustomize;

    [ObservableProperty]
    private bool selectedObjectSoundEnabled = true;

    [ObservableProperty]
    private bool selectedObjectSoundLooping;

    [ObservableProperty]
    private int selectedObjectSoundLoopCount = 1;

    [ObservableProperty]
    private string selectedObjectSoundPriority = WorldBuilderConstants.Objects.Normal;

    [ObservableProperty]
    private float selectedObjectSoundVolume = 1f;

    [ObservableProperty]
    private float selectedObjectSoundMinVolume;

    [ObservableProperty]
    private float selectedObjectSoundMinRange;

    [ObservableProperty]
    private float selectedObjectSoundMaxRange;

    [ObservableProperty]
    private string selectedObjectUpgrades = string.Empty;

    [ObservableProperty]
    private bool selectedObjectReflectsInMirror;

    [ObservableProperty]
    private float selectedObjectX;

    [ObservableProperty]
    private float selectedObjectY;

    [ObservableProperty]
    private float selectedObjectZ;

    [ObservableProperty]
    private float selectedObjectAngle;

    [ObservableProperty]
    private MapObjectEntry? selectedWaypoint;

    [ObservableProperty]
    private MapWaypointLink? selectedWaypointLink;

    [ObservableProperty]
    private MapTrigger? selectedTrigger;

    [ObservableProperty]
    private int selectedPlayerStartNumber = 1;

    /// <summary>Gets the list of all map objects.</summary>
    public ObservableCollection<MapObjectEntry> Objects { get; } = [];

    /// <summary>Gets the filtered list of map objects.</summary>
    public ObservableCollection<MapObjectEntry> FilteredObjects { get; } = [];

    /// <summary>Gets preset object template names.</summary>
    public ObservableCollection<string> ObjectTemplates { get; } = [.. DefaultTemplates];

    /// <summary>Gets the weather presets for the object weather picker.</summary>
    public ObservableCollection<string> ObjectWeatherOptions { get; } =
    [
        WorldBuilderConstants.WeatherPresets.Normal,
        WorldBuilderConstants.WeatherPresets.Snow,
    ];

    /// <summary>Gets the waypoint objects.</summary>
    public ObservableCollection<MapObjectEntry> Waypoints { get; } = [];

    /// <summary>Gets the waypoint links.</summary>
    public ObservableCollection<MapWaypointLink> WaypointLinks { get; } = [];

    /// <summary>Gets the area triggers.</summary>
    public ObservableCollection<MapTrigger> Triggers { get; } = [];

    /// <summary>Gets or sets the selected object aggressiveness index.</summary>
    public int SelectedObjectAggressivenessIndex
    {
        get => _selectedObjectAggressivenessIndex;
        set => SetProperty(ref _selectedObjectAggressivenessIndex, value);
    }

    /// <summary>Gets or sets the selected object veterancy index.</summary>
    public int SelectedObjectVeterancyIndex
    {
        get => _selectedObjectVeterancyIndex;
        set => SetProperty(ref _selectedObjectVeterancyIndex, value);
    }

    /// <summary>
    /// Adds a new object at the center of the battlefield.
    /// </summary>
    [RelayCommand]
    public void AddObject()
    {
        if (_map == null)
        {
            return;
        }

        _undoService.Checkpoint(_map);
        var center = MapCoordinates.CellCenterToWorld(_map.Terrain.BorderSize, _map.Terrain.Width / 2, _map.Terrain.Height / 2);
        var cx = center.X;
        var cy = center.Y;
        var template = string.IsNullOrWhiteSpace(SelectedObjectTemplate) ? WorldBuilderConstants.Objects.DefaultTemplate : SelectedObjectTemplate;
        var obj = MapOverlayTools.PlaceObject(_map, template, cx, cy);
        SyncObjects();
        SelectedObject = obj;
        IsDirty = true;
        RefreshCanvasBitmap();
        UpdateUndoState();
    }

    /// <summary>
    /// Applies edited properties to the selected map object.
    /// </summary>
    [RelayCommand]
    public void ApplyObjectProperties()
    {
        if (_map == null || SelectedObject == null)
        {
            return;
        }

        _undoService.Checkpoint(_map);
        SelectedObject.Name = SelectedObjectName;
        SelectedObject.X = SelectedObjectX;
        SelectedObject.Y = SelectedObjectY;
        SelectedObject.Z = SelectedObjectZ;
        SelectedObject.Angle = (SelectedObjectAngle * MathF.PI) / 180.0f;

        ResolveObjectStance();
        WriteObjectCoreProperties(SelectedObject);
        WriteObjectFlagProperties(SelectedObject);
        WriteObjectSoundProperties(SelectedObject);

        SyncObjects();
        IsDirty = true;
        RefreshCanvasBitmap();
        UpdateUndoState();
    }

    /// <summary>
    /// Duplicates the selected map object.
    /// </summary>
    [RelayCommand]
    public void DuplicateObject()
    {
        if (_map == null || SelectedObject == null)
        {
            return;
        }

        _undoService.Checkpoint(_map);
        var copy = MapOverlayTools.PlaceObject(_map, SelectedObject.Name, SelectedObject.X + 25f, SelectedObject.Y + 25f);
        copy.Angle = SelectedObject.Angle;
        SyncObjects();
        SelectedObject = copy;
        IsDirty = true;
        RefreshCanvasBitmap();
        UpdateUndoState();
    }

    /// <summary>
    /// Deletes the currently selected object.
    /// </summary>
    [RelayCommand]
    public void DeleteObject()
    {
        if (_map == null || SelectedObject == null)
        {
            return;
        }

        _undoService.Checkpoint(_map);
        MapOverlayTools.DeleteObject(_map, SelectedObject);
        SelectedObject = null;
        SyncObjects();
        SyncWaypoints();
        IsDirty = true;
        RefreshCanvasBitmap();
        UpdateUndoState();
    }

    /// <summary>
    /// Adds a new waypoint at the center of the battlefield.
    /// </summary>
    [RelayCommand]
    public void AddWaypoint()
    {
        if (_map == null)
        {
            return;
        }

        _undoService.Checkpoint(_map);
        var waypointCenter = MapCoordinates.CellCenterToWorld(_map.Terrain.BorderSize, _map.Terrain.Width / 2, _map.Terrain.Height / 2);
        var cx = waypointCenter.X;
        var cy = waypointCenter.Y;
        var wp = MapOverlayTools.AddWaypoint(_map, cx, cy);
        SyncWaypoints();
        SelectedWaypoint = wp;
        IsDirty = true;
        RefreshCanvasBitmap();
        UpdateUndoState();
    }

    /// <summary>
    /// Deletes the currently selected waypoint.
    /// </summary>
    [RelayCommand]
    public void DeleteWaypoint()
    {
        if (_map == null || SelectedWaypoint == null)
        {
            return;
        }

        _undoService.Checkpoint(_map);
        var wpId = SelectedWaypoint.Properties.GetInt(WorldBuilderConstants.DictKeys.WaypointId, -1);
        if (wpId >= 0)
        {
            _map.WaypointLinks.RemoveAll(l => l.Waypoint1 == wpId || l.Waypoint2 == wpId);
        }

        MapOverlayTools.DeleteObject(_map, SelectedWaypoint);
        SelectedWaypoint = null;
        SyncWaypoints();
        SyncWaypointLinks();
        IsDirty = true;
        RefreshCanvasBitmap();
        UpdateUndoState();
    }

    /// <summary>
    /// Adds a player starting spot waypoint (e.g. Player_1_Start).
    /// </summary>
    /// <param name="playerNumber">The player index 1-8.</param>
    [RelayCommand]
    public void AddPlayerStart(int? playerNumber)
    {
        if (_map == null)
        {
            return;
        }

        var num = playerNumber ?? SelectedPlayerStartNumber;
        if (num < 1 || num > 8)
        {
            num = 1;
        }

        _undoService.Checkpoint(_map);
        var startCenter = MapCoordinates.CellCenterToWorld(_map.Terrain.BorderSize, _map.Terrain.Width / 2, _map.Terrain.Height / 2);
        var cx = startCenter.X;
        var cy = startCenter.Y;
        var name = $"Player_{num}_Start";

        // Remove old start if already exists
        MapOverlayTools.DeleteObject(_map, name);

        var wp = MapOverlayTools.AddWaypoint(_map, cx, cy);
        wp.Name = name;
        wp.Properties.Set(new MapDictValue(
            WorldBuilderConstants.DictKeys.WaypointName,
            WorldBuilderConstants.DictValueType.UnicodeString,
            StringValue: name));

        SyncWaypoints();
        SelectedWaypoint = wp;
        IsDirty = true;
        RefreshCanvasBitmap();
        UpdateUndoState();
    }

    /// <summary>
    /// Links the selected waypoint to another waypoint.
    /// </summary>
    [RelayCommand]
    public void LinkWaypoints()
    {
        if (_map == null || SelectedWaypoint == null)
        {
            return;
        }

        var firstId = SelectedWaypoint.Properties.GetInt(WorldBuilderConstants.DictKeys.WaypointId, -1);
        if (firstId < 0)
        {
            return;
        }

        var other = Waypoints.FirstOrDefault(w => w != SelectedWaypoint);
        if (other == null)
        {
            return;
        }

        var secondId = other.Properties.GetInt(WorldBuilderConstants.DictKeys.WaypointId, -1);
        if (secondId < 0 || secondId == firstId)
        {
            return;
        }

        _undoService.Checkpoint(_map);
        if (MapOverlayTools.LinkWaypoints(_map, firstId, secondId))
        {
            SyncWaypointLinks();
            IsDirty = true;
            RefreshCanvasBitmap();
            UpdateUndoState();
        }
    }

    /// <summary>
    /// Removes the selected waypoint link.
    /// </summary>
    [RelayCommand]
    public void UnlinkWaypoints()
    {
        if (_map == null || SelectedWaypointLink == null)
        {
            return;
        }

        _undoService.Checkpoint(_map);
        if (MapOverlayTools.UnlinkWaypoints(_map, SelectedWaypointLink.Waypoint1, SelectedWaypointLink.Waypoint2))
        {
            SelectedWaypointLink = null;
            SyncWaypointLinks();
            IsDirty = true;
            RefreshCanvasBitmap();
            UpdateUndoState();
        }
    }

    /// <summary>
    /// Adds a default rectangular trigger area.
    /// </summary>
    [RelayCommand]
    public void AddTrigger()
    {
        if (_map == null)
        {
            return;
        }

        _undoService.Checkpoint(_map);
        var cellSize = (int)WorldBuilderConstants.Terrain.CellSize;
        var cx = (_map.Terrain.Width / 2) * cellSize;
        var cy = (_map.Terrain.Height / 2) * cellSize;
        var points = new (int X, int Y, int Z)[]
        {
            (cx - 10, cy - 10, 0),
            (cx + 10, cy - 10, 0),
            (cx + 10, cy + 10, 0),
            (cx - 10, cy + 10, 0),
        };

        var triggerName = $"Trigger_{Triggers.Count + 1}";
        var trigger = MapOverlayTools.AddTrigger(_map, triggerName, points);
        SyncTriggers();
        SelectedTrigger = trigger;
        IsDirty = true;
        RefreshCanvasBitmap();
        UpdateUndoState();
    }

    /// <summary>
    /// Deletes the selected trigger.
    /// </summary>
    [RelayCommand]
    public void DeleteTrigger()
    {
        if (_map == null || SelectedTrigger == null)
        {
            return;
        }

        _undoService.Checkpoint(_map);
        MapOverlayTools.DeleteTrigger(_map, SelectedTrigger.Id);
        SelectedTrigger = null;
        SyncTriggers();
        IsDirty = true;
        RefreshCanvasBitmap();
        UpdateUndoState();
    }

    private static MapDictValue BoolValue(string key, bool value)
    {
        return new MapDictValue(key, WorldBuilderConstants.DictValueType.Bool, IntValue: value ? 1 : 0);
    }

    private void ResolveObjectStance()
    {
        SelectedObjectAggressiveness = SelectedObjectAggressivenessIndex switch
        {
            0 => WorldBuilderConstants.Objects.Passive,
            2 => WorldBuilderConstants.Objects.Aggressive,
            _ => WorldBuilderConstants.Objects.Normal,
        };
        SelectedObjectVeterancy = SelectedObjectVeterancyIndex switch
        {
            1 => WorldBuilderConstants.Objects.Veteran,
            2 => WorldBuilderConstants.Objects.Elite,
            3 => WorldBuilderConstants.Objects.Heroic,
            _ => WorldBuilderConstants.Objects.Regular,
        };
    }

    [SuppressMessage("Minor Code Smell", "S2325", Justification = "Reads instance observable editing properties; cannot be static.")]
    private void WriteObjectCoreProperties(MapObjectEntry obj)
    {
        obj.Properties.Set(new MapDictValue(WorldBuilderConstants.DictKeys.TeamName, WorldBuilderConstants.DictValueType.AsciiString, StringValue: SelectedObjectTeam));
        obj.Properties.Set(new MapDictValue(WorldBuilderConstants.DictKeys.ObjectScript, WorldBuilderConstants.DictValueType.AsciiString, StringValue: SelectedObjectScript));
        obj.Properties.Set(new MapDictValue(WorldBuilderConstants.DictKeys.ObjectHealth, WorldBuilderConstants.DictValueType.Int, IntValue: SelectedObjectHealthPercent));
        obj.Properties.Set(new MapDictValue(WorldBuilderConstants.DictKeys.ObjectHitPoints, WorldBuilderConstants.DictValueType.Int, IntValue: SelectedObjectHitPoints));
        obj.Properties.Set(new MapDictValue(WorldBuilderConstants.DictKeys.ObjectAggressiveness, WorldBuilderConstants.DictValueType.AsciiString, StringValue: SelectedObjectAggressiveness));
        obj.Properties.Set(new MapDictValue(WorldBuilderConstants.DictKeys.ObjectVeterancy, WorldBuilderConstants.DictValueType.AsciiString, StringValue: SelectedObjectVeterancy));
        obj.Properties.Set(new MapDictValue(WorldBuilderConstants.DictKeys.ObjectStoppingDistance, WorldBuilderConstants.DictValueType.Real, RealValue: SelectedObjectStoppingDistance));
        obj.Properties.Set(new MapDictValue(WorldBuilderConstants.DictKeys.ObjectVisionDistance, WorldBuilderConstants.DictValueType.Real, RealValue: SelectedObjectVisionDistance));
        obj.Properties.Set(new MapDictValue(WorldBuilderConstants.DictKeys.ObjectShroudClearingDistance, WorldBuilderConstants.DictValueType.Real, RealValue: SelectedObjectShroudClearingDistance));
        obj.Properties.Set(new MapDictValue(WorldBuilderConstants.DictKeys.Weather, WorldBuilderConstants.DictValueType.AsciiString, StringValue: SelectedObjectWeather));
        obj.Properties.Set(new MapDictValue(WorldBuilderConstants.DictKeys.ObjectTime, WorldBuilderConstants.DictValueType.AsciiString, StringValue: SelectedObjectTime));
        obj.Properties.Set(new MapDictValue(WorldBuilderConstants.DictKeys.ObjectScale, WorldBuilderConstants.DictValueType.Real, RealValue: SelectedObjectScale));
        obj.Properties.Set(new MapDictValue(WorldBuilderConstants.DictKeys.ObjectUpgrades, WorldBuilderConstants.DictValueType.AsciiString, StringValue: SelectedObjectUpgrades));
    }

    [SuppressMessage("Minor Code Smell", "S2325", Justification = "Reads instance observable editing properties; cannot be static.")]
    private void WriteObjectFlagProperties(MapObjectEntry obj)
    {
        obj.Properties.Set(BoolValue(WorldBuilderConstants.DictKeys.ObjectEnabled, SelectedObjectEnabled));
        obj.Properties.Set(BoolValue(WorldBuilderConstants.DictKeys.ObjectUnsellable, SelectedObjectUnsellable));
        obj.Properties.Set(BoolValue(WorldBuilderConstants.DictKeys.ObjectTargetable, SelectedObjectTargetable));
        obj.Properties.Set(BoolValue(WorldBuilderConstants.DictKeys.ObjectIndestructible, SelectedObjectIndestructible));
        obj.Properties.Set(BoolValue(WorldBuilderConstants.DictKeys.ObjectRecruitableAI, SelectedObjectRecruitableAI));
        obj.Properties.Set(BoolValue(WorldBuilderConstants.DictKeys.ObjectPowered, SelectedObjectPowered));
        obj.Properties.Set(BoolValue(WorldBuilderConstants.DictKeys.ObjectSelectable, SelectedObjectSelectable));
        obj.Properties.Set(BoolValue(WorldBuilderConstants.DictKeys.ObjectScaleEnabled, SelectedObjectScaleEnabled));
        obj.Properties.Set(BoolValue(WorldBuilderConstants.DictKeys.ObjectReflectsInMirror, SelectedObjectReflectsInMirror));
    }

    [SuppressMessage("Minor Code Smell", "S2325", Justification = "Reads instance observable editing properties; cannot be static.")]
    private void WriteObjectSoundProperties(MapObjectEntry obj)
    {
        obj.Properties.Set(new MapDictValue(WorldBuilderConstants.DictKeys.ObjectSound, WorldBuilderConstants.DictValueType.AsciiString, StringValue: SelectedObjectSound));
        obj.Properties.Set(BoolValue(WorldBuilderConstants.DictKeys.ObjectSoundCustomize, SelectedObjectSoundCustomize));
        obj.Properties.Set(BoolValue(WorldBuilderConstants.DictKeys.ObjectSoundEnabled, SelectedObjectSoundEnabled));
        obj.Properties.Set(BoolValue(WorldBuilderConstants.DictKeys.ObjectSoundLooping, SelectedObjectSoundLooping));
        obj.Properties.Set(new MapDictValue(WorldBuilderConstants.DictKeys.ObjectSoundLoopCount, WorldBuilderConstants.DictValueType.Int, IntValue: SelectedObjectSoundLoopCount));
        obj.Properties.Set(new MapDictValue(WorldBuilderConstants.DictKeys.ObjectSoundPriority, WorldBuilderConstants.DictValueType.AsciiString, StringValue: SelectedObjectSoundPriority));
        obj.Properties.Set(new MapDictValue(WorldBuilderConstants.DictKeys.ObjectSoundVolume, WorldBuilderConstants.DictValueType.Real, RealValue: SelectedObjectSoundVolume));
        obj.Properties.Set(new MapDictValue(WorldBuilderConstants.DictKeys.ObjectSoundMinVolume, WorldBuilderConstants.DictValueType.Real, RealValue: SelectedObjectSoundMinVolume));
        obj.Properties.Set(new MapDictValue(WorldBuilderConstants.DictKeys.ObjectSoundMinRange, WorldBuilderConstants.DictValueType.Real, RealValue: SelectedObjectSoundMinRange));
        obj.Properties.Set(new MapDictValue(WorldBuilderConstants.DictKeys.ObjectSoundMaxRange, WorldBuilderConstants.DictValueType.Real, RealValue: SelectedObjectSoundMaxRange));
    }

    private void SyncObjects()
    {
        Objects.Clear();
        if (_map != null)
        {
            foreach (var obj in _map.Objects)
            {
                Objects.Add(obj);
            }
        }

        RefreshFilteredObjects();
    }

    private void RefreshFilteredObjects()
    {
        FilteredObjects.Clear();
        var filter = ObjectFilterText?.Trim();
        var matchingObjects = Objects.Where(obj =>
            string.IsNullOrEmpty(filter)
            || obj.Name.Contains(filter, StringComparison.OrdinalIgnoreCase));

        foreach (var obj in matchingObjects)
        {
            FilteredObjects.Add(obj);
        }
    }

    private void SyncWaypoints()
    {
        Waypoints.Clear();
        if (_map != null)
        {
            foreach (var obj in _map.Objects.Where(o => o.Properties.Find(WorldBuilderConstants.DictKeys.WaypointId) != null))
            {
                Waypoints.Add(obj);
            }
        }
    }

    private void SyncWaypointLinks()
    {
        WaypointLinks.Clear();
        if (_map != null)
        {
            foreach (var link in _map.WaypointLinks)
            {
                WaypointLinks.Add(link);
            }
        }
    }

    private void SyncTriggers()
    {
        Triggers.Clear();
        if (_map != null)
        {
            foreach (var trigger in _map.Triggers)
            {
                Triggers.Add(trigger);
            }
        }
    }

    partial void OnObjectFilterTextChanged(string value) => RefreshFilteredObjects();

    partial void OnSelectedObjectChanged(MapObjectEntry? value)
    {
        if (value != null)
        {
            SelectedObjectName = value.Name;
            SelectedObjectTeam = value.Properties.GetString(WorldBuilderConstants.DictKeys.TeamName, WorldBuilderConstants.Objects.NeutralTeam);
            SelectedObjectScript = value.Properties.GetString(WorldBuilderConstants.DictKeys.ObjectScript, "<none>");
            SelectedObjectX = value.X;
            SelectedObjectY = value.Y;
            SelectedObjectZ = value.Z;
            SelectedObjectAngle = (value.Angle * 180.0f) / MathF.PI;
            SelectedObjectHealthPercent = value.Properties.GetInt(WorldBuilderConstants.DictKeys.ObjectHealth, 100);
            SelectedObjectHitPoints = value.Properties.GetInt(WorldBuilderConstants.DictKeys.ObjectHitPoints, 0);
            SelectedObjectAggressiveness = value.Properties.GetString(WorldBuilderConstants.DictKeys.ObjectAggressiveness, WorldBuilderConstants.Objects.Normal);
            SelectedObjectAggressivenessIndex = SelectedObjectAggressiveness switch
            {
                WorldBuilderConstants.Objects.Passive => 0,
                WorldBuilderConstants.Objects.Aggressive => 2,
                _ => 1,
            };
            SelectedObjectVeterancy = value.Properties.GetString(WorldBuilderConstants.DictKeys.ObjectVeterancy, WorldBuilderConstants.Objects.Regular);
            SelectedObjectVeterancyIndex = SelectedObjectVeterancy switch
            {
                WorldBuilderConstants.Objects.Veteran => 1,
                WorldBuilderConstants.Objects.Elite => 2,
                WorldBuilderConstants.Objects.Heroic => 3,
                _ => 0,
            };
            SelectedObjectEnabled = value.Properties.GetBool(WorldBuilderConstants.DictKeys.ObjectEnabled, true);
            SelectedObjectUnsellable = value.Properties.GetBool(WorldBuilderConstants.DictKeys.ObjectUnsellable, false);
            SelectedObjectTargetable = value.Properties.GetBool(WorldBuilderConstants.DictKeys.ObjectTargetable, true);
            SelectedObjectIndestructible = value.Properties.GetBool(WorldBuilderConstants.DictKeys.ObjectIndestructible, false);
            SelectedObjectRecruitableAI = value.Properties.GetBool(WorldBuilderConstants.DictKeys.ObjectRecruitableAI, true);
            SelectedObjectPowered = value.Properties.GetBool(WorldBuilderConstants.DictKeys.ObjectPowered, true);
            SelectedObjectSelectable = value.Properties.GetBool(WorldBuilderConstants.DictKeys.ObjectSelectable, true);
            SelectedObjectStoppingDistance = value.Properties.GetReal(WorldBuilderConstants.DictKeys.ObjectStoppingDistance, 0f);
            SelectedObjectVisionDistance = value.Properties.GetReal(WorldBuilderConstants.DictKeys.ObjectVisionDistance, 0f);
            SelectedObjectShroudClearingDistance = value.Properties.GetReal(WorldBuilderConstants.DictKeys.ObjectShroudClearingDistance, 0f);
            SelectedObjectWeather = value.Properties.GetString(WorldBuilderConstants.DictKeys.Weather, WorldBuilderConstants.WeatherPresets.Normal);
            SelectedObjectTime = value.Properties.GetString(WorldBuilderConstants.DictKeys.ObjectTime, string.Empty);
            SelectedObjectScale = value.Properties.GetReal(WorldBuilderConstants.DictKeys.ObjectScale, 1f);
            SelectedObjectScaleEnabled = value.Properties.GetBool(WorldBuilderConstants.DictKeys.ObjectScaleEnabled, false);
            SelectedObjectSound = value.Properties.GetString(WorldBuilderConstants.DictKeys.ObjectSound, string.Empty);
            SelectedObjectSoundCustomize = value.Properties.GetBool(WorldBuilderConstants.DictKeys.ObjectSoundCustomize, false);
            SelectedObjectSoundEnabled = value.Properties.GetBool(WorldBuilderConstants.DictKeys.ObjectSoundEnabled, true);
            SelectedObjectSoundLooping = value.Properties.GetBool(WorldBuilderConstants.DictKeys.ObjectSoundLooping, false);
            SelectedObjectSoundLoopCount = value.Properties.GetInt(WorldBuilderConstants.DictKeys.ObjectSoundLoopCount, 1);
            SelectedObjectSoundPriority = value.Properties.GetString(WorldBuilderConstants.DictKeys.ObjectSoundPriority, WorldBuilderConstants.Objects.Normal);
            SelectedObjectSoundVolume = value.Properties.GetReal(WorldBuilderConstants.DictKeys.ObjectSoundVolume, 1f);
            SelectedObjectSoundMinVolume = value.Properties.GetReal(WorldBuilderConstants.DictKeys.ObjectSoundMinVolume, 0f);
            SelectedObjectSoundMinRange = value.Properties.GetReal(WorldBuilderConstants.DictKeys.ObjectSoundMinRange, 0f);
            SelectedObjectSoundMaxRange = value.Properties.GetReal(WorldBuilderConstants.DictKeys.ObjectSoundMaxRange, 0f);
            SelectedObjectUpgrades = value.Properties.GetString(WorldBuilderConstants.DictKeys.ObjectUpgrades, string.Empty);
            SelectedObjectReflectsInMirror = value.Properties.GetBool(WorldBuilderConstants.DictKeys.ObjectReflectsInMirror, false);
            IsObjectPropertiesOpen = true;
        }

        RefreshCanvasBitmap();
    }

    partial void OnSelectedWaypointChanged(MapObjectEntry? value) => RefreshCanvasBitmap();
}
