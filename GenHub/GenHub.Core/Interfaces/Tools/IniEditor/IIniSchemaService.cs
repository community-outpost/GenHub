using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.IniEditor;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Interfaces.Tools.IniEditor;

/// <summary>
/// Provides schema metadata for known Generals and Zero Hour INI block types.
/// Unknown blocks and fields remain editable generically.
/// </summary>
public interface IIniSchemaService
{
    /// <summary>
    /// Gets the schema for a block type, or null when the block is generic.
    /// </summary>
    /// <param name="blockType">The block type name.</param>
    /// <returns>The block schema, or null for unknown blocks.</returns>
    IniBlockSchema? GetBlockSchema(string blockType);

    /// <summary>
    /// Tries to get the schema for a field within a block type.
    /// </summary>
    /// <param name="blockType">The block type name.</param>
    /// <param name="key">The field key.</param>
    /// <param name="schema">The field schema when known.</param>
    /// <returns>True when the field is known for the block type.</returns>
    bool TryGetField(string blockType, string key, out IniFieldSchema? schema);

    /// <summary>
    /// Gets all block types with schema assistance.
    /// </summary>
    IReadOnlyList<IniBlockSchema> BlockSchemas { get; }
}
