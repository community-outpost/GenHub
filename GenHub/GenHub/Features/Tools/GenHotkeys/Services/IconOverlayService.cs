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
    private static readonly Rgba32 BadgeBackground = new(14, 18, 24, 245);
    private static readonly Rgba32 BadgeBorder = new(245, 175, 35, 255); // Generals Gold
    private static readonly Rgba32 TextColor = new(255, 255, 255, 255);
    private static readonly Rgba32 ShadowColor = new(0, 0, 0, 180);

    // 5x7 bitmap font definitions for letters A-Z and digits 0-9
    private static readonly Dictionary<char, byte[]> Font5X7 = new()
    {
        ['A'] = [0b01110, 0b10001, 0b10001, 0b11111, 0b10001, 0b10001, 0b10001],
        ['B'] = [0b11110, 0b10001, 0b10001, 0b11110, 0b10001, 0b10001, 0b11110],
        ['C'] = [0b01111, 0b10000, 0b10000, 0b10000, 0b10000, 0b10000, 0b01111],
        ['D'] = [0b11110, 0b10001, 0b10001, 0b10001, 0b10001, 0b10001, 0b11110],
        ['E'] = [0b11111, 0b10000, 0b10000, 0b11110, 0b10000, 0b10000, 0b11111],
        ['F'] = [0b11111, 0b10000, 0b10000, 0b11110, 0b10000, 0b10000, 0b10000],
        ['G'] = [0b01111, 0b10000, 0b10000, 0b10111, 0b10001, 0b10001, 0b01111],
        ['H'] = [0b10001, 0b10001, 0b10001, 0b11111, 0b10001, 0b10001, 0b10001],
        ['I'] = [0b11111, 0b00100, 0b00100, 0b00100, 0b00100, 0b00100, 0b11111],
        ['J'] = [0b00001, 0b00001, 0b00001, 0b00001, 0b10001, 0b10001, 0b01110],
        ['K'] = [0b10001, 0b10010, 0b10100, 0b11000, 0b10100, 0b10010, 0b10001],
        ['L'] = [0b10000, 0b10000, 0b10000, 0b10000, 0b10000, 0b10000, 0b11111],
        ['M'] = [0b10001, 0b11011, 0b10101, 0b10101, 0b10001, 0b10001, 0b10001],
        ['N'] = [0b10001, 0b11001, 0b10101, 0b10011, 0b10001, 0b10001, 0b10001],
        ['O'] = [0b01110, 0b10001, 0b10001, 0b10001, 0b10001, 0b10001, 0b01110],
        ['P'] = [0b11110, 0b10001, 0b10001, 0b11110, 0b10000, 0b10000, 0b10000],
        ['Q'] = [0b01110, 0b10001, 0b10001, 0b10001, 0b10101, 0b10011, 0b01111],
        ['R'] = [0b11110, 0b10001, 0b10001, 0b11110, 0b10100, 0b10010, 0b10001],
        ['S'] = [0b01111, 0b10000, 0b10000, 0b01110, 0b00001, 0b00001, 0b11110],
        ['T'] = [0b11111, 0b00100, 0b00100, 0b00100, 0b00100, 0b00100, 0b00100],
        ['U'] = [0b10001, 0b10001, 0b10001, 0b10001, 0b10001, 0b10001, 0b01110],
        ['V'] = [0b10001, 0b10001, 0b10001, 0b10001, 0b10001, 0b01010, 0b00100],
        ['W'] = [0b10001, 0b10001, 0b10001, 0b10101, 0b10101, 0b11011, 0b10001],
        ['X'] = [0b10001, 0b10001, 0b01010, 0b00100, 0b01010, 0b10001, 0b10001],
        ['Y'] = [0b10001, 0b10001, 0b01010, 0b00100, 0b00100, 0b00100, 0b00100],
        ['Z'] = [0b11111, 0b00001, 0b00010, 0b00100, 0b01000, 0b10000, 0b11111],
        ['0'] = [0b01110, 0b10011, 0b10101, 0b10001, 0b10001, 0b10001, 0b01110],
        ['1'] = [0b00100, 0b01100, 0b00100, 0b00100, 0b00100, 0b00100, 0b01110],
        ['2'] = [0b01110, 0b10001, 0b00001, 0b00010, 0b00100, 0b01000, 0b11111],
        ['3'] = [0b11111, 0b00010, 0b00100, 0b00010, 0b00001, 0b10001, 0b01110],
        ['4'] = [0b00010, 0b00110, 0b01010, 0b10010, 0b11111, 0b00010, 0b00010],
        ['5'] = [0b11111, 0b10000, 0b11110, 0b00001, 0b00001, 0b10001, 0b01110],
        ['6'] = [0b00110, 0b01000, 0b10000, 0b11110, 0b10001, 0b10001, 0b01110],
        ['7'] = [0b11111, 0b00001, 0b00010, 0b00100, 0b01000, 0b01000, 0b01000],
        ['8'] = [0b01110, 0b10001, 0b10001, 0b01110, 0b10001, 0b10001, 0b01110],
        ['9'] = [0b01110, 0b10001, 0b10001, 0b01111, 0b00001, 0b00010, 0b01100],
    };

    /// <inheritdoc />
    public Task<byte[]> GenerateOverlayTgaAsync(
        byte[] sourceIconBytes,
        char hotkey,
        OverlayCorner corner,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceIconBytes);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
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

                image.Save(ms, tgaEncoder);
                return ms.ToArray();
            },
            cancellationToken);
    }

    private static void StampBadge(Image<Rgba32> image, char character, OverlayCorner corner)
    {
        const int scale = 2; // 2x crisp scale for 5x7 font -> 10x14 solid character
        const int paddingX = 3;
        const int paddingY = 2;
        const int charWidth = 5 * scale;
        const int charHeight = 7 * scale;
        const int badgeWidth = charWidth + (paddingX * 2);
        const int badgeHeight = charHeight + (paddingY * 2);

        var (badgeX, badgeY) = CalculateBadgePosition(image.Width, image.Height, badgeWidth, badgeHeight, corner);

        // 1. Draw Drop Shadow
        DrawBadgeBox(image, badgeX + 1, badgeY + 1, badgeWidth, badgeHeight, ShadowColor, ShadowColor);

        // 2. Draw Badge Box & Border
        DrawBadgeBox(image, badgeX, badgeY, badgeWidth, badgeHeight, BadgeBackground, BadgeBorder);

        // 3. Draw Character Glyph
        DrawGlyph(image, character, badgeX + paddingX, badgeY + paddingY, scale);
    }

    private static void DrawGlyph(Image<Rgba32> image, char character, int textStartX, int textStartY, int scale)
    {
        if (!Font5X7.TryGetValue(character, out var glyphRows))
        {
            return;
        }

        for (var row = 0; row < 7; row++)
        {
            var rowBits = glyphRows[row];
            for (var col = 0; col < 5; col++)
            {
                var isPixelSet = ((rowBits >> (4 - col)) & 1) == 1;
                if (isPixelSet)
                {
                    DrawScaledPixel(image, textStartX + (col * scale), textStartY + (row * scale), scale);
                }
            }
        }
    }

    private static void DrawScaledPixel(Image<Rgba32> image, int px, int py, int scale)
    {
        for (var dy = 0; dy < scale; dy++)
        {
            for (var dx = 0; dx < scale; dx++)
            {
                SetPixelSafe(image, px + dx, py + dy, TextColor);
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
            OverlayCorner.TopRight => (Math.Max(0, imageWidth - badgeWidth - margin), margin),
            OverlayCorner.BottomLeft => (margin, Math.Max(0, imageHeight - badgeHeight - margin)),
            OverlayCorner.BottomRight => (Math.Max(0, imageWidth - badgeWidth - margin), Math.Max(0, imageHeight - badgeHeight - margin)),
            _ => (margin, margin), // TopLeft
        };
    }

    private static void DrawBadgeBox(
        Image<Rgba32> image,
        int startX,
        int startY,
        int width,
        int height,
        Rgba32 fill,
        Rgba32 border)
    {
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var isBorder = x == 0 || x == width - 1 || y == 0 || y == height - 1;
                var color = isBorder ? border : fill;
                SetPixelSafe(image, startX + x, startY + y, color);
            }
        }
    }

    private static void SetPixelSafe(Image<Rgba32> image, int x, int y, Rgba32 color)
    {
        if (x >= 0 && x < image.Width && y >= 0 && y < image.Height)
        {
            image[x, y] = color;
        }
    }
}
