// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using GenHub.Core.Models.Results;

namespace GenHub.Core.Models.Tools.WorldBuilder;

/// <summary>
/// Options controlling one SAGE INI parse: the block dispatch table, strict versus
/// tolerant failure handling, cross-file inheritance parents, field tables, and the
/// include-file reader used to expand #include directives.
/// </summary>
/// <param name="BlockTable">Recognized block tokens; null selects the full engine table.</param>
/// <param name="TolerateBlockFailures">True for loadWB semantics (skip failing blocks and continue); false aborts the file on the first failure like INI::load.</param>
/// <param name="KnownBlocks">Previously loaded blocks by template name for inheritance; parents must already be present like the engine requires.</param>
/// <param name="FieldTables">Per-token field tables; null selects the built-in defaults (wildcard for Object-family full tables, strict visual-only for reskins).</param>
/// <param name="IncludeReader">Resolves and reads an include target; takes the including file and the raw include path, returns the resolved path plus the text. Null rejects #include lines.</param>
public sealed record SageIniParseOptions(
    IReadOnlySet<string>? BlockTable = null,
    bool TolerateBlockFailures = false,
    IReadOnlyDictionary<string, SageIniBlock>? KnownBlocks = null,
    IReadOnlyDictionary<string, SageIniFieldTable>? FieldTables = null,
    Func<string, string, CancellationToken, Task<OperationResult<(string ResolvedPath, string Text)>>>? IncludeReader = null);
