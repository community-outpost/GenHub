using System.Collections.Generic;

namespace GenHub.Core.Models.Tools.IniEditor;

/// <summary>
/// A single key/value field of an INI block. Order is significant and preserved.
/// </summary>
/// <param name="Key">The field key.</param>
/// <param name="Value">The raw field value.</param>
/// <param name="TrailingComment">The inline comment from the field line, when present.</param>
public sealed record IniField(string Key, string Value, string? TrailingComment = null)
{
    /// <summary>
    /// Gets full line comments written immediately before the field line.
    /// </summary>
    public List<IniComment> LeadingComments { get; } = [];

    /// <summary>
    /// Gets a value indicating whether the field was written as a bare key without
    /// a separator (for example <c>Blank</c> in credits files). Bare fields round-trip
    /// verbatim instead of being rewritten as <c>Key = </c>.
    /// </summary>
    public bool IsBare { get; init; }
}
