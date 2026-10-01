// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
namespace GenHub.Core.Models.Tools.WorldBuilder;

/// <summary>
/// The field-parse table for one block token, mirroring the engine FieldParse tables:
/// known fields, an optional wildcard terminal entry that keeps unknown fields with the
/// token as key, and the fields that open End-terminated nested scopes.
/// </summary>
/// <param name="KnownFields">Field keys handled by this table (case-sensitive, like findFieldParse).</param>
/// <param name="HasWildcard">True when unknown fields are kept raw (wildcard terminal entry); false drops them with a diagnostic.</param>
/// <param name="SubBlockOpeners">Field keys that open an End-terminated module scope (Key = Type Tag form).</param>
/// <param name="NestedScopeOpeners">Field keys that open a bare End-terminated scope (ArmorSet, WeaponSet, Prerequisites); null means none.</param>
public sealed record SageIniFieldTable(
    IReadOnlySet<string> KnownFields,
    bool HasWildcard,
    IReadOnlySet<string> SubBlockOpeners,
    IReadOnlySet<string>? NestedScopeOpeners = null);
