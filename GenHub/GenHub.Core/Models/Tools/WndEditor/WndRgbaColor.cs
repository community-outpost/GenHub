using System;
using System.Collections.Generic;

namespace GenHub.Core.Models.Tools.WndEditor;

/// <summary>
/// A red/green/blue/alpha color component block parsed from a property value.
/// </summary>
public sealed record WndRgbaColor
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WndRgbaColor"/> class.
    /// </summary>
    /// <param name="red">The red channel, 0 to 255.</param>
    /// <param name="green">The green channel, 0 to 255.</param>
    /// <param name="blue">The blue channel, 0 to 255.</param>
    /// <param name="alpha">The alpha channel, 0 to 255.</param>
    public WndRgbaColor(int red, int green, int blue, int alpha)
    {
        Red = red;
        Green = green;
        Blue = blue;
        Alpha = alpha;
    }

    /// <summary>
    /// Gets the red channel.
    /// </summary>
    public int Red { get; }

    /// <summary>
    /// Gets the green channel.
    /// </summary>
    public int Green { get; }

    /// <summary>
    /// Gets the blue channel.
    /// </summary>
    public int Blue { get; }

    /// <summary>
    /// Gets the alpha channel.
    /// </summary>
    public int Alpha { get; }

    /// <summary>
    /// Gets opaque white.
    /// </summary>
    public static WndRgbaColor White => new(255, 255, 255, 255);

    /// <summary>
    /// Tries to read four channel tokens starting at the given index.
    /// </summary>
    /// <param name="tokens">The token stream.</param>
    /// <param name="index">The first channel token index.</param>
    /// <param name="color">The parsed color when successful.</param>
    /// <returns>True when four valid channels were read.</returns>
    public static bool TryParseTokens(IReadOnlyList<string> tokens, int index, out WndRgbaColor? color)
    {
        color = null;
        if (index < 0 || index + 3 >= tokens.Count)
        {
            return false;
        }

        if (!TryParseChannel(tokens[index], out var red)
            || !TryParseChannel(tokens[index + 1], out var green)
            || !TryParseChannel(tokens[index + 2], out var blue)
            || !TryParseChannel(tokens[index + 3], out var alpha))
        {
            return false;
        }

        color = new WndRgbaColor(red, green, blue, alpha);
        return true;
    }

    /// <summary>
    /// Returns the canonical space-separated channel representation.
    /// </summary>
    /// <returns>The canonical representation.</returns>
    public override string ToString()
    {
        return string.Concat(Red, " ", Green, " ", Blue, " ", Alpha);
    }

    private static bool TryParseChannel(string token, out int channel)
    {
        if (int.TryParse(token, out channel) && channel >= 0 && channel <= 255)
        {
            return true;
        }

        channel = 0;
        return false;
    }
}
