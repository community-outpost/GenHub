using GenHub.Core.Constants;
using ImageMagick;
using System;

namespace GenHub.Features.Tools.WndEditor.Services;

/// <summary>
/// Composes three-piece button and text-entry bars (left cap, tiled center, right cap)
/// the way the engine draws them (see W3DGadgetPushButtonImageDrawThree in GeneralsGameCode).
/// </summary>
public static class WndPreviewImageComposer
{
    /// <summary>
    /// Composes left, center, and right art into one bar of the requested size.
    /// </summary>
    /// <param name="leftPng">The left cap PNG bytes.</param>
    /// <param name="centerPng">The horizontally tiled middle PNG bytes.</param>
    /// <param name="rightPng">The right cap PNG bytes.</param>
    /// <param name="width">The target width in pixels.</param>
    /// <param name="height">The target height in pixels.</param>
    /// <returns>The composed PNG bytes, or null when composition fails.</returns>
    public static byte[]? ComposeThreePiece(byte[] leftPng, byte[] centerPng, byte[] rightPng, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(leftPng);
        ArgumentNullException.ThrowIfNull(centerPng);
        ArgumentNullException.ThrowIfNull(rightPng);
        if (width <= 0 || height <= 0 || width > WndConstants.Preview.MaxComposedDimension || height > WndConstants.Preview.MaxComposedDimension)
        {
            return null;
        }

        try
        {
            using var left = StretchToHeight(new MagickImage(leftPng), height);
            using var center = StretchToHeight(new MagickImage(centerPng), height);
            using var right = StretchToHeight(new MagickImage(rightPng), height);
            if (left.Width == 0 || center.Width == 0 || right.Width == 0)
            {
                return null;
            }

            using var canvas = new MagickImage(MagickColors.Transparent, (uint)width, (uint)height);
            if (left.Width + right.Width >= (uint)width)
            {
                ComposeHalves(canvas, left, right, width, height);
            }
            else
            {
                ComposeBar(canvas, left, center, right, width);
            }

            return canvas.ToByteArray(MagickFormat.Png);
        }
        catch (MagickException)
        {
            return null;
        }
    }

    private static MagickImage StretchToHeight(MagickImage image, int height)
    {
        if ((int)image.Height != height)
        {
            image.Resize(new MagickGeometry(image.Width, (uint)height) { IgnoreAspectRatio = true });
        }

        return image;
    }

    private static void ComposeHalves(MagickImage canvas, MagickImage left, MagickImage right, int width, int height)
    {
        var half = width / 2;
        using var leftHalf = (MagickImage)left.Clone();
        using var rightHalf = (MagickImage)right.Clone();
        leftHalf.Resize(new MagickGeometry((uint)half, (uint)height) { IgnoreAspectRatio = true });
        rightHalf.Resize(new MagickGeometry((uint)(width - half), (uint)height) { IgnoreAspectRatio = true });
        canvas.Composite(leftHalf, 0, 0, CompositeOperator.Over);
        canvas.Composite(rightHalf, half, 0, CompositeOperator.Over);
    }

    private static void ComposeBar(MagickImage canvas, MagickImage left, MagickImage center, MagickImage right, int width)
    {
        canvas.Composite(left, 0, 0, CompositeOperator.Over);
        var rightX = width - (int)right.Width;
        canvas.Composite(right, rightX, 0, CompositeOperator.Over);
        var x = (int)left.Width;
        while (x + (int)center.Width <= rightX)
        {
            canvas.Composite(center, x, 0, CompositeOperator.Over);
            x += (int)center.Width;
        }

        if (x < rightX)
        {
            using var clipped = (MagickImage)center.Clone();
            clipped.Crop(new MagickGeometry((uint)(rightX - x), clipped.Height));
            canvas.Composite(clipped, x, 0, CompositeOperator.Over);
        }
    }
}
