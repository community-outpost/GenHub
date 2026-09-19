using System.Collections.Generic;

namespace GenHub.Core.Models.Tools.ModBuilder;

/// <summary>
/// Placeholder for Bundles configuration.
/// </summary>
public sealed class Bundles
{
    /// <summary>
    /// Gets or sets the list of bundle items.
    /// </summary>
    public List<BundleItem>? Items { get; set; }

    /// <summary>
    /// Gets or sets the list of bundle packs.
    /// </summary>
    public List<BundlePack>? Packs { get; set; }
}
