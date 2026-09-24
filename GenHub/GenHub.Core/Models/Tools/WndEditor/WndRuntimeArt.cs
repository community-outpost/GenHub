using System.Collections.Generic;

namespace GenHub.Core.Models.Tools.WndEditor;

/// <summary>
/// Runtime presentation facts the game engine applies on top of static draw data
/// (challenge medallions, shell-driven window visibility).
/// </summary>
/// <param name="MedalImages">Medallion image names keyed by decorated window name.</param>
/// <param name="HiddenWindows">Decorated window names hidden at rest by game code.</param>
public sealed record WndRuntimeArt(
    IReadOnlyDictionary<string, string> MedalImages,
    IReadOnlySet<string> HiddenWindows)
{
    /// <summary>
    /// Gets an empty runtime presentation (no medals, no hidden windows).
    /// </summary>
    public static WndRuntimeArt Empty { get; } = new(
        new Dictionary<string, string>(),
        new HashSet<string>());
}
