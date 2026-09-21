using GenHub.Core.Constants;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GenHub.Core.Models.Tools.WndEditor;

/// <summary>
/// Geometry of a window parsed from a SCREENRECT property value.
/// </summary>
public sealed record WndScreenRect
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WndScreenRect"/> class.
    /// </summary>
    /// <param name="upperLeftX">Upper-left X coordinate.</param>
    /// <param name="upperLeftY">Upper-left Y coordinate.</param>
    /// <param name="bottomRightX">Bottom-right X coordinate.</param>
    /// <param name="bottomRightY">Bottom-right Y coordinate.</param>
    /// <param name="creationWidth">Creation resolution width.</param>
    /// <param name="creationHeight">Creation resolution height.</param>
    public WndScreenRect(int upperLeftX, int upperLeftY, int bottomRightX, int bottomRightY, int creationWidth, int creationHeight)
    {
        UpperLeftX = upperLeftX;
        UpperLeftY = upperLeftY;
        BottomRightX = bottomRightX;
        BottomRightY = bottomRightY;
        CreationWidth = creationWidth;
        CreationHeight = creationHeight;
    }

    /// <summary>
    /// Gets the upper-left X coordinate.
    /// </summary>
    public int UpperLeftX { get; }

    /// <summary>
    /// Gets the upper-left Y coordinate.
    /// </summary>
    public int UpperLeftY { get; }

    /// <summary>
    /// Gets the bottom-right X coordinate.
    /// </summary>
    public int BottomRightX { get; }

    /// <summary>
    /// Gets the bottom-right Y coordinate.
    /// </summary>
    public int BottomRightY { get; }

    /// <summary>
    /// Gets the creation resolution width.
    /// </summary>
    public int CreationWidth { get; }

    /// <summary>
    /// Gets the creation resolution height.
    /// </summary>
    public int CreationHeight { get; }

    /// <summary>
    /// Gets the width derived from the corner coordinates.
    /// </summary>
    public int Width => BottomRightX - UpperLeftX;

    /// <summary>
    /// Gets the height derived from the corner coordinates.
    /// </summary>
    public int Height => BottomRightY - UpperLeftY;

    /// <summary>
    /// Tries to parse a SCREENRECT property value.
    /// </summary>
    /// <param name="value">The raw property value.</param>
    /// <param name="screenRect">The parsed rectangle when successful.</param>
    /// <returns>True when the value parsed successfully.</returns>
    public static bool TryParse(string? value, out WndScreenRect? screenRect)
    {
        screenRect = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var components = SplitComponents(value);
        if (components.Count != 3)
        {
            return false;
        }

        if (!TryParseComponent(components[0], WndConstants.ScreenRectKeys.UpperLeft, out var upperLeft)
            || !TryParseComponent(components[1], WndConstants.ScreenRectKeys.BottomRight, out var bottomRight)
            || !TryParseComponent(components[2], WndConstants.ScreenRectKeys.CreationResolution, out var creation))
        {
            return false;
        }

        screenRect = new WndScreenRect(upperLeft[0], upperLeft[1], bottomRight[0], bottomRight[1], creation[0], creation[1]);
        return true;
    }

    /// <summary>
    /// Returns the canonical single-line representation of this rectangle.
    /// </summary>
    /// <returns>The canonical representation.</returns>
    public override string ToString()
    {
        return string.Concat(
            WndConstants.ScreenRectKeys.UpperLeft,
            WndConstants.Syntax.CoordinateSeparator,
            " ",
            UpperLeftX,
            " ",
            UpperLeftY,
            WndConstants.Syntax.ComponentSeparator,
            " ",
            WndConstants.ScreenRectKeys.BottomRight,
            WndConstants.Syntax.CoordinateSeparator,
            " ",
            BottomRightX,
            " ",
            BottomRightY,
            WndConstants.Syntax.ComponentSeparator,
            " ",
            WndConstants.ScreenRectKeys.CreationResolution,
            WndConstants.Syntax.CoordinateSeparator,
            " ",
            CreationWidth,
            " ",
            CreationHeight);
    }

    private static List<string> SplitComponents(string value)
    {
        return value
            .Split(WndConstants.Syntax.ComponentSeparator)
            .Select(part => part.Trim())
            .Where(part => part.Length > 0)
            .ToList();
    }

    private static bool TryParseComponent(string component, string expectedKey, out int[] coordinates)
    {
        coordinates = [];
        var separatorIndex = component.IndexOf(WndConstants.Syntax.CoordinateSeparator, StringComparison.Ordinal);
        if (separatorIndex < 0)
        {
            return false;
        }

        var key = component.Substring(0, separatorIndex).Trim();
        if (!string.Equals(key, expectedKey, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var numbers = component
            .Substring(separatorIndex + 1)
            .Split((char[])[' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        if (numbers.Length != 2)
        {
            return false;
        }

        if (!int.TryParse(numbers[0], out var first) || !int.TryParse(numbers[1], out var second))
        {
            return false;
        }

        coordinates = [first, second];
        return true;
    }
}
