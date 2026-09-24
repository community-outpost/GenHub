using System;
using System.Collections.Generic;
using System.Linq;

namespace GenHub.Core.Models.Tools.WndEditor;

/// <summary>
/// Sub-gadget art names from enabled sub-draw-data blocks (scrollbar pieces, combo button, slider thumb).
/// </summary>
public sealed record WndPreviewSubImages(
    string? ScrollUp,
    string? ScrollDown,
    string? ScrollThumb,
    string? ComboButton,
    string? SliderThumb,
    string? ScrollTrackTop = null,
    string? ScrollTrackCenter = null,
    string? ScrollTrackBottom = null)
{
    /// <summary>
    /// Gets a value indicating whether the scrollbar track has all three vertical pieces.
    /// </summary>
    public bool HasScrollTrack =>
        !string.IsNullOrWhiteSpace(ScrollTrackTop)
        && !string.IsNullOrWhiteSpace(ScrollTrackCenter)
        && !string.IsNullOrWhiteSpace(ScrollTrackBottom);

    /// <summary>
    /// Gets the distinct mapped image names referenced by these sub-images.
    /// </summary>
    public IReadOnlyCollection<string> ReferencedImages =>
        new[] { ScrollUp, ScrollDown, ScrollThumb, ComboButton, SliderThumb, ScrollTrackTop, ScrollTrackCenter, ScrollTrackBottom }
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
}
