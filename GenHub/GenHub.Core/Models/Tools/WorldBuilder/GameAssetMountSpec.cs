// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
namespace GenHub.Core.Models.Tools.WorldBuilder;

/// <summary>
/// Describes the layers mounted into a <see cref="GenHub.Core.Interfaces.Tools.WorldBuilder.IGameAssetFileSystem"/>.
/// Precedence from highest to lowest: explicit mod path, workspace loose files,
/// Zero Hour archives, Generals archives, bundled base (ZH_Generals inside a ZH install).
/// When <see cref="InstallationId"/> is set, missing roots are resolved from the
/// game installation service; explicit roots always win over resolved ones.
/// </summary>
/// <param name="ModPath">Optional explicit mod directory or .big file (highest precedence).</param>
/// <param name="WorkspaceRoot">Optional materialized profile workspace root.</param>
/// <param name="ZeroHourRoot">Optional Zero Hour install root.</param>
/// <param name="GeneralsRoot">Optional base Generals install root.</param>
/// <param name="BundledGeneralsRoot">Optional bundled Generals base inside a ZH install (lowest precedence).</param>
/// <param name="InstallationId">Optional installation id used to resolve missing roots.</param>
public sealed record GameAssetMountSpec(
    string? ModPath = null,
    string? WorkspaceRoot = null,
    string? ZeroHourRoot = null,
    string? GeneralsRoot = null,
    string? BundledGeneralsRoot = null,
    string? InstallationId = null);
