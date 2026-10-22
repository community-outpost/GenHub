using GenHub.Core.Constants;
using System;

namespace GenHub.Core.Models.Tools.WndEditor;

/// <summary>
/// A TEXTENTRYDATA property value.
/// </summary>
public sealed record WndTextEntryData
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WndTextEntryData"/> class.
    /// </summary>
    /// <param name="maxLen">The maximum text length.</param>
    /// <param name="secretText">Whether entered text is masked.</param>
    /// <param name="numericalOnly">Whether only numbers are accepted.</param>
    /// <param name="alphaNumericalOnly">Whether only letters and numbers are accepted.</param>
    /// <param name="asciiOnly">Whether only ASCII characters are accepted.</param>
    public WndTextEntryData(int maxLen, bool secretText, bool numericalOnly, bool alphaNumericalOnly, bool asciiOnly)
    {
        MaxLen = maxLen;
        SecretText = secretText;
        NumericalOnly = numericalOnly;
        AlphaNumericalOnly = alphaNumericalOnly;
        AsciiOnly = asciiOnly;
    }

    /// <summary>
    /// Gets the maximum text length.
    /// </summary>
    public int MaxLen { get; }

    /// <summary>
    /// Gets a value indicating whether entered text is masked.
    /// </summary>
    public bool SecretText { get; }

    /// <summary>
    /// Gets a value indicating whether only numbers are accepted.
    /// </summary>
    public bool NumericalOnly { get; }

    /// <summary>
    /// Gets a value indicating whether only letters and numbers are accepted.
    /// </summary>
    public bool AlphaNumericalOnly { get; }

    /// <summary>
    /// Gets a value indicating whether only ASCII characters are accepted.
    /// </summary>
    public bool AsciiOnly { get; }

    /// <summary>
    /// Tries to parse a TEXTENTRYDATA property value.
    /// </summary>
    /// <param name="value">The raw property value.</param>
    /// <param name="data">The parsed data when successful.</param>
    /// <returns>True when the value parsed successfully.</returns>
    public static bool TryParse(string? value, out WndTextEntryData? data)
    {
        data = null;
        var tokens = WndValueTokenizer.SplitTokens(value);
        if (tokens.Count != 10
            || !string.Equals(tokens[0], WndConstants.GadgetDataKeys.MaxLen, StringComparison.OrdinalIgnoreCase)
            || !int.TryParse(tokens[1], out var maxLen)
            || !string.Equals(tokens[2], WndConstants.GadgetDataKeys.SecretText, StringComparison.OrdinalIgnoreCase)
            || !WndValueTokenizer.TryParseBool(tokens[3], out var secretText)
            || !string.Equals(tokens[4], WndConstants.GadgetDataKeys.NumericalOnly, StringComparison.OrdinalIgnoreCase)
            || !WndValueTokenizer.TryParseBool(tokens[5], out var numericalOnly)
            || !string.Equals(tokens[6], WndConstants.GadgetDataKeys.AlphaNumericalOnly, StringComparison.OrdinalIgnoreCase)
            || !WndValueTokenizer.TryParseBool(tokens[7], out var alphaNumericalOnly)
            || !string.Equals(tokens[8], WndConstants.GadgetDataKeys.AsciiOnly, StringComparison.OrdinalIgnoreCase)
            || !WndValueTokenizer.TryParseBool(tokens[9], out var asciiOnly))
        {
            return false;
        }

        data = new WndTextEntryData(maxLen, secretText, numericalOnly, alphaNumericalOnly, asciiOnly);
        return true;
    }

    /// <summary>
    /// Returns the canonical single-line representation of this data.
    /// </summary>
    /// <returns>The canonical representation.</returns>
    public override string ToString()
    {
        return WndValueFormatter.JoinPairs(
            (WndConstants.GadgetDataKeys.MaxLen, MaxLen.ToString()),
            (WndConstants.GadgetDataKeys.SecretText, WndValueTokenizer.FormatBool(SecretText)),
            (WndConstants.GadgetDataKeys.NumericalOnly, WndValueTokenizer.FormatBool(NumericalOnly)),
            (WndConstants.GadgetDataKeys.AlphaNumericalOnly, WndValueTokenizer.FormatBool(AlphaNumericalOnly)),
            (WndConstants.GadgetDataKeys.AsciiOnly, WndValueTokenizer.FormatBool(AsciiOnly)));
    }
}
