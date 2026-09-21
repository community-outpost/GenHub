namespace GenHub.Core.Models.Tools.WndEditor;

/// <summary>
/// An IMAGEOFFSET property value: a plain X/Y image draw offset.
/// </summary>
public sealed record WndImageOffset
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WndImageOffset"/> class.
    /// </summary>
    /// <param name="x">The horizontal offset.</param>
    /// <param name="y">The vertical offset.</param>
    public WndImageOffset(int x, int y)
    {
        X = x;
        Y = y;
    }

    /// <summary>
    /// Gets the horizontal offset.
    /// </summary>
    public int X { get; }

    /// <summary>
    /// Gets the vertical offset.
    /// </summary>
    public int Y { get; }

    /// <summary>
    /// Tries to parse an IMAGEOFFSET property value.
    /// </summary>
    /// <param name="value">The raw property value.</param>
    /// <param name="offset">The parsed offset when successful.</param>
    /// <returns>True when the value parsed successfully.</returns>
    public static bool TryParse(string? value, out WndImageOffset? offset)
    {
        offset = null;
        var tokens = WndValueTokenizer.SplitTokens(value);
        if (tokens.Count != 2 || !int.TryParse(tokens[0], out var x) || !int.TryParse(tokens[1], out var y))
        {
            return false;
        }

        offset = new WndImageOffset(x, y);
        return true;
    }

    /// <summary>
    /// Returns the canonical single-line representation of this offset.
    /// </summary>
    /// <returns>The canonical representation.</returns>
    public override string ToString()
    {
        return string.Concat(X, " ", Y);
    }
}
