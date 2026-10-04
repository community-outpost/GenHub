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
/// <param name="PairTargets">Known target names for target plus percent pair values.</param>
/// <param name="ModuleName">Owning module header when the row comes from a child module fallback.</param>
public sealed record IniFieldMetadata(
    string? Description = null,
    bool IsKnown = false,
    string? Tooltip = null,
    IReadOnlyList<IniSuggestionItem>? Suggestions = null,
    string? ReferenceBlockType = null,
    bool IsTexture = false,
    IReadOnlyList<IniSuggestionItem>? PairTargets = null,
    string? ModuleName = null);
