// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using System.Collections.Generic;

namespace GenHub.Core.Models.Tools.WorldBuilder;

/// <summary>
/// Persisted WorldBuilder preferences. The GenHub equivalent of Adriane's
/// Worldbuilder.ini user profile: panel geometry, tutorial hints, undo depth,
/// and picker search toggles.
/// </summary>
public sealed class WorldBuilderSettings
{
    /// <summary>
    /// Gets or sets a value indicating whether one-time tutorial hint toasts may show. Defaults on.
    /// </summary>
    public bool ShowTutorialHints { get; set; } = true;

    /// <summary>
    /// Gets or sets the undo history depth (1..999). GenHub default is 50.
    /// </summary>
    public int UndoDepth { get; set; } = 50;

    /// <summary>
    /// Gets or sets a value indicating whether tree pickers filter live as you type (NewSearch).
    /// </summary>
    public bool LiveSearchInTrees { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether long combo boxes filter as you type.
    /// </summary>
    public bool SearchInComboBoxes { get; set; } = true;

    /// <summary>
    /// Gets or sets saved frame geometry per window name.
    /// </summary>
    public Dictionary<string, WbWindowGeometry> WindowGeometries { get; set; } = [];

    /// <summary>
    /// Gets or sets dismissed one-time tutorial hint keys.
    /// </summary>
    public HashSet<string> DismissedHints { get; set; } = [];

    /// <summary>
    /// Creates a deep copy of the settings.
    /// </summary>
    /// <returns>A new settings instance.</returns>
    public WorldBuilderSettings Clone()
    {
        var clone = new WorldBuilderSettings
        {
            ShowTutorialHints = ShowTutorialHints,
            UndoDepth = UndoDepth,
            LiveSearchInTrees = LiveSearchInTrees,
            SearchInComboBoxes = SearchInComboBoxes,
            WindowGeometries = [],
            DismissedHints = [.. DismissedHints],
        };
        foreach (var entry in WindowGeometries)
        {
            clone.WindowGeometries[entry.Key] = new WbWindowGeometry
            {
                X = entry.Value.X,
                Y = entry.Value.Y,
                Width = entry.Value.Width,
                Height = entry.Value.Height,
            };
        }

        return clone;
    }
}
