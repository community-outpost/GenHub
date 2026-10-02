namespace GenHub.Core.Services.Tools.WorldBuilder;

/// <summary>
/// Mutable generation workspace: height bytes plus base and cliff flags.
/// </summary>
public sealed class MapGenField
{
    private readonly byte[] heights;
    private readonly byte[] flags;

    /// <summary>
    /// Initializes a new instance of the <see cref="MapGenField"/> class.
    /// </summary>
    /// <param name="width">Width in cells.</param>
    /// <param name="height">Height in cells.</param>
    /// <param name="initial">Initial height for every cell.</param>
    public MapGenField(int width, int height, byte initial)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        Width = width;
        Height = height;
        heights = new byte[width * height];
        Array.Fill(heights, initial);
        flags = new byte[width * height];
    }

    /// <summary>Gets the width in cells.</summary>
    public int Width { get; }

    /// <summary>Gets the height in cells.</summary>
    public int Height { get; }

    /// <summary>Gets the height bytes.</summary>
    public IList<byte> Heights => heights;

    /// <summary>Checks cell bounds.</summary>
    /// <param name="x">X cell.</param>
    /// <param name="y">Y cell.</param>
    /// <returns>True when inside the field.</returns>
    public bool IsValid(int x, int y)
    {
        return x >= 0 && y >= 0 && x < Width && y < Height;
    }

    /// <summary>Reads a height byte.</summary>
    /// <param name="x">X cell.</param>
    /// <param name="y">Y cell.</param>
    /// <returns>The height, or 0 outside the field.</returns>
    public byte GetHeight(int x, int y)
    {
        return IsValid(x, y) ? heights[(y * Width) + x] : (byte)0;
    }

    /// <summary>Writes a height byte.</summary>
    /// <param name="x">X cell.</param>
    /// <param name="y">Y cell.</param>
    /// <param name="value">The height.</param>
    public void SetHeight(int x, int y, byte value)
    {
        if (IsValid(x, y))
        {
            heights[(y * Width) + x] = value;
        }
    }

    /// <summary>Adds a signed delta with byte wraparound.</summary>
    /// <param name="x">X cell.</param>
    /// <param name="y">Y cell.</param>
    /// <param name="delta">The delta.</param>
    public void AddHeight(int x, int y, int delta)
    {
        if (IsValid(x, y))
        {
            heights[(y * Width) + x] = (byte)((heights[(y * Width) + x] + delta) & 0xFF);
        }
    }

    /// <summary>Checks the base-area flag.</summary>
    /// <param name="x">X cell.</param>
    /// <param name="y">Y cell.</param>
    /// <returns>True when inside a base area.</returns>
    public bool IsBase(int x, int y)
    {
        return IsValid(x, y) && (flags[(y * Width) + x] & 1) != 0;
    }

    /// <summary>Sets the base-area flag.</summary>
    /// <param name="x">X cell.</param>
    /// <param name="y">Y cell.</param>
    public void SetBase(int x, int y)
    {
        if (IsValid(x, y))
        {
            flags[(y * Width) + x] |= 1;
        }
    }

    /// <summary>Checks the cliff flag.</summary>
    /// <param name="x">X cell.</param>
    /// <param name="y">Y cell.</param>
    /// <returns>True when marked as cliff.</returns>
    public bool IsCliff(int x, int y)
    {
        return IsValid(x, y) && (flags[(y * Width) + x] & 2) != 0;
    }

    /// <summary>Sets the cliff flag.</summary>
    /// <param name="x">X cell.</param>
    /// <param name="y">Y cell.</param>
    public void SetCliff(int x, int y)
    {
        if (IsValid(x, y))
        {
            flags[(y * Width) + x] |= 2;
        }
    }
}
