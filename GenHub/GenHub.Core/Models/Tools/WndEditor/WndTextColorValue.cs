using GenHub.Core.Constants;
using System;
using System.Collections.Generic;

namespace GenHub.Core.Models.Tools.WndEditor;

/// <summary>
/// A TEXTCOLOR property value: enabled, disabled, and hilite text colors with border colors.
/// </summary>
public sealed record WndTextColorValue
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WndTextColorValue"/> class.
    /// </summary>
    /// <param name="enabled">The enabled text color.</param>
    /// <param name="enabledBorder">The enabled text border color.</param>
    /// <param name="disabled">The disabled text color.</param>
    /// <param name="disabledBorder">The disabled text border color.</param>
    /// <param name="hilite">The hilite text color.</param>
    /// <param name="hiliteBorder">The hilite text border color.</param>
    public WndTextColorValue(
        WndRgbaColor enabled,
        WndRgbaColor enabledBorder,
        WndRgbaColor disabled,
        WndRgbaColor disabledBorder,
        WndRgbaColor hilite,
        WndRgbaColor hiliteBorder)
    {
        Enabled = enabled;
        EnabledBorder = enabledBorder;
        Disabled = disabled;
        DisabledBorder = disabledBorder;
        Hilite = hilite;
        HiliteBorder = hiliteBorder;
    }

    /// <summary>
    /// Gets the enabled text color.
    /// </summary>
    public WndRgbaColor Enabled { get; }

    /// <summary>
    /// Gets the enabled text border color.
    /// </summary>
    public WndRgbaColor EnabledBorder { get; }

    /// <summary>
    /// Gets the disabled text color.
    /// </summary>
    public WndRgbaColor Disabled { get; }

    /// <summary>
    /// Gets the disabled text border color.
    /// </summary>
    public WndRgbaColor DisabledBorder { get; }

    /// <summary>
    /// Gets the hilite text color.
    /// </summary>
    public WndRgbaColor Hilite { get; }

    /// <summary>
    /// Gets the hilite text border color.
    /// </summary>
    public WndRgbaColor HiliteBorder { get; }

    /// <summary>
    /// Tries to parse a TEXTCOLOR property value.
    /// </summary>
    /// <param name="value">The raw property value.</param>
    /// <param name="textColor">The parsed text color when successful.</param>
    /// <returns>True when the value parsed successfully.</returns>
    public static bool TryParse(string? value, out WndTextColorValue? textColor)
    {
        textColor = null;
        var tokens = WndValueTokenizer.SplitTokens(value);
        var labels = new[]
        {
            WndConstants.TextColorKeys.Enabled,
            WndConstants.TextColorKeys.EnabledBorder,
            WndConstants.TextColorKeys.Disabled,
            WndConstants.TextColorKeys.DisabledBorder,
            WndConstants.TextColorKeys.Hilite,
            WndConstants.TextColorKeys.HiliteBorder,
        };
        if (tokens.Count != labels.Length * 5)
        {
            return false;
        }

        var colors = new List<WndRgbaColor>(labels.Length);
        for (var i = 0; i < labels.Length; i++)
        {
            var offset = i * 5;
            if (!string.Equals(tokens[offset], labels[i], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!WndRgbaColor.TryParseTokens(tokens, offset + 1, out var color) || color == null)
            {
                return false;
            }

            colors.Add(color);
        }

        textColor = new WndTextColorValue(colors[0], colors[1], colors[2], colors[3], colors[4], colors[5]);
        return true;
    }

    /// <summary>
    /// Returns the canonical single-line representation of this text color.
    /// </summary>
    /// <returns>The canonical representation.</returns>
    public override string ToString()
    {
        return WndValueFormatter.JoinPairs(
            (WndConstants.TextColorKeys.Enabled, Enabled.ToString()),
            (WndConstants.TextColorKeys.EnabledBorder, EnabledBorder.ToString()),
            (WndConstants.TextColorKeys.Disabled, Disabled.ToString()),
            (WndConstants.TextColorKeys.DisabledBorder, DisabledBorder.ToString()),
            (WndConstants.TextColorKeys.Hilite, Hilite.ToString()),
            (WndConstants.TextColorKeys.HiliteBorder, HiliteBorder.ToString()));
    }
}
