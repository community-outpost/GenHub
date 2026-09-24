using System.Globalization;
using GenHub.Core.Constants;
using System;

namespace GenHub.Core.Models.Tools.WndEditor;

/// <summary>
/// A FONT property value: NAME, SIZE, and BOLD components.
/// </summary>
public sealed record WndFontValue
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WndFontValue"/> class.
    /// </summary>
    /// <param name="name">The font name.</param>
    /// <param name="size">The font size.</param>
    /// <param name="bold">Whether the font is bold.</param>
    public WndFontValue(string name, int size, bool bold)
    {
        Name = name;
        Size = size;
        Bold = bold;
    }

    /// <summary>
    /// Gets the font name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the font size.
    /// </summary>
    public int Size { get; }

    /// <summary>
    /// Gets a value indicating whether the font is bold.
    /// </summary>
    public bool Bold { get; }

    /// <summary>
    /// Tries to parse a FONT property value.
    /// </summary>
    /// <param name="value">The raw property value.</param>
    /// <param name="font">The parsed font when successful.</param>
    /// <returns>True when the value parsed successfully.</returns>
    public static bool TryParse(string? value, out WndFontValue? font)
    {
        font = null;
        var tokens = WndValueTokenizer.SplitTokens(value);
        if (tokens.Count != 6)
        {
            return false;
        }

        if (!string.Equals(tokens[0], WndConstants.FontKeys.Name, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(tokens[2], WndConstants.FontKeys.Size, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(tokens[4], WndConstants.FontKeys.Bold, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!int.TryParse(tokens[3], out var size) || !int.TryParse(tokens[5], out var bold))
        {
            return false;
        }

        font = new WndFontValue(WndValueTokenizer.Unquote(tokens[1]), size, bold != 0);
        return true;
    }

    /// <summary>
    /// Returns the canonical single-line representation of this font.
    /// </summary>
    /// <returns>The canonical representation.</returns>
    public override string ToString()
    {
        return WndValueFormatter.JoinPairs(
            (WndConstants.FontKeys.Name, WndValueTokenizer.Quote(Name)),
            (WndConstants.FontKeys.Size, Size.ToString(CultureInfo.InvariantCulture)),
            (WndConstants.FontKeys.Bold, Bold ? "1" : "0"));
    }
}
