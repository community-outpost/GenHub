using GenHub.Core.Constants;
using ImageMagick;
using System;

namespace GenHub.Features.Tools.WndEditor.Services;

/// <summary>
/// Composes three-piece button and text-entry bars (left cap, stretched center, right cap)
/// the way the engine draws them (stretchable middle per OpenSAGE StretchableMappedImageSource),
/// as well as composite background and menu frame overlays.
/// </summary>
public static class WndPreviewImageComposer
{
    /// <summary>
    /// Composes left, center, and right art into one bar of the requested size.
    /// </summary>
    /// <param name="leftPng">The left cap PNG bytes.</param>
    /// <param name="centerPng">The middle PNG bytes, stretched to fill between the caps.</param>
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

    /// <summary>
    /// Composes top, center, and bottom art into one vertical bar of the requested size.
    /// </summary>
    /// <param name="topPng">The top cap PNG bytes.</param>
    /// <param name="centerPng">The middle PNG bytes, stretched to fill between the caps.</param>
    /// <param name="bottomPng">The bottom cap PNG bytes.</param>
    /// <param name="width">The target width in pixels.</param>
    /// <param name="height">The target height in pixels.</param>
    /// <returns>The composed PNG bytes, or null when composition fails.</returns>
    public static byte[]? ComposeThreePieceVertical(byte[] topPng, byte[] centerPng, byte[] bottomPng, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(topPng);
        ArgumentNullException.ThrowIfNull(centerPng);
        ArgumentNullException.ThrowIfNull(bottomPng);
        if (width <= 0 || height <= 0 || width > WndConstants.Preview.MaxComposedDimension || height > WndConstants.Preview.MaxComposedDimension)
        {
            return null;
        }

        try
        {
            using var top = StretchToWidth(new MagickImage(topPng), width);
            using var center = StretchToWidth(new MagickImage(centerPng), width);
            using var bottom = StretchToWidth(new MagickImage(bottomPng), width);
            if (top.Width == 0 || center.Width == 0 || bottom.Width == 0)
            {
                return null;
            }

            using var canvas = new MagickImage(MagickColors.Transparent, (uint)width, (uint)height);
            if (top.Height + bottom.Height >= (uint)height)
            {
                ComposeVerticalHalves(canvas, top, bottom, width, height);
            }
            else
            {
                ComposeVerticalBar(canvas, top, center, bottom, height);
            }

            return canvas.ToByteArray(MagickFormat.Png);
        }
        catch (MagickException)
        {
            return null;
        }
    }

    /// <summary>
    /// Composes an overlay image on top of a backdrop underlay stretched to the target dimensions.
    /// </summary>
    /// <param name="underlayPng">The backdrop underlay PNG bytes.</param>
    /// <param name="overlayPng">The foreground overlay PNG bytes.</param>
    /// <param name="width">The target width in pixels.</param>
    /// <param name="height">The target height in pixels.</param>
    /// <returns>The composed PNG bytes, or null when composition fails.</returns>
    public static byte[]? ComposeUnderlay(byte[] underlayPng, byte[] overlayPng, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(underlayPng);
        ArgumentNullException.ThrowIfNull(overlayPng);
        if (width <= 0 || height <= 0 || width > WndConstants.Preview.MaxComposedDimension || height > WndConstants.Preview.MaxComposedDimension)
        {
            return null;
        }

        try
        {
            using var canvas = new MagickImage(underlayPng);
            if ((int)canvas.Width != width || (int)canvas.Height != height)
            {
                canvas.Resize(new MagickGeometry((uint)width, (uint)height) { IgnoreAspectRatio = true });
            }

            using var overlay = new MagickImage(overlayPng);
            if ((int)overlay.Width != width || (int)overlay.Height != height)
            {
                overlay.Resize(new MagickGeometry((uint)width, (uint)height) { IgnoreAspectRatio = true });
            }

            canvas.Composite(overlay, 0, 0, CompositeOperator.Over);
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

    private static MagickImage StretchToWidth(MagickImage image, int width)
    {
        if ((int)image.Width != width)
        {
            image.Resize(new MagickGeometry((uint)width, image.Height) { IgnoreAspectRatio = true });
        }

        return image;
    }

    private static void ComposeVerticalHalves(MagickImage canvas, MagickImage top, MagickImage bottom, int width, int height)
    {
        var half = height / 2;
        using var topHalf = (MagickImage)top.Clone();
        using var bottomHalf = (MagickImage)bottom.Clone();
        topHalf.Resize(new MagickGeometry((uint)width, (uint)half) { IgnoreAspectRatio = true });
        bottomHalf.Resize(new MagickGeometry((uint)width, (uint)(height - half)) { IgnoreAspectRatio = true });
        canvas.Composite(topHalf, 0, 0, CompositeOperator.Over);
        canvas.Composite(bottomHalf, 0, half, CompositeOperator.Over);
    }

    private static void ComposeVerticalBar(MagickImage canvas, MagickImage top, MagickImage center, MagickImage bottom, int height)
    {
        canvas.Composite(top, 0, 0, CompositeOperator.Over);
        var bottomY = height - (int)bottom.Height;
        canvas.Composite(bottom, 0, bottomY, CompositeOperator.Over);
        var middleHeight = bottomY - (int)top.Height;
        if (middleHeight > 0)
        {
            using var stretched = (MagickImage)center.Clone();
            stretched.Resize(new MagickGeometry(stretched.Width, (uint)middleHeight) { IgnoreAspectRatio = true });
            canvas.Composite(stretched, 0, (int)top.Height, CompositeOperator.Over);
        }
    }

    private static void ComposeBar(MagickImage canvas, MagickImage left, MagickImage center, MagickImage right, int width)
    {
        canvas.Composite(left, 0, 0, CompositeOperator.Over);
        var rightX = width - (int)right.Width;
        canvas.Composite(right, rightX, 0, CompositeOperator.Over);
        var middleWidth = rightX - (int)left.Width;
        if (middleWidth > 0)
        {
            using var stretched = (MagickImage)center.Clone();
            stretched.Resize(new MagickGeometry((uint)middleWidth, stretched.Height) { IgnoreAspectRatio = true });
            canvas.Composite(stretched, (int)left.Width, 0, CompositeOperator.Over);
        }
    }
}
