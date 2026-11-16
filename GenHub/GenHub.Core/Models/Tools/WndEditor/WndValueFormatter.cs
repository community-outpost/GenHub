using GenHub.Core.Constants;
using System.Collections.Generic;
using System.Linq;

namespace GenHub.Core.Models.Tools.WndEditor;

/// <summary>
/// Formats window definition property values in canonical engine spelling.
/// </summary>
internal static class WndValueFormatter
{
    /// <summary>
    /// Formats one labeled component.
    /// </summary>
    /// <param name="label">The component label.</param>
    /// <param name="value">The component value.</param>
    /// <returns>The formatted component.</returns>
    public static string Pair(string label, string value)
    {
        return string.Concat(label, WndConstants.Syntax.CoordinateSeparator, " ", value);
    }

    /// <summary>
    /// Joins labeled components with the engine component separator.
    /// </summary>
    /// <param name="pairs">The label/value pairs.</param>
    /// <returns>The joined value.</returns>
    public static string JoinPairs(params (string Label, string Value)[] pairs)
    {
        return JoinParts(pairs.Select(pair => Pair(pair.Label, pair.Value)));
    }

    /// <summary>
    /// Joins raw components with the engine component separator.
    /// </summary>
    /// <param name="parts">The components.</param>
    /// <returns>The joined value.</returns>
    public static string JoinParts(IEnumerable<string> parts)
    {
        return string.Join(WndConstants.Syntax.ComponentListSeparator, parts);
    }
}
