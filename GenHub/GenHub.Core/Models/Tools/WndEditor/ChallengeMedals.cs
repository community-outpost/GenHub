using System.Collections.Generic;

namespace GenHub.Core.Models.Tools.WndEditor;

/// <summary>
/// Challenge general medallions resolved from ChallengeMode.ini personas and
/// PlayerTemplate.ini medallion fields, mirroring ChallengeMenu.cpp.
/// </summary>
/// <param name="MedalsByPosition">Resting medallion image names keyed by persona index.</param>
/// <param name="HiddenPositions">Persona indexes starting disabled (hidden at rest).</param>
public sealed record ChallengeMedals(
    IReadOnlyDictionary<int, string> MedalsByPosition,
    IReadOnlySet<int> HiddenPositions)
{
    /// <summary>
    /// Gets an empty roster (no medals, nothing hidden).
    /// </summary>
    public static ChallengeMedals Empty { get; } = new(
        new Dictionary<int, string>(),
        new HashSet<int>());
}
