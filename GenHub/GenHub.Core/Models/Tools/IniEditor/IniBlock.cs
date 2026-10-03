using System.Collections.Generic;

namespace GenHub.Core.Models.Tools.IniEditor;

/// <summary>
/// A single INI block (for example <c>Object ... End</c> or a <c>Draw = ... End</c> module).
/// </summary>
public sealed class IniBlock
{
    /// <summary>
    /// Gets or sets the block type (for example <c>Object</c> or <c>Weapon</c>).
    /// </summary>
    public string BlockType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the block name or identifier tokens from the opening line.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the assignment value for module style headers.
    /// When set, the header is written as <c>BlockType = AssignmentValue</c>.
    /// </summary>
    public string? AssignmentValue { get; set; }

    /// <summary>
    /// Gets the canonical header text used for display, writing, and validation.
    /// </summary>
    public string DisplayHeader
    {
        get
        {
            if (AssignmentValue != null)
            {
                return $"{BlockType} = {AssignmentValue}";
            }

            return string.IsNullOrEmpty(Name) ? BlockType : $"{BlockType} {Name}";
        }
    }

    /// <summary>
    /// Gets the ordered fields of this block.
    /// </summary>
    public List<IniField> Fields { get; } = [];

    /// <summary>
    /// Gets nested module sub-blocks (for example Behavior or Draw modules inside an Object).
    /// </summary>
    public List<IniBlock> Children { get; } = [];

    /// <summary>
    /// Gets full line comments written immediately before the opening line.
    /// </summary>
    public List<IniComment> LeadingComments { get; } = [];

    /// <summary>
    /// Gets full line comments written after the last field or child, before <c>End</c>.
    /// </summary>
    public List<IniComment> TrailingComments { get; } = [];

    /// <summary>
    /// Gets or sets the inline comment from the opening line, when present.
    /// </summary>
    public string? TrailingComment { get; set; }

    /// <summary>
    /// Gets or sets the 1-based line number where the block starts, or 0 when unknown.
    /// </summary>
    public int LineNumber { get; set; }
}
