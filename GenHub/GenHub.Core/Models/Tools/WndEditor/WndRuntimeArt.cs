using System;
using System.Collections.Generic;

namespace GenHub.Core.Models.Tools.WndEditor;

/// <summary>
/// Runtime presentation facts the game engine applies on top of static draw data
/// (challenge medallions, shell-driven window visibility).
/// </summary>
public sealed record WndRuntimeArt
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WndRuntimeArt"/> class.
    /// Ensures case-insensitive lookup for window names.
    /// </summary>
    /// <param name="medalImages">Medallion image names keyed by decorated window name.</param>
    /// <param name="hiddenWindows">Decorated window names hidden at rest by game code.</param>
    public WndRuntimeArt(
        IReadOnlyDictionary<string, string> medalImages,
        IReadOnlySet<string> hiddenWindows)
    {
        ArgumentNullException.ThrowIfNull(medalImages);
        ArgumentNullException.ThrowIfNull(hiddenWindows);

        MedalImages = new Dictionary<string, string>(medalImages, StringComparer.OrdinalIgnoreCase);
        HiddenWindows = new HashSet<string>(hiddenWindows, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Gets medallion image names keyed by decorated window name.</summary>
    public IReadOnlyDictionary<string, string> MedalImages { get; init; }

    /// <summary>Gets decorated window names hidden at rest by game code.</summary>
    public IReadOnlySet<string> HiddenWindows { get; init; }

    /// <summary>
    /// Gets an empty runtime presentation (no medals, no hidden windows).
    /// </summary>
    public static WndRuntimeArt Empty { get; } = new(
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
        new HashSet<string>(StringComparer.OrdinalIgnoreCase));
}
