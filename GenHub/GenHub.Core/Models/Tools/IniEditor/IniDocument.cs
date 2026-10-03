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

    /// <summary>
    /// Gets non-fatal parse diagnostics collected while reading the source text
    /// (missing <c>End</c> markers, unexpected <c>End</c> lines, fields without keys).
    /// The parser recovers from these so editors can open and repair real-world
    /// files such as map overrides instead of refusing to open them.
    /// </summary>
    public List<string> ParseErrors { get; } = [];

    /// <summary>
    /// Gets a value indicating whether the parser recovered from any content errors.
    /// </summary>
    public bool HasParseErrors => ParseErrors.Count > 0;

    /// <summary>
    /// Gets or sets a value indicating whether recovery discarded source lines
    /// (keyless fields, unexpected <c>End</c> markers) or repaired a missing
    /// <c>End</c> ambiguously. Serializing such a document would silently drop
    /// content or bake in the wrong block nesting, so format and save paths must
    /// refuse until the parse errors are resolved. Missing-<c>End</c> repairs
    /// that only append markers never set this flag.
    /// </summary>
    public bool HasDiscardedContent { get; set; }
}
