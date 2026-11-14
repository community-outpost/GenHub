using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Tools.GenHotkeys;
using GenHub.Core.Interfaces.Tools.TextureEditor;
using GenHub.Core.Models.Tools.GenHotkeys;
using GenHub.Core.Services.Tools.TextureEditor;
using GenHub.Infrastructure.Imaging;
using ImageMagick;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.GenHotkeys.Services;

/// <summary>
/// Service for stamping hotkey badges onto icon textures and generating standard 60x48 in-game TGAs.
/// </summary>
public class IconOverlayService(ILogger<IconOverlayService> logger, ISageTextureCodec? sageTextureCodec = null) : IIconOverlayService
{
    // Badge styling constants (matching Zero Hour UI theme)
    private static readonly ushort[] BadgeBackground = ToQuantum(MagickColor.FromRgba(20, 20, 30, 215)); // Dark semi-transparent
    private static readonly ushort[] BadgeBorder = ToQuantum(MagickColor.FromRgba(200, 170, 50, 255));     // Gold border
    private static readonly ushort[] GlyphColor = ToQuantum(MagickColor.FromRgba(255, 255, 255, 255));     // White text
    private static readonly ushort[] ShadowColor = ToQuantum(MagickColor.FromRgba(0, 0, 0, 160));          // Drop shadow

    // 5x7 bitmap font definitions for letters A-Z and digits 0-9
    private static readonly Dictionary<char, byte[]> Font5X7 = new()
    {
        ['A'] = [0b01110, 0b10001, 0b10001, 0b11111, 0b10001, 0b10001, 0b10001],
        ['B'] = [0b11110, 0b10001, 0b10001, 0b11110, 0b10001, 0b10001, 0b11110],
        ['C'] = [0b01110, 0b10001, 0b10000, 0b10000, 0b10000, 0b10001, 0b01110],
        ['D'] = [0b11110, 0b10001, 0b10001, 0b10001, 0b10001, 0b10001, 0b11110],
        ['E'] = [0b11111, 0b10000, 0b10000, 0b11110, 0b10000, 0b10000, 0b11111],
        ['F'] = [0b11111, 0b10000, 0b10000, 0b11110, 0b10000, 0b10000, 0b10000],
        ['G'] = [0b01110, 0b10001, 0b10000, 0b10011, 0b10001, 0b10001, 0b01111],
        ['H'] = [0b10001, 0b10001, 0b10001, 0b11111, 0b10001, 0b10001, 0b10001],
        ['I'] = [0b11111, 0b00100, 0b00100, 0b00100, 0b00100, 0b00100, 0b11111],
        ['J'] = [0b00111, 0b00010, 0b00010, 0b00010, 0b00010, 0b10010, 0b01100],
        ['K'] = [0b10001, 0b10010, 0b10100, 0b11000, 0b10100, 0b10010, 0b10001],
        ['L'] = [0b10000, 0b10000, 0b10000, 0b10000, 0b10000, 0b10000, 0b11111],
        ['M'] = [0b10001, 0b11011, 0b10101, 0b10101, 0b10001, 0b10001, 0b10001],
        ['N'] = [0b10001, 0b11001, 0b10101, 0b10011, 0b10001, 0b10001, 0b10001],
        ['O'] = [0b01110, 0b10001, 0b10001, 0b10001, 0b10001, 0b10001, 0b01110],
        ['P'] = [0b11110, 0b10001, 0b10001, 0b11110, 0b10000, 0b10000, 0b10000],
        ['Q'] = [0b01110, 0b10001, 0b10001, 0b10001, 0b10101, 0b10010, 0b01101],
        ['R'] = [0b11110, 0b10001, 0b10001, 0b11110, 0b10100, 0b10010, 0b10001],
        ['S'] = [0b01111, 0b10000, 0b10000, 0b01110, 0b00001, 0b00001, 0b11110],
        ['T'] = [0b11111, 0b00100, 0b00100, 0b00100, 0b00100, 0b00100, 0b00100],
        ['U'] = [0b10001, 0b10001, 0b10001, 0b10001, 0b10001, 0b10001, 0b01110],
        ['V'] = [0b10001, 0b10001, 0b10001, 0b10001, 0b10001, 0b01010, 0b00100],
        ['W'] = [0b10001, 0b10001, 0b10001, 0b10101, 0b10101, 0b11011, 0b10001],
        ['X'] = [0b10001, 0b10001, 0b01010, 0b00100, 0b01010, 0b10001, 0b10001],
        ['Y'] = [0b10001, 0b10001, 0b01010, 0b00100, 0b00100, 0b00100, 0b00100],
        ['Z'] = [0b11111, 0b00001, 0b00010, 0b00100, 0b01000, 0b10000, 0b11111],
        ['0'] = [0b01110, 0b10011, 0b10101, 0b10101, 0b11001, 0b10001, 0b01110],
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

                using var image = LoadImageSafe(sourceIconBytes, sageTextureCodec, logger);
                if (image.Width != TextureEditorConstants.CameoSmallWidth || image.Height != TextureEditorConstants.CameoSmallHeight)
                {
                    image.Resize(new MagickGeometry(TextureEditorConstants.CameoSmallWidth, TextureEditorConstants.CameoSmallHeight)
                    {
                        IgnoreAspectRatio = true,
                    });
                }

                var badgeChar = char.ToUpperInvariant(hotkey);
                using (var pixels = image.GetPixelsUnsafe())
                {
                    StampBadge(new Canvas(pixels, (int)image.Width, (int)image.Height), badgeChar, corner);
                }

                using var ms = new MemoryStream();
                TgaWriter.Write32BitUncompressed(image, ms);
                return ms.ToArray();
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<byte[]> ConvertToTgaAsync(
        byte[] sourceIconBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceIconBytes);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                logger.LogDebug("Converting custom cameo directly to 60x48 TGA without hotkey stamp");

                using var image = LoadImageSafe(sourceIconBytes, sageTextureCodec, logger);
                if (image.Width != TextureEditorConstants.CameoSmallWidth || image.Height != TextureEditorConstants.CameoSmallHeight)
                {
                    image.Resize(new MagickGeometry(TextureEditorConstants.CameoSmallWidth, TextureEditorConstants.CameoSmallHeight)
                    {
                        IgnoreAspectRatio = true,
                    });
                }

                using var ms = new MemoryStream();
                TgaWriter.Write32BitUncompressed(image, ms);
                return ms.ToArray();
            },
            cancellationToken);
    }

    /// <summary>
    /// Loads image bytes using Magick.NET with defensive fallback to SageTextureCodec
    /// for SAGE TGA / DDS formats or non-standard image structures.
    /// </summary>
    private static MagickImage LoadImageSafe(byte[] sourceIconBytes, ISageTextureCodec? sageTextureCodec, ILogger logger)
    {
        try
        {
            var image = new MagickImage(sourceIconBytes);
            image.ColorSpace = ColorSpace.sRGB;
            image.ColorType = ColorType.TrueColorAlpha;
            return image;
        }
        catch (Exception ex) when (ex is MagickException or InvalidDataException or NotSupportedException or ArgumentException)
        {
            logger.LogDebug(ex, "Magick.NET failed to load image bytes; attempting SAGE texture decoder fallback");
            var codec = sageTextureCodec ?? new SageTextureCodec(NullLogger<SageTextureCodec>.Instance);

            var tgaResult = codec.Decode(sourceIconBytes, WndConstants.MappedImages.TextureExtensionTga);
            if (tgaResult.Success && tgaResult.Data != null)
            {
                return RgbaPixels.FromBytes(
                    tgaResult.Data.PixelData,
                    tgaResult.Data.Width,
                    tgaResult.Data.Height);
            }

            var ddsResult = codec.Decode(sourceIconBytes, WndConstants.MappedImages.TextureExtensionDds);
            if (ddsResult.Success && ddsResult.Data != null)
            {
                return RgbaPixels.FromBytes(
                    ddsResult.Data.PixelData,
                    ddsResult.Data.Width,
                    ddsResult.Data.Height);
            }

            throw new InvalidDataException("Failed to decode image bytes using Magick.NET and SAGE texture codec fallback.", ex);
        }
    }

    private static void StampBadge(Canvas image, char character, OverlayCorner corner)
    {
        if (!Font5X7.ContainsKey(character))
        {
            return;
        }

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

    private static void DrawGlyph(Canvas image, char character, int originX, int originY, int scale)
    {
        var bitmap = Font5X7[character];

        for (var row = 0; row < 7; row++)
        {
            var rowBits = bitmap[row];
            for (var col = 0; col < 5; col++)
            {
                var isSet = ((rowBits >> (4 - col)) & 1) == 1;
                if (isSet)
                {
                    DrawScaledPixel(image, originX + (col * scale), originY + (row * scale), scale);
                }
            }
        }
    }

    private static void DrawScaledPixel(Canvas image, int px, int py, int scale)
    {
        for (var dy = 0; dy < scale; dy++)
        {
            for (var dx = 0; dx < scale; dx++)
            {
                SetPixelSafe(image, px + dx, py + dy, GlyphColor);
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
            OverlayCorner.TopLeft => (margin, margin),
            OverlayCorner.TopRight => (Math.Max(0, imageWidth - badgeWidth - margin), margin),
            OverlayCorner.BottomLeft => (margin, Math.Max(0, imageHeight - badgeHeight - margin)),
            OverlayCorner.BottomRight => (Math.Max(0, imageWidth - badgeWidth - margin), Math.Max(0, imageHeight - badgeHeight - margin)),
            _ => (margin, margin),
        };
    }

    private static void DrawBadgeBox(
        Canvas image,
        int startX,
        int startY,
        int width,
        int height,
        ushort[] fill,
        ushort[] border)
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

    private static void SetPixelSafe(Canvas image, int x, int y, ushort[] color)
    {
        if (x >= 0 && x < image.Width && y >= 0 && y < image.Height)
        {
            image.Pixels.SetPixel(x, y, color);
        }
    }

    private static ushort[] ToQuantum(MagickColor color) => [color.R, color.G, color.B, color.A];

    private readonly record struct Canvas(IPixelCollection<ushort> Pixels, int Width, int Height);
}
