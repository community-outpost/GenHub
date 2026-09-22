using System;
using System.Collections.Generic;
using System.Linq;

namespace GenHub.Core.Models.Tools.WndEditor;

/// <summary>
/// Engine-accurate preview description for one window: which mapped images to draw,
/// which colors fill the background, and which text overlays the control.
/// </summary>
public sealed record WndPreviewPlan(
    string? SingleImage,
    string? LeftImage,
    string? CenterImage,
    string? RightImage,
    string? GlyphImage,
    WndPreviewSubImages? SubImages,
    bool IsVerticalBar,
    WndRgbaColor? FillColor,
    WndRgbaColor? BorderColor,
    string? Text,
    WndRgbaColor? TextColor,
    int FontSize,
    bool FontBold,
    bool TextCentered,
    bool IsHidden,
    string? UnderlayImage = null)
{
    /// <summary>
    /// Gets a value indicating whether a three-piece (left, tiled center, right) bar should be drawn.
    /// </summary>
    public bool IsThreePiece => LeftImage != null && CenterImage != null && RightImage != null;

    /// <summary>
    /// Gets the distinct mapped image names referenced by this plan.
    /// </summary>
    public IReadOnlyCollection<string> ReferencedImages
    {
        get
        {
            var names = new[] { SingleImage, LeftImage, CenterImage, RightImage, GlyphImage, UnderlayImage }
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (SubImages != null)
            {
                foreach (var name in SubImages.ReferencedImages)
                {
                    names.Add(name);
                }
            }

            return names;
        }
    }
}
