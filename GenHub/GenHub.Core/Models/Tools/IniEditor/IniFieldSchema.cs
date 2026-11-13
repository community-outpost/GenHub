using System.Collections.Generic;

namespace GenHub.Core.Models.Tools.IniEditor;

/// <summary>
/// Describes a single INI field for schema assisted editing.
/// </summary>
/// <param name="Key">The field key.</param>
/// <param name="Description">Short description of the field.</param>
/// <param name="IsNumeric">Whether the value is numeric.</param>
/// <param name="Options">Fixed value options for searchable dropdowns, when any.</param>
/// <param name="ReferenceBlockType">Referenced block type for reference pickers, when any.</param>
/// <param name="IsTexture">Whether the value names a mapped image texture.</param>
public sealed record IniFieldSchema(
    string Key,
    string Description,
    bool IsNumeric,
    IReadOnlyList<string>? Options = null,
    string? ReferenceBlockType = null,
    bool IsTexture = false);
