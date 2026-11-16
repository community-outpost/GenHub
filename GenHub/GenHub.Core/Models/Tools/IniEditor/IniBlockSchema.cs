using System.Collections.Generic;

namespace GenHub.Core.Models.Tools.IniEditor;

/// <summary>
/// Describes an INI block type for schema assisted editing.
/// </summary>
/// <param name="BlockType">The block type name.</param>
/// <param name="Description">Short description of the block.</param>
/// <param name="Fields">Known fields for the block.</param>
public sealed record IniBlockSchema(string BlockType, string Description, IReadOnlyList<IniFieldSchema> Fields);
