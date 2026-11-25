namespace GenHub.Core.Models.Tools.IniEditor;

/// <summary>
/// A single indexed INI block used for reference pickers and clone-from-reference actions.
/// </summary>
/// <param name="BlockType">The block type name.</param>
/// <param name="Name">The block name.</param>
/// <param name="Source">The origin of the entry.</param>
/// <param name="SourceLabel">The display label of the origin.</param>
/// <param name="FilePath">The source file path, when the entry comes from a file.</param>
public sealed record IniReferenceEntry(string BlockType, string Name, IniReferenceSource Source, string SourceLabel, string? FilePath);
