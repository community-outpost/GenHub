using GenHub.Core.Constants;
using System;

namespace GenHub.Core.Models.Tools.WndEditor;

/// <summary>
/// A COMBOBOXDATA property value.
/// </summary>
public sealed record WndComboBoxData
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WndComboBoxData"/> class.
    /// </summary>
    /// <param name="isEditable">Whether the edit box accepts input.</param>
    /// <param name="maxChars">The maximum character count.</param>
    /// <param name="maxDisplay">The maximum displayed entries before scrolling.</param>
    /// <param name="asciiOnly">Whether only ASCII characters are accepted.</param>
    /// <param name="lettersAndNumbersOnly">Whether only letters and numbers are accepted.</param>
    public WndComboBoxData(bool isEditable, int maxChars, int maxDisplay, bool asciiOnly, bool lettersAndNumbersOnly)
    {
        IsEditable = isEditable;
        MaxChars = maxChars;
        MaxDisplay = maxDisplay;
        AsciiOnly = asciiOnly;
        LettersAndNumbersOnly = lettersAndNumbersOnly;
    }

    /// <summary>
    /// Gets a value indicating whether the edit box accepts input.
    /// </summary>
    public bool IsEditable { get; }

    /// <summary>
    /// Gets the maximum character count.
    /// </summary>
    public int MaxChars { get; }

    /// <summary>
    /// Gets the maximum displayed entries before scrolling.
    /// </summary>
    public int MaxDisplay { get; }

    /// <summary>
    /// Gets a value indicating whether only ASCII characters are accepted.
    /// </summary>
    public bool AsciiOnly { get; }

    /// <summary>
    /// Gets a value indicating whether only letters and numbers are accepted.
    /// </summary>
    public bool LettersAndNumbersOnly { get; }

    /// <summary>
    /// Tries to parse a COMBOBOXDATA property value.
    /// </summary>
    /// <param name="value">The raw property value.</param>
    /// <param name="data">The parsed data when successful.</param>
    /// <returns>True when the value parsed successfully.</returns>
    public static bool TryParse(string? value, out WndComboBoxData? data)
    {
        data = null;
        var tokens = WndValueTokenizer.SplitTokens(value);
        if (tokens.Count != 10
            || !string.Equals(tokens[0], WndConstants.GadgetDataKeys.IsEditable, StringComparison.OrdinalIgnoreCase)
            || !WndValueTokenizer.TryParseBool(tokens[1], out var isEditable)
            || !string.Equals(tokens[2], WndConstants.GadgetDataKeys.MaxChars, StringComparison.OrdinalIgnoreCase)
            || !int.TryParse(tokens[3], out var maxChars)
            || !string.Equals(tokens[4], WndConstants.GadgetDataKeys.MaxDisplay, StringComparison.OrdinalIgnoreCase)
            || !int.TryParse(tokens[5], out var maxDisplay)
            || !string.Equals(tokens[6], WndConstants.GadgetDataKeys.AsciiOnly, StringComparison.OrdinalIgnoreCase)
            || !WndValueTokenizer.TryParseBool(tokens[7], out var asciiOnly)
            || (!string.Equals(tokens[8], WndConstants.GadgetDataKeys.LettersAndNumbersOnly, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(tokens[8], "LETTERSANDNUMBERSONLY", StringComparison.OrdinalIgnoreCase))
            || !WndValueTokenizer.TryParseBool(tokens[9], out var lettersAndNumbersOnly))
        {
            return false;
        }

        data = new WndComboBoxData(isEditable, maxChars, maxDisplay, asciiOnly, lettersAndNumbersOnly);
        return true;
    }

    /// <summary>
    /// Returns the canonical single-line representation of this data.
    /// </summary>
    /// <returns>The canonical representation.</returns>
    public override string ToString()
    {
        return WndValueFormatter.JoinPairs(
            (WndConstants.GadgetDataKeys.IsEditable, WndValueTokenizer.FormatBool(IsEditable)),
            (WndConstants.GadgetDataKeys.MaxChars, MaxChars.ToString()),
            (WndConstants.GadgetDataKeys.MaxDisplay, MaxDisplay.ToString()),
            (WndConstants.GadgetDataKeys.AsciiOnly, WndValueTokenizer.FormatBool(AsciiOnly)),
            (WndConstants.GadgetDataKeys.LettersAndNumbersOnly, WndValueTokenizer.FormatBool(LettersAndNumbersOnly)));
    }
}
