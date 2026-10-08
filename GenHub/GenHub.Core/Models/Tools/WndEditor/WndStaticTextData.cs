using GenHub.Core.Constants;
using System;

namespace GenHub.Core.Models.Tools.WndEditor;

/// <summary>
/// A STATICTEXTDATA property value.
/// </summary>
public sealed record WndStaticTextData
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WndStaticTextData"/> class.
    /// </summary>
    /// <param name="centered">Whether the text is centered.</param>
    public WndStaticTextData(bool centered)
    {
        Centered = centered;
    }

    /// <summary>
    /// Gets a value indicating whether the text is centered.
    /// </summary>
    public bool Centered { get; }

    /// <summary>
    /// Tries to parse a STATICTEXTDATA property value.
    /// </summary>
    /// <param name="value">The raw property value.</param>
    /// <param name="data">The parsed data when successful.</param>
    /// <returns>True when the value parsed successfully.</returns>
    public static bool TryParse(string? value, out WndStaticTextData? data)
    {
        data = null;
        var tokens = WndValueTokenizer.SplitTokens(value);
        if (tokens.Count != 2
            || !string.Equals(tokens[0], WndConstants.GadgetDataKeys.Centered, StringComparison.OrdinalIgnoreCase)
            || !WndValueTokenizer.TryParseBool(tokens[1], out var centered))
        {
            return false;
        }

        data = new WndStaticTextData(centered);
        return true;
    }

    /// <summary>
    /// Returns the canonical single-line representation of this data.
    /// </summary>
    /// <returns>The canonical representation.</returns>
    public override string ToString()
    {
        return WndValueFormatter.Pair(WndConstants.GadgetDataKeys.Centered, WndValueTokenizer.FormatBool(Centered));
    }
}
