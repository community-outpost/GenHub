using System.Collections.Generic;

namespace GenHub.Core.Models.Tools.RmlEditor;

/// <summary>
/// An at-rule block such as a font face, preserved opaquely for round-tripping.
/// </summary>
public sealed class RcssAtRule
{
    /// <summary>
    /// Gets or sets the at-rule name including the leading at sign.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the prelude between the name and the block or terminator.
    /// </summary>
    public string Prelude { get; set; } = string.Empty;

    /// <summary>
    /// Gets the declarations of a block at-rule, empty for statement at-rules.
    /// </summary>
    public List<RcssDeclaration> Declarations { get; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether the at-rule carries a declaration block.
    /// </summary>
    public bool HasBlock { get; set; }

    /// <summary>
    /// Gets the comments placed directly above this at-rule, preserved for round-tripping.
    /// </summary>
    public List<string> LeadingComments { get; } = [];

    /// <summary>
    /// Gets the comments trailing the declaration block, preserved for round-tripping.
    /// </summary>
    public List<string> TrailingComments { get; } = [];

    /// <summary>
    /// Gets or sets the raw inner text of a block carrying nested rules, preserved
    /// verbatim when the block cannot be represented as flat declarations.
    /// </summary>
    public string? RawBody { get; set; }
}
