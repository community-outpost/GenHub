using GenHub.Core.Constants;
using System;
using System.Globalization;

namespace GenHub.Features.Tools.RmlEditor.Services;

/// <summary>
/// A parsed style sheet length.
/// </summary>
/// <param name="Value">The numeric value.</param>
/// <param name="Unit">The unit of measurement.</param>
public readonly record struct RmlLength(double Value, RmlLengthUnit Unit)
{
    /// <summary>
    /// Tries to parse a style sheet length such as 12dp, 50%, or auto.
    /// </summary>
    /// <param name="text">The raw value.</param>
    /// <param name="baseFontSize">The font size used for relative units.</param>
    /// <param name="length">The parsed length.</param>
    /// <returns>True when parsing succeeded.</returns>
    public static bool TryParse(string? text, double baseFontSize, out RmlLength length)
    {
        length = new RmlLength(0, RmlLengthUnit.Auto);
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        text = text.Trim();
        if (string.Equals(text, RmlConstants.Values.Auto, StringComparison.OrdinalIgnoreCase))
        {
            length = new RmlLength(0, RmlLengthUnit.Auto);
            return true;
        }

        if (text.EndsWith(RmlConstants.Units.Percent, StringComparison.Ordinal))
        {
            if (TryParseNumber(text.Substring(0, text.Length - 1), out var percent))
            {
                length = new RmlLength(percent, RmlLengthUnit.Percent);
                return true;
            }

            return false;
        }

        var multiplier = 1.0;
        var number = text;
        if (TryStripUnit(ref number, RmlConstants.Units.Dp) || TryStripUnit(ref number, RmlConstants.Units.Px))
        {
            multiplier = 1.0;
        }
        else if (TryStripUnit(ref number, RmlConstants.Units.Pt))
        {
            multiplier = 96.0 / 72.0;
        }
        else if (TryStripUnit(ref number, RmlConstants.Units.Em) || TryStripUnit(ref number, RmlConstants.Units.Rem))
        {
            multiplier = baseFontSize;
        }

        if (TryParseNumber(number, out var pixels))
        {
            length = new RmlLength(pixels * multiplier, RmlLengthUnit.Px);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Resolves the length to pixels.
    /// </summary>
    /// <param name="baseFontSize">The font size used for relative units.</param>
    /// <param name="percentBase">The base used for percentage values.</param>
    /// <returns>The pixel value, or null when automatic.</returns>
    public double? ToPixels(double baseFontSize, double percentBase)
    {
        return Unit switch
        {
            RmlLengthUnit.Px => Value,
            RmlLengthUnit.Percent => (Value / 100.0) * percentBase,
            RmlLengthUnit.Auto => null,
            _ => null,
        };
    }

    private static bool TryStripUnit(ref string text, string unit)
    {
        if (!text.EndsWith(unit, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        text = text[..^unit.Length];
        return true;
    }

    private static bool TryParseNumber(string text, out double value)
    {
        return double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
