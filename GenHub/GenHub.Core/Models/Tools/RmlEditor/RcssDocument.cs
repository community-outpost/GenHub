using System.Collections.Generic;

namespace GenHub.Core.Models.Tools.RmlEditor;

/// <summary>
/// An in-memory interface style sheet (.rcss) document.
/// </summary>
public sealed class RcssDocument
{
    /// <summary>
    /// Gets or sets the source path this document was loaded from, when known.
    /// </summary>
    public string? SourcePath { get; set; }

    /// <summary>
    /// Gets the ordered style rules of this document.
    /// </summary>
    public List<RcssRule> Rules { get; } = [];

    /// <summary>
    /// Gets the ordered at-rules of this document.
    /// </summary>
    public List<RcssAtRule> AtRules { get; } = [];

    /// <summary>
    /// Gets the comments placed before the first rule, preserved for round-tripping.
    /// </summary>
    public List<string> LeadingComments { get; } = [];
}
