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

    // High-fidelity 8x10 bitmap font with anti-aliased edge smoothing ('+') and solid strokes ('#')
    private static readonly Dictionary<char, string[]> Font8X10 = new()
    {
        ['A'] =
        [
            "  ####  ",
            " +####+ ",
            " #    # ",
            " #    # ",
            " ###### ",
            " #    # ",
            " #    # ",
            " #    # ",
            " #    # ",
            "        ",
        ],
        ['B'] =
        [
            " #####+ ",
            " #    # ",
            " #    # ",
            " #####+ ",
            " #    # ",
            " #    # ",
            " #    # ",
            " #    # ",
            " #####+ ",
            "        ",
        ],
        ['C'] =
        [
            " +##### ",
            " #    + ",
            " #      ",
            " #      ",
            " #      ",
            " #      ",
            " #      ",
            " #    + ",
            " +##### ",
            "        ",
        ],
        ['D'] =
        [
            " #####+ ",
            " #    # ",
            " #     #",
            " #     #",
            " #     #",
            " #     #",
            " #     #",
            " #    # ",
            " #####+ ",
            "        ",
        ],
        ['E'] =
        [
            " ###### ",
            " #      ",
            " #      ",
            " #####  ",
            " #      ",
            " #      ",
            " #      ",
            " #      ",
            " ###### ",
            "        ",
        ],
        ['F'] =
        [
            " ###### ",
            " #      ",
            " #      ",
            " #####  ",
            " #      ",
            " #      ",
            " #      ",
            " #      ",
            " #      ",
            "        ",
        ],
        ['G'] =
        [
            " +##### ",
            " #    + ",
            " #      ",
            " #      ",
            " #  ### ",
            " #    # ",
            " #    # ",
            " #    # ",
            " +####+ ",
            "        ",
        ],
        ['H'] =
        [
            " #    # ",
            " #    # ",
            " #    # ",
            " ###### ",
            " #    # ",
            " #    # ",
            " #    # ",
            " #    # ",
            " #    # ",
            "        ",
        ],
        ['I'] =
        [
            " ###### ",
            "   ##   ",
            "   ##   ",
            "   ##   ",
            "   ##   ",
            "   ##   ",
            "   ##   ",
            "   ##   ",
            " ###### ",
            "        ",
        ],
        ['J'] =
        [
            "   #### ",
            "     ## ",
            "     ## ",
            "     ## ",
            "     ## ",
            "     ## ",
            " #   ## ",
            " #   ## ",
            "  ###+  ",
            "        ",
        ],
        ['K'] =
        [
            " #    # ",
            " #   #  ",
            " #  #   ",
            " # #    ",
            " ##+    ",
            " # #    ",
            " #  #   ",
            " #   #  ",
            " #    # ",
            "        ",
        ],
        ['L'] =
        [
            " #      ",
            " #      ",
            " #      ",
            " #      ",
            " #      ",
            " #      ",
            " #      ",
            " #      ",
            " ###### ",
            "        ",
        ],
        ['M'] =
        [
            " #    # ",
            " ##  ## ",
            " # ## # ",
            " # ## # ",
            " #    # ",
            " #    # ",
            " #    # ",
            " #    # ",
            " #    # ",
            "        ",
        ],
        ['N'] =
        [
            " #    # ",
            " ##   # ",
            " ##   # ",
            " # #  # ",
            " #  # # ",
            " #  # # ",
            " #   ## ",
            " #   ## ",
            " #    # ",
            "        ",
        ],
        ['O'] =
        [
            " +####+ ",
            " #    # ",
            " #    # ",
            " #    # ",
            " #    # ",
            " #    # ",
            " #    # ",
            " #    # ",
            " +####+ ",
            "        ",
        ],
        ['P'] =
        [
            " #####+ ",
            " #    # ",
            " #    # ",
            " #####+ ",
            " #      ",
            " #      ",
            " #      ",
            " #      ",
            " #      ",
            "        ",
        ],
        ['Q'] =
        [
            " +####+ ",
            " #    # ",
            " #    # ",
            " #    # ",
            " #    # ",
            " #    # ",
            " #  # # ",
            " #   ## ",
            " +####+ ",
            "       #",
        ],
        ['R'] =
        [
            " #####+ ",
            " #    # ",
            " #    # ",
            " #####+ ",
            " #   #  ",
            " #   #  ",
            " #    # ",
            " #    # ",
            " #    # ",
            "        ",
        ],
        ['S'] =
        [
            " +##### ",
            " #    + ",
            " #      ",
            " +####+ ",
            "      # ",
            "      # ",
            "      # ",
            " +    # ",
            " #####+ ",
            "        ",
        ],
        ['T'] =
        [
            " ###### ",
            "   ##   ",
            "   ##   ",
            "   ##   ",
            "   ##   ",
            "   ##   ",
            "   ##   ",
            "   ##   ",
            "   ##   ",
            "        ",
        ],
        ['U'] =
        [
            " #    # ",
            " #    # ",
            " #    # ",
            " #    # ",
            " #    # ",
            " #    # ",
            " #    # ",
            " #    # ",
            " +####+ ",
            "        ",
        ],
        ['V'] =
        [
            " #    # ",
            " #    # ",
            " #    # ",
            "  #  #  ",
            "  #  #  ",
            "  #  #  ",
            "   ##   ",
            "   ##   ",
            "   ##   ",
            "        ",
        ],
        ['W'] =
        [
            " #    # ",
            " #    # ",
            " #    # ",
            " #    # ",
            " #    # ",
            " # ## # ",
            " # ## # ",
            " ##  ## ",
            " #    # ",
            "        ",
        ],
        ['X'] =
        [
            " #    # ",
            "  #  #  ",
            "  #  #  ",
            "   ##   ",
            "   ##   ",
            "  #  #  ",
            "  #  #  ",
            " #    # ",
            " #    # ",
            "        ",
        ],
        ['Y'] =
        [
            " #    # ",
            " #    # ",
            "  #  #  ",
            "   ##   ",
            "   ##   ",
            "   ##   ",
            "   ##   ",
            "   ##   ",
            "   ##   ",
            "        ",
        ],
        ['Z'] =
        [
            " ###### ",
            "      # ",
            "     #  ",
            "    #   ",
            "   #    ",
            "  #     ",
            " #      ",
            " #      ",
            " ###### ",
            "        ",
        ],
        ['0'] =
        [
            " +####+ ",
            " #   +# ",
            " #  +#  ",
            " # +# # ",
            " #+#  # ",
            " +#   # ",
            " #    # ",
            " #    # ",
            " +####+ ",
            "        ",
        ],
        ['1'] =
        [
            "   ##   ",
            " ####   ",
            "   ##   ",
            "   ##   ",
            "   ##   ",
            "   ##   ",
            "   ##   ",
            "   ##   ",
            " ###### ",
            "        ",
        ],
        ['2'] =
        [
            " +####+ ",
            " #    # ",
            "      # ",
            "     #  ",
            "    #   ",
            "   #    ",
            "  #     ",
            " #      ",
            " ###### ",
            "        ",
        ],
        ['3'] =
        [
            " #####+ ",
            "      # ",
            "      # ",
            "  ####+ ",
            "      # ",
            "      # ",
            "      # ",
            "      # ",
            " #####+ ",
            "        ",
        ],
        ['4'] =
        [
            " #   #  ",
            " #   #  ",
            " #   #  ",
            " #   #  ",
            " ###### ",
            "     #  ",
            "     #  ",
            "     #  ",
            "     #  ",
            "        ",
        ],
        ['5'] =
        [
            " ###### ",
            " #      ",
            " #      ",
            " #####+ ",
            "      # ",
            "      # ",
            "      # ",
            "      # ",
            " #####+ ",
            "        ",
        ],
        ['6'] =
        [
            " +####+ ",
            " #    + ",
            " #      ",
            " #####+ ",
            " #    # ",
            " #    # ",
            " #    # ",
            " #    # ",
            " +####+ ",
            "        ",
        ],
        ['7'] =
        [
            " ###### ",
            "      # ",
            "     #  ",
            "     #  ",
            "    #   ",
            "    #   ",
            "   #    ",
            "   #    ",
            "   #    ",
            "        ",
        ],
        ['8'] =
        [
            " +####+ ",
            " #    # ",
            " #    # ",
            " +####+ ",
            " #    # ",
            " #    # ",
            " #    # ",
            " #    # ",
            " +####+ ",
            "        ",
        ],
        ['9'] =
        [
            " +####+ ",
            " #    # ",
            " #    # ",
            " #    # ",
            " +##### ",
            "      # ",
            "      # ",
            "      # ",
            " +####+ ",
            "        ",
        ],
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

    private static void DrawBadgeBox(Image<Rgba32> image, int startX, int startY, int width, int height)
    {
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                // Round corners: skip the 4 extreme outer corner pixels
                var isCorner = (x == 0 || x == width - 1) && (y == 0 || y == height - 1);
                if (isCorner)
                {
                    continue;
                }

                var isOuterBorder = x == 0 || x == width - 1 || y == 0 || y == height - 1;
                var isInnerBorder = x == 1 || x == width - 2 || y == 1 || y == height - 2;

                Rgba32 pixelColor;
                if (isOuterBorder)
                {
                    pixelColor = OuterBorderColor;
                }
                else if (isInnerBorder)
                {
                    pixelColor = InnerBorderColor;
                }
                else
                {
                    pixelColor = BadgeBackground;
                }

                SetPixelSafe(image, startX + x, startY + y, pixelColor);
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
            var line = glyphRows[row];
            for (var col = 0; col < line.Length; col++)
            {
                var ch = line[col];
                if (ch == '#' || ch == '+')
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
            var line = glyphRows[row];
            for (var col = 0; col < line.Length; col++)
            {
                var ch = line[col];
                if (ch == '#')
                {
                    SetPixelSafe(image, startX + col, startY + row, TextColor);
                }
                else if (ch == '+')
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
