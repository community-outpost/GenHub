using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Tools.GenHotkeys;
using GenHub.Core.Models.Tools.GenHotkeys;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Tga;
using SixLabors.ImageSharp.PixelFormats;

namespace GenHub.Features.Tools.GenHotkeys.Services;

/// <summary>
/// Service for stamping hotkey badges onto unit/structure icons and encoding them as in-game TGAs.
/// </summary>
public class IconOverlayService(ILogger<IconOverlayService> logger) : IIconOverlayService
{
    private static readonly Rgba32 BadgeBackground = new(14, 18, 24, 248);
    private static readonly Rgba32 OuterBorderColor = new(8, 10, 14, 255);
    private static readonly Rgba32 InnerBorderColor = new(255, 185, 30, 255);
    private static readonly Rgba32 TextColor = new(255, 255, 255, 255);
    private static readonly Rgba32 EdgeColor = new(215, 225, 235, 220);
    private static readonly Rgba32 GlyphShadow = new(0, 0, 0, 240);
    private static readonly Rgba32 DropShadowColor = new(0, 0, 0, 150);

    // High-fidelity 8x10 bitmap font with anti-aliased edge smoothing and solid strokes.
    // Encoded as 10 rows of 8 pixels; each 2-bit pair represents 0=blank, 1=edge smoothing, 2=solid stroke.
    private static readonly Dictionary<char, ushort[]> Font8X10 = new()
    {
        ['A'] = [0x0AA0, 0x1AA4, 0x2008, 0x2008, 0x2AA8, 0x2008, 0x2008, 0x2008, 0x2008, 0x0000],
        ['B'] = [0x1AA8, 0x2008, 0x2008, 0x1AA8, 0x2008, 0x2008, 0x2008, 0x2008, 0x1AA8, 0x0000],
        ['C'] = [0x2AA4, 0x1008, 0x0008, 0x0008, 0x0008, 0x0008, 0x0008, 0x1008, 0x2AA4, 0x0000],
        ['D'] = [0x1AA8, 0x2008, 0x8008, 0x8008, 0x8008, 0x8008, 0x8008, 0x2008, 0x1AA8, 0x0000],
        ['E'] = [0x2AA8, 0x0008, 0x0008, 0x0AA8, 0x0008, 0x0008, 0x0008, 0x0008, 0x2AA8, 0x0000],
        ['F'] = [0x2AA8, 0x0008, 0x0008, 0x0AA8, 0x0008, 0x0008, 0x0008, 0x0008, 0x0008, 0x0000],
        ['G'] = [0x2AA4, 0x1008, 0x0008, 0x0008, 0x2A08, 0x2008, 0x2008, 0x2008, 0x1AA4, 0x0000],
        ['H'] = [0x2008, 0x2008, 0x2008, 0x2AA8, 0x2008, 0x2008, 0x2008, 0x2008, 0x2008, 0x0000],
        ['I'] = [0x2AA8, 0x0280, 0x0280, 0x0280, 0x0280, 0x0280, 0x0280, 0x0280, 0x2AA8, 0x0000],
        ['J'] = [0x2A80, 0x2800, 0x2800, 0x2800, 0x2800, 0x2800, 0x2808, 0x2808, 0x06A0, 0x0000],
        ['K'] = [0x2008, 0x0808, 0x0208, 0x0088, 0x0068, 0x0088, 0x0208, 0x0808, 0x2008, 0x0000],
        ['L'] = [0x0008, 0x0008, 0x0008, 0x0008, 0x0008, 0x0008, 0x0008, 0x0008, 0x2AA8, 0x0000],
        ['M'] = [0x2008, 0x2828, 0x2288, 0x2288, 0x2008, 0x2008, 0x2008, 0x2008, 0x2008, 0x0000],
        ['N'] = [0x2008, 0x2028, 0x2028, 0x2088, 0x2208, 0x2208, 0x2808, 0x2808, 0x2008, 0x0000],
        ['O'] = [0x1AA4, 0x2008, 0x2008, 0x2008, 0x2008, 0x2008, 0x2008, 0x2008, 0x1AA4, 0x0000],
        ['P'] = [0x1AA8, 0x2008, 0x2008, 0x1AA8, 0x0008, 0x0008, 0x0008, 0x0008, 0x0008, 0x0000],
        ['Q'] = [0x1AA4, 0x2008, 0x2008, 0x2008, 0x2008, 0x2008, 0x2208, 0x2808, 0x1AA4, 0x8000],
        ['R'] = [0x1AA8, 0x2008, 0x2008, 0x1AA8, 0x0808, 0x0808, 0x2008, 0x2008, 0x2008, 0x0000],
        ['S'] = [0x2AA4, 0x1008, 0x0008, 0x1AA4, 0x2000, 0x2000, 0x2000, 0x2004, 0x1AA8, 0x0000],
        ['T'] = [0x2AA8, 0x0280, 0x0280, 0x0280, 0x0280, 0x0280, 0x0280, 0x0280, 0x0280, 0x0000],
        ['U'] = [0x2008, 0x2008, 0x2008, 0x2008, 0x2008, 0x2008, 0x2008, 0x2008, 0x1AA4, 0x0000],
        ['V'] = [0x2008, 0x2008, 0x2008, 0x0820, 0x0820, 0x0820, 0x0280, 0x0280, 0x0280, 0x0000],
        ['W'] = [0x2008, 0x2008, 0x2008, 0x2008, 0x2008, 0x2288, 0x2288, 0x2828, 0x2008, 0x0000],
        ['X'] = [0x2008, 0x0820, 0x0820, 0x0280, 0x0280, 0x0820, 0x0820, 0x2008, 0x2008, 0x0000],
        ['Y'] = [0x2008, 0x2008, 0x0820, 0x0280, 0x0280, 0x0280, 0x0280, 0x0280, 0x0280, 0x0000],
        ['Z'] = [0x2AA8, 0x2000, 0x0800, 0x0200, 0x0080, 0x0020, 0x0008, 0x0008, 0x2AA8, 0x0000],
        ['0'] = [0x1AA4, 0x2408, 0x0908, 0x2248, 0x2098, 0x2024, 0x2008, 0x2008, 0x1AA4, 0x0000],
        ['1'] = [0x0280, 0x02A8, 0x0280, 0x0280, 0x0280, 0x0280, 0x0280, 0x0280, 0x2AA8, 0x0000],
        ['2'] = [0x1AA4, 0x2008, 0x2000, 0x0800, 0x0200, 0x0080, 0x0020, 0x0008, 0x2AA8, 0x0000],
        ['3'] = [0x1AA8, 0x2000, 0x2000, 0x1AA0, 0x2000, 0x2000, 0x2000, 0x2000, 0x1AA8, 0x0000],
        ['4'] = [0x0808, 0x0808, 0x0808, 0x0808, 0x2AA8, 0x0800, 0x0800, 0x0800, 0x0800, 0x0000],
        ['5'] = [0x2AA8, 0x0008, 0x0008, 0x1AA8, 0x2000, 0x2000, 0x2000, 0x2000, 0x1AA8, 0x0000],
        ['6'] = [0x1AA4, 0x1008, 0x0008, 0x1AA8, 0x2008, 0x2008, 0x2008, 0x2008, 0x1AA4, 0x0000],
        ['7'] = [0x2AA8, 0x2000, 0x0800, 0x0800, 0x0200, 0x0200, 0x0080, 0x0080, 0x0080, 0x0000],
        ['8'] = [0x1AA4, 0x2008, 0x2008, 0x1AA4, 0x2008, 0x2008, 0x2008, 0x2008, 0x1AA4, 0x0000],
        ['9'] = [0x1AA4, 0x2008, 0x2008, 0x2008, 0x2AA4, 0x2000, 0x2000, 0x2000, 0x1AA4, 0x0000],
    };

    /// <inheritdoc />
    public async Task<byte[]> GenerateOverlayTgaAsync(
        byte[] sourceIconBytes,
        char hotkey,
        OverlayCorner corner,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceIconBytes);
        logger.LogDebug("Rendering hotkey badge '{Key}' at {Corner}", hotkey, corner);

        using var image = Image.Load<Rgba32>(sourceIconBytes);

        var badgeChar = char.ToUpperInvariant(hotkey);
        StampBadge(image, badgeChar, corner);

        using var ms = new MemoryStream();
        var tgaEncoder = new TgaEncoder
        {
            BitsPerPixel = TgaBitsPerPixel.Pixel32,
            Compression = TgaCompression.None,
        };

        await image.SaveAsync(ms, tgaEncoder, cancellationToken);
        return ms.ToArray();
    }

    private static void StampBadge(Image<Rgba32> image, char character, OverlayCorner corner)
    {
        const int badgeWidth = 16;
        const int badgeHeight = 16;
        const int glyphWidth = 8;
        const int glyphHeight = 10;

        var (badgeX, badgeY) = CalculateBadgePosition(image.Width, image.Height, badgeWidth, badgeHeight, corner);

        // 1. Draw Drop Shadow for the badge
        DrawBadgeShadow(image, badgeX + 1, badgeY + 1, badgeWidth, badgeHeight);

        // 2. Draw High-Contrast Dual-Border Badge Box
        DrawBadgeBox(image, badgeX, badgeY, badgeWidth, badgeHeight);

        // 3. Draw Character Glyph with high-contrast outline
        var textStartX = badgeX + ((badgeWidth - glyphWidth) / 2);
        var textStartY = badgeY + ((badgeHeight - glyphHeight) / 2);
        DrawGlyph(image, character, textStartX, textStartY);
    }

    private static void DrawBadgeShadow(Image<Rgba32> image, int startX, int startY, int width, int height)
    {
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var isOuterEdge = x == width - 1 || y == height - 1;
                if (isOuterEdge)
                {
                    SetPixelSafe(image, startX + x, startY + y, DropShadowColor);
                }
            }
        }
    }

    private static Rgba32? GetBadgePixelColor(int x, int y, int width, int height)
    {
        if ((x == 0 || x == width - 1) && (y == 0 || y == height - 1))
        {
            return null;
        }

        if (x == 0 || x == width - 1 || y == 0 || y == height - 1)
        {
            return OuterBorderColor;
        }

        if (x == 1 || x == width - 2 || y == 1 || y == height - 2)
        {
            return InnerBorderColor;
        }

        return BadgeBackground;
    }

    private static void DrawBadgeBox(Image<Rgba32> image, int startX, int startY, int width, int height)
    {
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var color = GetBadgePixelColor(x, y, width, height);
                if (color.HasValue)
                {
                    SetPixelSafe(image, startX + x, startY + y, color.Value);
                }
            }
        }
    }

    private static void DrawGlyph(Image<Rgba32> image, char character, int startX, int startY)
    {
        if (!Font8X10.TryGetValue(character, out var glyphRows))
        {
            return;
        }

        // Pass 1: 8-way Dark Outline / Halo behind the glyph for maximum contrast
        for (var row = 0; row < glyphRows.Length; row++)
        {
            var rowBits = glyphRows[row];
            for (var col = 0; col < 8; col++)
            {
                var pixelType = (rowBits >> (col * 2)) & 3;
                if (pixelType != 0)
                {
                    var px = startX + col;
                    var py = startY + row;

                    SetPixelSafe(image, px - 1, py, GlyphShadow);
                    SetPixelSafe(image, px + 1, py, GlyphShadow);
                    SetPixelSafe(image, px, py - 1, GlyphShadow);
                    SetPixelSafe(image, px, py + 1, GlyphShadow);
                    SetPixelSafe(image, px - 1, py - 1, GlyphShadow);
                    SetPixelSafe(image, px + 1, py - 1, GlyphShadow);
                    SetPixelSafe(image, px - 1, py + 1, GlyphShadow);
                    SetPixelSafe(image, px + 1, py + 1, GlyphShadow);
                }
            }
        }

        // Pass 2: High-contrast glyph fill
        for (var row = 0; row < glyphRows.Length; row++)
        {
            var rowBits = glyphRows[row];
            for (var col = 0; col < 8; col++)
            {
                var pixelType = (rowBits >> (col * 2)) & 3;
                if (pixelType == 2)
                {
                    SetPixelSafe(image, startX + col, startY + row, TextColor);
                }
                else if (pixelType == 1)
                {
                    SetPixelSafe(image, startX + col, startY + row, EdgeColor);
                }
            }
        }
    }

    private static (int X, int Y) CalculateBadgePosition(
        int imageWidth,
        int imageHeight,
        int badgeWidth,
        int badgeHeight,
        OverlayCorner corner)
    {
        const int margin = 2;
        return corner switch
        {
            OverlayCorner.TopRight => (imageWidth - badgeWidth - margin, margin),
            OverlayCorner.BottomLeft => (margin, imageHeight - badgeHeight - margin),
            OverlayCorner.BottomRight => (imageWidth - badgeWidth - margin, imageHeight - badgeHeight - margin),
            _ => (margin, margin), // TopLeft
        };
    }

    private static void SetPixelSafe(Image<Rgba32> image, int x, int y, Rgba32 color)
    {
        if (x >= 0 && x < image.Width && y >= 0 && y < image.Height)
        {
            image[x, y] = color;
        }
    }
}
