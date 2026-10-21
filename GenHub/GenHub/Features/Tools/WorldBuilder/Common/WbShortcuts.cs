// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using Avalonia.Controls;
using Avalonia.Input;
using System;
using System.Collections.Generic;

namespace GenHub.Features.Tools.WorldBuilder.Common;

/// <summary>
/// WorldBuilder command identifiers for the global keyboard shortcut table.
/// Mirrors the WBID_* command ids behind qt/WBQtShortcuts.cpp s_accels[].
/// </summary>
public enum WbCommand
{
    /// <summary>No command.</summary>
    None,

    /// <summary>Pick constraints: anything, structures, infantry, vehicles.</summary>
    PickAnything,

    /// <summary>Pick structures only.</summary>
    PickStructures,

    /// <summary>Pick infantry only.</summary>
    PickInfantry,

    /// <summary>Pick vehicles only.</summary>
    PickVehicles,

    /// <summary>Pick shrubbery only.</summary>
    PickShrubbery,

    /// <summary>Pick man-made props only.</summary>
    PickManMade,

    /// <summary>Pick natural props only.</summary>
    PickNatural,

    /// <summary>Pick debris only.</summary>
    PickDebris,

    /// <summary>Pick waypoints and areas only.</summary>
    PickWaypoints,

    /// <summary>Pick roads only.</summary>
    PickRoads,

    /// <summary>Group pivot center.</summary>
    GroupPivotCenter,

    /// <summary>Group rotate object.</summary>
    GroupRotateObject,

    /// <summary>Show the entire 3D map.</summary>
    ViewShowEntireMap,

    /// <summary>Show object icons.</summary>
    ViewShowObjects,

    /// <summary>Copy selection.</summary>
    EditCopy,

    /// <summary>Change time of day.</summary>
    ViewTimeOfDay,

    /// <summary>Show water.</summary>
    ViewShowWater,

    /// <summary>Top-down view.</summary>
    ViewShowTopDown,

    /// <summary>Show grid.</summary>
    ViewShowGrid,

    /// <summary>Snap to grid.</summary>
    ViewSnapToGrid,

    /// <summary>Show impassable areas.</summary>
    ViewShowImpassable,

    /// <summary>Jump to game.</summary>
    FileJumpToGame,

    /// <summary>Link centers.</summary>
    EditLinkCenters,

    /// <summary>Select similar objects.</summary>
    EditSelectSimilar,

    /// <summary>New map.</summary>
    FileNew,

    /// <summary>Open map.</summary>
    FileOpen,

    /// <summary>Ruler grid.</summary>
    ViewRulerGrid,

    /// <summary>Replace selected objects.</summary>
    EditReplace,

    /// <summary>Save map.</summary>
    FileSave,

    /// <summary>Show texture.</summary>
    ViewShowTexture,

    /// <summary>Show clouds.</summary>
    ViewShowClouds,

    /// <summary>Paste selection.</summary>
    EditPaste,

    /// <summary>Wireframe view.</summary>
    ViewShowWireframe,

    /// <summary>Cut selection.</summary>
    EditCut,

    /// <summary>Undo.</summary>
    EditUndo,

    /// <summary>Redo.</summary>
    EditRedo,

    /// <summary>View home.</summary>
    ViewHome,

    /// <summary>Waypoint tool.</summary>
    WaypointTool,

    /// <summary>Polygon tool.</summary>
    PolygonTool,

    /// <summary>Border tool.</summary>
    BorderTool,

    /// <summary>Script editor.</summary>
    ScriptEdit,

    /// <summary>Team editor.</summary>
    TeamEdit,

    /// <summary>Lock horizontal.</summary>
    LockHorizontal,

    /// <summary>Brush tool.</summary>
    BrushTool,

    /// <summary>Brush add tool.</summary>
    BrushAddTool,

    /// <summary>Brush subtract tool.</summary>
    BrushSubtractTool,

    /// <summary>Feather tool.</summary>
    FeatherTool,

    /// <summary>Mesh mold tool.</summary>
    MoldTool,

    /// <summary>Water tool.</summary>
    WaterTool,

    /// <summary>Tile tool.</summary>
    TileTool,

    /// <summary>Big tile tool.</summary>
    BigTileTool,

    /// <summary>Tile flood fill tool.</summary>
    TileFloodFill,

    /// <summary>Auto edge out tool.</summary>
    AutoEdgeOutTool,

    /// <summary>Blend edge tool.</summary>
    BlendEdgeTool,

    /// <summary>Place object tool.</summary>
    PlaceObjectTool,

    /// <summary>Road tool.</summary>
    RoadTool,

    /// <summary>Grove tool.</summary>
    GroveTool,

    /// <summary>Ramp tool.</summary>
    RampTool,

    /// <summary>Scorch tool.</summary>
    ScorchTool,

    /// <summary>Fence tool.</summary>
    FenceTool,

    /// <summary>Build list tool.</summary>
    BuildListTool,

    /// <summary>Show waypoints.</summary>
    ViewShowWaypoints,

    /// <summary>Show polygon triggers.</summary>
    ViewShowPolygonTriggers,

    /// <summary>Show labels.</summary>
    ViewShowLabels,

    /// <summary>Show models.</summary>
    ViewShowModels,

    /// <summary>Show bounding boxes.</summary>
    ViewShowBoundingBoxes,

    /// <summary>Show sight ranges.</summary>
    ViewShowSightRanges,

    /// <summary>Show weapon ranges.</summary>
    ViewShowWeaponRanges,

    /// <summary>Show map boundaries.</summary>
    ViewShowMapBoundaries,

    /// <summary>Fixed colored waypoints.</summary>
    ViewFixedColoredWaypoints,

    /// <summary>Reset the render device.</summary>
    ViewResetDevice,

    /// <summary>Toggle fullscreen.</summary>
    ViewToggleFullscreen,

    /// <summary>Exit fullscreen.</summary>
    ViewExitFullscreen,

    /// <summary>Delete the selected objects.</summary>
    EditDeleteSelected,

    /// <summary>Shrink the active brush.</summary>
    BrushShrink,

    /// <summary>Grow the active brush.</summary>
    BrushGrow,

    /// <summary>Nudge selection left.</summary>
    NudgeLeft,

    /// <summary>Nudge selection right.</summary>
    NudgeRight,

    /// <summary>Nudge selection up.</summary>
    NudgeUp,

    /// <summary>Nudge selection down.</summary>
    NudgeDown,
}

/// <summary>
/// Global WorldBuilder keyboard shortcut table. Direct port of the
/// qt/WBQtShortcuts.cpp s_accels[] accelerator table (which mirrors
/// res/WorldBuilder.rc), plus the Qt-only extras: Shift+F5 device reset,
/// F11/Esc fullscreen, bare Del/Backspace delete, brackets/arrows brush/nudge.
/// Shortcuts fire only when the viewport or main window owns focus, never from
/// a text field in a floating panel.
/// </summary>
public static class WbShortcuts
{
    private static readonly IReadOnlyList<(Key Key, KeyModifiers Modifiers, WbCommand Command)> Table =
    [
        (Key.D0, KeyModifiers.Control, WbCommand.PickAnything),
        (Key.D1, KeyModifiers.Control, WbCommand.PickStructures),
        (Key.D2, KeyModifiers.Control, WbCommand.PickInfantry),
        (Key.D3, KeyModifiers.Control, WbCommand.PickVehicles),
        (Key.D4, KeyModifiers.Control, WbCommand.PickShrubbery),
        (Key.D5, KeyModifiers.Control, WbCommand.PickManMade),
        (Key.D6, KeyModifiers.Control, WbCommand.PickNatural),
        (Key.D7, KeyModifiers.Control, WbCommand.PickDebris),
        (Key.D8, KeyModifiers.Control, WbCommand.PickWaypoints),
        (Key.D9, KeyModifiers.Control, WbCommand.PickRoads),
        (Key.D1, KeyModifiers.Control | KeyModifiers.Shift, WbCommand.GroupPivotCenter),
        (Key.D2, KeyModifiers.Control | KeyModifiers.Shift, WbCommand.GroupRotateObject),
        (Key.A, KeyModifiers.Control, WbCommand.ViewShowEntireMap),
        (Key.B, KeyModifiers.Control, WbCommand.ViewShowObjects),
        (Key.C, KeyModifiers.Control, WbCommand.EditCopy),
        (Key.D, KeyModifiers.Control, WbCommand.ViewTimeOfDay),
        (Key.E, KeyModifiers.Control, WbCommand.ViewShowWater),
        (Key.F, KeyModifiers.Control, WbCommand.ViewShowTopDown),
        (Key.G, KeyModifiers.Control, WbCommand.ViewShowGrid),
        (Key.G, KeyModifiers.Control | KeyModifiers.Shift, WbCommand.ViewSnapToGrid),
        (Key.I, KeyModifiers.Control, WbCommand.ViewShowImpassable),
        (Key.J, KeyModifiers.Control, WbCommand.FileJumpToGame),
        (Key.L, KeyModifiers.Control, WbCommand.EditLinkCenters),
        (Key.M, KeyModifiers.Control, WbCommand.EditSelectSimilar),
        (Key.N, KeyModifiers.Control, WbCommand.FileNew),
        (Key.O, KeyModifiers.Control, WbCommand.FileOpen),
        (Key.Q, KeyModifiers.Control, WbCommand.ViewRulerGrid),
        (Key.R, KeyModifiers.Control, WbCommand.EditReplace),
        (Key.S, KeyModifiers.Control, WbCommand.FileSave),
        (Key.T, KeyModifiers.Control, WbCommand.ViewShowTexture),
        (Key.U, KeyModifiers.Control, WbCommand.ViewShowClouds),
        (Key.V, KeyModifiers.Control, WbCommand.EditPaste),
        (Key.W, KeyModifiers.Control, WbCommand.ViewShowWireframe),
        (Key.X, KeyModifiers.Control, WbCommand.EditCut),
        (Key.Z, KeyModifiers.Control, WbCommand.EditUndo),
        (Key.Z, KeyModifiers.Control | KeyModifiers.Shift, WbCommand.EditRedo),
        (Key.Back, KeyModifiers.Alt, WbCommand.EditUndo),
        (Key.Delete, KeyModifiers.Shift, WbCommand.EditCut),
        (Key.Insert, KeyModifiers.Control, WbCommand.EditCopy),
        (Key.Insert, KeyModifiers.Shift, WbCommand.EditPaste),
        (Key.Home, KeyModifiers.None, WbCommand.ViewHome),
        (Key.F1, KeyModifiers.None, WbCommand.WaypointTool),
        (Key.F2, KeyModifiers.None, WbCommand.PolygonTool),
        (Key.F3, KeyModifiers.None, WbCommand.BorderTool),
        (Key.F4, KeyModifiers.None, WbCommand.ScriptEdit),
        (Key.F5, KeyModifiers.None, WbCommand.TeamEdit),
        (Key.F5, KeyModifiers.Shift, WbCommand.ViewResetDevice),
        (Key.Tab, KeyModifiers.None, WbCommand.LockHorizontal),
        (Key.Q, KeyModifiers.None, WbCommand.BrushTool),
        (Key.W, KeyModifiers.None, WbCommand.BrushAddTool),
        (Key.E, KeyModifiers.None, WbCommand.BrushSubtractTool),
        (Key.R, KeyModifiers.None, WbCommand.FeatherTool),
        (Key.T, KeyModifiers.None, WbCommand.MoldTool),
        (Key.Y, KeyModifiers.None, WbCommand.WaterTool),
        (Key.A, KeyModifiers.None, WbCommand.TileTool),
        (Key.S, KeyModifiers.None, WbCommand.BigTileTool),
        (Key.D, KeyModifiers.None, WbCommand.TileFloodFill),
        (Key.F, KeyModifiers.None, WbCommand.AutoEdgeOutTool),
        (Key.G, KeyModifiers.None, WbCommand.BlendEdgeTool),
        (Key.Z, KeyModifiers.None, WbCommand.PlaceObjectTool),
        (Key.X, KeyModifiers.None, WbCommand.RoadTool),
        (Key.C, KeyModifiers.None, WbCommand.GroveTool),
        (Key.V, KeyModifiers.None, WbCommand.RampTool),
        (Key.B, KeyModifiers.None, WbCommand.ScorchTool),
        (Key.N, KeyModifiers.None, WbCommand.FenceTool),
        (Key.M, KeyModifiers.None, WbCommand.BuildListTool),
        (Key.D1, KeyModifiers.Alt, WbCommand.ViewShowObjects),
        (Key.D2, KeyModifiers.Alt, WbCommand.ViewShowWaypoints),
        (Key.D3, KeyModifiers.Alt, WbCommand.ViewShowPolygonTriggers),
        (Key.D4, KeyModifiers.Alt, WbCommand.ViewShowLabels),
        (Key.D5, KeyModifiers.Alt, WbCommand.ViewShowModels),
        (Key.D6, KeyModifiers.Alt, WbCommand.ViewShowBoundingBoxes),
        (Key.D7, KeyModifiers.Alt, WbCommand.ViewShowSightRanges),
        (Key.D8, KeyModifiers.Alt, WbCommand.ViewShowWeaponRanges),
        (Key.D9, KeyModifiers.Alt, WbCommand.ViewShowMapBoundaries),
        (Key.D0, KeyModifiers.Alt, WbCommand.ViewFixedColoredWaypoints),
        (Key.F11, KeyModifiers.None, WbCommand.ViewToggleFullscreen),
        (Key.Escape, KeyModifiers.None, WbCommand.ViewExitFullscreen),
        (Key.Delete, KeyModifiers.None, WbCommand.EditDeleteSelected),
        (Key.Back, KeyModifiers.None, WbCommand.EditDeleteSelected),
        (Key.OemOpenBrackets, KeyModifiers.None, WbCommand.BrushShrink),
        (Key.OemCloseBrackets, KeyModifiers.None, WbCommand.BrushGrow),
        (Key.Left, KeyModifiers.None, WbCommand.NudgeLeft),
        (Key.Right, KeyModifiers.None, WbCommand.NudgeRight),
        (Key.Up, KeyModifiers.None, WbCommand.NudgeUp),
        (Key.Down, KeyModifiers.None, WbCommand.NudgeDown),
    ];

    /// <summary>
    /// Gets the shortcut table entries in priority order.
    /// </summary>
    public static IReadOnlyList<(Key Key, KeyModifiers Modifiers, WbCommand Command)> Entries => Table;

    /// <summary>
    /// Looks up the command for a key chord. On macOS the Command modifier
    /// normalizes to Control so the same table drives both platforms.
    /// </summary>
    /// <param name="key">The pressed key.</param>
    /// <param name="modifiers">The active modifiers.</param>
    /// <param name="normalizeCommandModifier">Treat Meta as Control.</param>
    /// <returns>The bound command, or null.</returns>
    public static WbCommand? TryGetCommand(Key key, KeyModifiers modifiers, bool normalizeCommandModifier = true)
    {
        var normalized = modifiers;
        if (normalizeCommandModifier && normalized.HasFlag(KeyModifiers.Meta))
        {
            normalized = (normalized & ~KeyModifiers.Meta) | KeyModifiers.Control;
        }

        foreach (var entry in Table)
        {
            if (entry.Key == key && entry.Modifiers == normalized)
            {
                return entry.Command;
            }
        }

        return null;
    }

    /// <summary>
    /// Reports whether shortcuts must be suppressed because a text field owns
    /// focus. Mirrors the Qt focus check that keeps Ctrl+C/V/X/Z working in
    /// panel text fields instead of firing object commands.
    /// </summary>
    /// <param name="focusedElement">The currently focused element, if any.</param>
    /// <returns>True when shortcuts must not fire.</returns>
    public static bool ShouldSuppressForFocusedElement(IInputElement? focusedElement)
    {
        return focusedElement is TextBox;
    }
}
