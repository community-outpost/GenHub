using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace GenHub.Features.Tools.RmlEditor.Services;

/// <summary>
/// Parses style sheet color values into Avalonia colors.
/// </summary>
public static class RmlColors
{
    private static readonly Dictionary<string, Color> NamedColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["black"] = Colors.Black,
        ["white"] = Colors.White,
        ["red"] = Colors.Red,
        ["green"] = Colors.Green,
        ["lime"] = Colors.Lime,
        ["blue"] = Colors.Blue,
        ["yellow"] = Colors.Yellow,
        ["cyan"] = Colors.Cyan,
        ["aqua"] = Colors.Aqua,
        ["magenta"] = Colors.Magenta,
        ["fuchsia"] = Colors.Fuchsia,
        ["gray"] = Colors.Gray,
        ["grey"] = Colors.Gray,
        ["silver"] = Colors.Silver,
        ["maroon"] = Colors.Maroon,
        ["olive"] = Colors.Olive,
        ["navy"] = Colors.Navy,
        ["purple"] = Colors.Purple,
        ["teal"] = Colors.Teal,
        ["orange"] = Colors.Orange,
        ["gold"] = Colors.Gold,
        ["brown"] = Colors.Brown,
        ["pink"] = Colors.Pink,
        ["violet"] = Colors.Violet,
        ["indigo"] = Colors.Indigo,
        ["turquoise"] = Colors.Turquoise,
        ["beige"] = Colors.Beige,
        ["ivory"] = Colors.Ivory,
        ["khaki"] = Colors.Khaki,
        ["salmon"] = Colors.Salmon,
        ["coral"] = Colors.Coral,
        ["tomato"] = Colors.Tomato,
        ["crimson"] = Colors.Crimson,
        ["darkred"] = Colors.DarkRed,
        ["darkgreen"] = Colors.DarkGreen,
        ["darkblue"] = Colors.DarkBlue,
        ["darkgray"] = Colors.DarkGray,
        ["darkgrey"] = Colors.DarkGray,
        ["lightgray"] = Colors.LightGray,
        ["lightgrey"] = Colors.LightGray,
        ["dimgray"] = Colors.DimGray,
        ["dimgrey"] = Colors.DimGray,
        ["transparent"] = Colors.Transparent,
    };

    /// <summary>
    /// Tries to parse a style sheet color value.
    /// </summary>
    /// <param name="text">The raw value.</param>
    /// <param name="color">The parsed color.</param>
    /// <returns>True when parsing succeeded.</returns>
    public static bool TryParse(string? text, out Color color)
    {
        color = Colors.Transparent;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        text = text.Trim();
        if (NamedColors.TryGetValue(text, out var named))
        {
            color = named;
            return true;
        }

        if (text.StartsWith('#'))
        {
            return TryParseHex(text.Substring(1), out color);
        }

        if (text.StartsWith("rgba(", StringComparison.OrdinalIgnoreCase) && text.EndsWith(')'))
        {
            return TryParseChannels(text.Substring(5, text.Length - 6), hasAlpha: true, out color);
        }

        if (text.StartsWith("rgb(", StringComparison.OrdinalIgnoreCase) && text.EndsWith(')'))
        {
            return TryParseChannels(text.Substring(4, text.Length - 5), hasAlpha: false, out color);
        }

        return Color.TryParse(text, out color);
    }

    private static bool TryParseHex(string hex, out Color color)
    {
        color = Colors.Transparent;
        if (hex.Length is not (3 or 4 or 6 or 8))
        {
            return false;
        }

        try
        {
            if (hex.Length is 3 or 4)
            {
                var expanded = new char[hex.Length * 2];
                for (var i = 0; i < hex.Length; i++)
                {
                    expanded[i * 2] = hex[i];
                    expanded[(i * 2) + 1] = hex[i];
                }

                hex = new string(expanded);
            }

            var value = uint.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            if (hex.Length == 6)
            {
                color = Color.FromRgb((byte)(value >> 16), (byte)(value >> 8), (byte)value);
                return true;
            }

            color = Color.FromArgb((byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static bool TryParseChannels(string body, bool hasAlpha, out Color color)
    {
        color = Colors.Transparent;
        var parts = body.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != (hasAlpha ? 4 : 3))
        {
            return false;
        }

        if (!TryParseChannel(parts[0], out var red) || !TryParseChannel(parts[1], out var green) || !TryParseChannel(parts[2], out var blue))
        {
            return false;
        }

        var alpha = (byte)255;
        if (hasAlpha && !TryParseAlpha(parts[3], out alpha))
        {
            return false;
        }

        color = Color.FromArgb(alpha, red, green, blue);
        return true;
    }

    private static bool TryParseChannel(string text, out byte channel)
    {
        channel = 0;
        text = text.Trim();
        if (text.EndsWith('%'))
        {
            if (double.TryParse(text.Substring(0, text.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var percent))
            {
                channel = (byte)Math.Clamp(Math.Round((percent / 100.0) * 255.0), 0, 255);
                return true;
            }

            return false;
        }

        if (byte.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out channel))
        {
            return true;
        }

        return false;
    }

    private static bool TryParseAlpha(string text, out byte alpha)
    {
        alpha = 0;
        text = text.Trim();
        if (text.EndsWith('%'))
        {
            return TryParseChannel(text, out alpha);
        }

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            alpha = (byte)Math.Clamp(Math.Round(value * 255.0), 0, 255);
            return true;
        }

        return false;
    }
}
