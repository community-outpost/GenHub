using System;

namespace GenHub.Core.Models.Tools.ModBuilder;

/// <summary>
/// Event arguments for bundle events.
/// </summary>
public class BundleEventArgs : EventArgs
{
    /// <summary>
    /// Gets or sets the event type.
    /// </summary>
    public required BundleEventType EventType { get; set; }

    /// <summary>
    /// Gets or sets the bundle item name (if applicable).
    /// </summary>
    public string? BundleItemName { get; set; }

    /// <summary>
    /// Gets or sets the bundle pack name (if applicable).
    /// </summary>
    public string? BundlePackName { get; set; }

    /// <summary>
    /// Gets or sets the build index (stage).
    /// </summary>
    public BuildIndex? BuildIndex { get; set; }

    /// <summary>
    /// Gets or sets additional event data.
    /// </summary>
    public Dictionary<string, object> Data { get; set; } = new();
}
