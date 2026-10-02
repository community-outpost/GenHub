using Avalonia.Media.Imaging;
using GenHub.Core.Models.Tools.RmlEditor;
using System;
using System.Collections.Generic;

namespace GenHub.Features.Tools.RmlEditor.Services;

/// <summary>
/// Options controlling a single interface preview build.
/// </summary>
public sealed class RmlPreviewOptions
{
    /// <summary>
    /// Gets the style sheets applied in cascade order.
    /// </summary>
    public IReadOnlyList<RcssDocument> StyleSheets { get; init; } = [];

    /// <summary>
    /// Gets the pre-resolved image bitmaps keyed by raw reference.
    /// </summary>
    public IReadOnlyDictionary<string, Bitmap?> Images { get; init; } = new Dictionary<string, Bitmap?>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets a value indicating whether hidden elements render as placeholders.
    /// </summary>
    public bool ShowHidden { get; set; }

    /// <summary>
    /// Gets or sets the base font size in device-independent pixels.
    /// </summary>
    public double BaseFontSize { get; set; } = 16.0;

    /// <summary>
    /// Gets or sets the preview surface width in device-independent pixels.
    /// </summary>
    public double SurfaceWidth { get; set; } = 1024.0;

    /// <summary>
    /// Gets or sets the selected element identity highlighted in the preview.
    /// </summary>
    public Guid? SelectedId { get; set; }

    /// <summary>
    /// Gets or sets the callback invoked when a preview element is pressed.
    /// </summary>
    public Action<Guid>? ElementPressed { get; set; }
}
