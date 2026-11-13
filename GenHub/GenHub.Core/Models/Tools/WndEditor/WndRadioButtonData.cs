using GenHub.Core.Constants;
using System;

namespace GenHub.Core.Models.Tools.WndEditor;

/// <summary>
/// A RADIOBUTTONDATA property value.
/// </summary>
public sealed record WndRadioButtonData
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WndRadioButtonData"/> class.
    /// </summary>
    /// <param name="group">The radio group identifier.</param>
    public WndRadioButtonData(int group)
    {
        Group = group;
    }

    /// <summary>
    /// Gets the radio group identifier.
    /// </summary>
    public int Group { get; }

    /// <summary>
    /// Tries to parse a RADIOBUTTONDATA property value.
    /// </summary>
    /// <param name="value">The raw property value.</param>
    /// <param name="data">The parsed data when successful.</param>
    /// <returns>True when the value parsed successfully.</returns>
    public static bool TryParse(string? value, out WndRadioButtonData? data)
    {
        data = null;
        var tokens = WndValueTokenizer.SplitTokens(value);
        if (tokens.Count != 2
            || !string.Equals(tokens[0], WndConstants.GadgetDataKeys.Group, StringComparison.OrdinalIgnoreCase)
            || !int.TryParse(tokens[1], out var group))
        {
            return false;
        }

        data = new WndRadioButtonData(group);
        return true;
    }

    /// <summary>
    /// Returns the canonical single-line representation of this data.
    /// </summary>
    /// <returns>The canonical representation.</returns>
    public override string ToString()
    {
        return WndValueFormatter.Pair(WndConstants.GadgetDataKeys.Group, Group.ToString());
    }
}
