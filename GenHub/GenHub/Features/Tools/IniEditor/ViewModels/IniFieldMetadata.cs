using System.Collections.Generic;

namespace GenHub.Features.Tools.IniEditor.ViewModels;

/// <summary>
/// Metadata describing an INI field row for presentation and editing.
/// </summary>
/// <param name="Description">Field description from schema.</param>
/// <param name="IsKnown">Whether the field is recognized by schema.</param>
/// <param name="Tooltip">Display tooltip.</param>
/// <param name="Suggestions">Autocomplete suggestions.</param>
/// <param name="ReferenceBlockType">Block type referenced by field value.</param>
/// <param name="IsTexture">Whether the field represents a texture reference.</param>
public sealed record IniFieldMetadata(
    string? Description = null,
    bool IsKnown = false,
    string? Tooltip = null,
    IReadOnlyList<string>? Suggestions = null,
    string? ReferenceBlockType = null,
    bool IsTexture = false);
