using Avalonia.Controls;
using GenHub.Core.Models.Tools.RmlEditor;
using System;
using System.Collections.Generic;

namespace GenHub.Features.Tools.RmlEditor.Services;

/// <summary>
/// The result of a single interface preview build.
/// </summary>
public sealed class RmlPreviewResult
{
    /// <summary>
    /// Gets the preview root control.
    /// </summary>
    public Control Root { get; init; } = new Border();

    /// <summary>
    /// Gets the preview controls keyed by element identity.
    /// </summary>
    public IReadOnlyDictionary<Guid, Control> ElementControls { get; init; } = new Dictionary<Guid, Control>();

    /// <summary>
    /// Gets the computed styles keyed by element identity.
    /// </summary>
    public IReadOnlyDictionary<Guid, RmlComputedStyle> ComputedStyles { get; init; } = new Dictionary<Guid, RmlComputedStyle>();

    /// <summary>
    /// Gets the image references that resolved to no art.
    /// </summary>
    public IReadOnlyList<string> MissingImages { get; init; } = [];
}
