using GenHub.Core.Constants;
using System;
using System.Collections.Generic;

namespace GenHub.Core.Models.Tools.WndEditor;

/// <summary>
/// A single draw data entry: an image name with color and border color.
/// </summary>
public sealed record WndDrawDataEntry
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WndDrawDataEntry"/> class.
    /// </summary>
    /// <param name="image">The mapped image name, or NoImage when empty.</param>
    /// <param name="color">The tint color.</param>
    /// <param name="borderColor">The border color.</param>
    public WndDrawDataEntry(string image, WndRgbaColor color, WndRgbaColor borderColor)
    {
        Image = image;
        Color = color;
        BorderColor = borderColor;
    }

    /// <summary>
    /// Gets the mapped image name.
    /// </summary>
    public string Image { get; }

    /// <summary>
    /// Gets the tint color.
    /// </summary>
    public WndRgbaColor Color { get; }

    /// <summary>
    /// Gets the border color.
    /// </summary>
    public WndRgbaColor BorderColor { get; }

    /// <summary>
    /// Gets a value indicating whether this entry carries no image.
    /// </summary>
    public bool IsEmpty => string.Equals(Image, WndConstants.DrawData.NoImage, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Gets an empty entry.
    /// </summary>
    public static WndDrawDataEntry Empty => new(WndConstants.DrawData.NoImage, WndRgbaColor.White, WndRgbaColor.White);

    /// <summary>
    /// Tries to read one entry starting at the given token index.
    /// </summary>
    /// <param name="tokens">The token stream.</param>
    /// <param name="index">The IMAGE label token index.</param>
    /// <param name="entry">The parsed entry when successful.</param>
    /// <returns>True when a full entry was read.</returns>
    public static bool TryParseTokens(IReadOnlyList<string> tokens, int index, out WndDrawDataEntry? entry)
    {
        entry = null;
        if (index < 0 || index + 12 > tokens.Count)
        {
            return false;
        }

        if (!string.Equals(tokens[index], WndConstants.DrawData.ImageLabel, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(tokens[index + 2], WndConstants.DrawData.ColorLabel, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(tokens[index + 7], WndConstants.DrawData.BorderColorLabel, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!WndRgbaColor.TryParseTokens(tokens, index + 3, out var color) || color == null)
        {
            return false;
        }

        if (!WndRgbaColor.TryParseTokens(tokens, index + 8, out var borderColor) || borderColor == null)
        {
            return false;
        }

        entry = new WndDrawDataEntry(tokens[index + 1], color, borderColor);
        return true;
    }

    /// <summary>
    /// Returns the canonical single-line representation of this entry.
    /// </summary>
    /// <returns>The canonical representation.</returns>
    public override string ToString()
    {
        return WndValueFormatter.JoinPairs(
            (WndConstants.DrawData.ImageLabel, Image),
            (WndConstants.DrawData.ColorLabel, Color.ToString()),
            (WndConstants.DrawData.BorderColorLabel, BorderColor.ToString()));
    }
}
