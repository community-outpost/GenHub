// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
namespace GenHub.Core.Models.Tools.WorldBuilder;

/// <summary>
/// A parsed SAGE INI block: header tokens plus raw fields and nested sub-blocks.
/// Inheritance (ObjectReskin, ObjectExtend, ChildObject) is resolved at parse time by
/// copying the parent block, mirroring ThingFactory::parseObjectDefinition.
/// </summary>
/// <param name="BlockToken">The block type token (for example, Object).</param>
/// <param name="Name">The block name; empty for nameless singleton blocks such as GameData.</param>
/// <param name="ParentName">The inheritance parent name for reskin, extend, and child blocks; otherwise null.</param>
/// <param name="Fields">The raw fields in source order.</param>
/// <param name="SubBlocks">The nested End-terminated scopes in source order.</param>
/// <param name="SourceFile">The file the block header was read from.</param>
/// <param name="LineNumber">The 1-based line number of the block header.</param>
public sealed record SageIniBlock(
    string BlockToken,
    string Name,
    string? ParentName,
    IReadOnlyList<SageIniField> Fields,
    IReadOnlyList<SageIniSubBlock> SubBlocks,
    string SourceFile,
    int LineNumber);
