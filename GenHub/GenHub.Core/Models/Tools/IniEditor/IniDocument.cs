using System.Collections.Generic;
using System.Text;

namespace GenHub.Core.Models.Tools.IniEditor;

/// <summary>
/// An in-memory INI data document.
/// </summary>
public sealed class IniDocument
{
    /// <summary>
    /// Gets or sets the source path this document was loaded from, when known.
    /// </summary>
    public string? SourcePath { get; set; }

    /// <summary>
    /// Gets or sets the text encoding detected when the document was loaded.
    /// Saves and format operations write back with this encoding so ANSI files
    /// and UTF-8 byte order marks round-trip instead of being re-encoded.
    /// </summary>
    public Encoding SourceEncoding { get; set; } = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Gets the ordered top-level blocks.
    /// </summary>
    public List<IniBlock> Blocks { get; } = [];

    /// <summary>
    /// Gets file-scope settings written outside of any block (for example the
    /// benchmark and LOD preset entries in <c>GameLODPresets.ini</c>).
    /// </summary>
    public List<IniField> GlobalFields { get; } = [];

    /// <summary>
    /// Gets full line comments written before the first block.
    /// </summary>
    public List<IniComment> HeaderComments { get; } = [];

    /// <summary>
    /// Gets full line comments written after the last block.
    /// </summary>
    public List<IniComment> TrailingComments { get; } = [];
}
