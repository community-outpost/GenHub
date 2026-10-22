using GenHub.Core.Constants;
using System;

namespace GenHub.Core.Models.Tools.WndEditor;

/// <summary>
/// A SLIDERDATA property value.
/// </summary>
public sealed record WndSliderData
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WndSliderData"/> class.
    /// </summary>
    /// <param name="minValue">The minimum value.</param>
    /// <param name="maxValue">The maximum value.</param>
    public WndSliderData(int minValue, int maxValue)
    {
        MinValue = minValue;
        MaxValue = maxValue;
    }

    /// <summary>
    /// Gets the minimum value.
    /// </summary>
    public int MinValue { get; }

    /// <summary>
    /// Gets the maximum value.
    /// </summary>
    public int MaxValue { get; }

    /// <summary>
    /// Tries to parse a SLIDERDATA property value.
    /// </summary>
    /// <param name="value">The raw property value.</param>
    /// <param name="data">The parsed data when successful.</param>
    /// <returns>True when the value parsed successfully.</returns>
    public static bool TryParse(string? value, out WndSliderData? data)
    {
        data = null;
        var tokens = WndValueTokenizer.SplitTokens(value);
        if (tokens.Count != 4
            || !string.Equals(tokens[0], WndConstants.GadgetDataKeys.MinValue, StringComparison.OrdinalIgnoreCase)
            || !int.TryParse(tokens[1], out var minValue)
            || !string.Equals(tokens[2], WndConstants.GadgetDataKeys.MaxValue, StringComparison.OrdinalIgnoreCase)
            || !int.TryParse(tokens[3], out var maxValue))
        {
            return false;
        }

        data = new WndSliderData(minValue, maxValue);
        return true;
    }

    /// <summary>
    /// Returns the canonical single-line representation of this data.
    /// </summary>
    /// <returns>The canonical representation.</returns>
    public override string ToString()
    {
        return WndValueFormatter.JoinPairs(
            (WndConstants.GadgetDataKeys.MinValue, MinValue.ToString()),
            (WndConstants.GadgetDataKeys.MaxValue, MaxValue.ToString()));
    }
}
