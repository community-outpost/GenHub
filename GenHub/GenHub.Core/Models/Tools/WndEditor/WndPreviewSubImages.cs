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
    string? SliderThumb)
{
    /// <summary>
    /// Gets the distinct mapped image names referenced by these sub-images.
    /// </summary>
    public IReadOnlyCollection<string> ReferencedImages =>
        new[] { ScrollUp, ScrollDown, ScrollThumb, ComboButton, SliderThumb }
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
}
