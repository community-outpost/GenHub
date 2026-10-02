using System.Collections.Generic;

namespace GenHub.Core.Models.Tools.RmlEditor;

/// <summary>
/// A style sheet rule pairing selectors with property declarations.
/// </summary>
public sealed class RcssRule
{
    /// <summary>
    /// Gets the ordered selector list of this rule.
    /// </summary>
    public List<string> Selectors { get; } = [];

    /// <summary>
    /// Gets the ordered property declarations of this rule.
    /// </summary>
    public List<RcssDeclaration> Declarations { get; } = [];

    /// <summary>
    /// Gets the comments placed directly above this rule, preserved for round-tripping.
    /// </summary>
    public List<string> LeadingComments { get; } = [];

    /// <summary>
    /// Gets the comments trailing the declaration block, preserved for round-tripping.
    /// </summary>
    public List<string> TrailingComments { get; } = [];
}
