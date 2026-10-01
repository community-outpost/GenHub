// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
namespace GenHub.Core.Models.Tools.WorldBuilder;

/// <summary>
/// Editor-relevant view of one Bridge block, typed per
/// TerrainRoadType::m_terrainBridgeFieldParseTable.
/// </summary>
/// <param name="Name">The bridge template name.</param>
/// <param name="BridgeScale">The bridge model scale; null when absent or unparsable.</param>
/// <param name="ScaffoldObjectName">The scaffold object template name; null when absent.</param>
/// <param name="ScaffoldSupportObjectName">The scaffold support object template name; null when absent.</param>
/// <param name="RadarColor">The radar-map color as ARGB; null when absent or unparsable.</param>
/// <param name="TransitionEffectsHeight">The damage-transition effect height; null when absent or unparsable.</param>
/// <param name="NumFXPerType">The effect count per damage transition; null when absent or unparsable.</param>
/// <param name="BridgeModelName">The pristine bridge deck model; null when absent.</param>
/// <param name="Texture">The pristine bridge texture; null when absent.</param>
/// <param name="BridgeModelNameDamaged">The damaged bridge deck model; null when absent.</param>
/// <param name="TextureDamaged">The damaged bridge texture; null when absent.</param>
/// <param name="BridgeModelNameReallyDamaged">The heavily damaged bridge deck model; null when absent.</param>
/// <param name="TextureReallyDamaged">The heavily damaged bridge texture; null when absent.</param>
/// <param name="BridgeModelNameBroken">The broken bridge deck model; null when absent.</param>
/// <param name="TextureBroken">The broken bridge texture; null when absent.</param>
/// <param name="TowerObjectNames">Tower object names from-left, from-right, to-left, to-right.</param>
/// <param name="DamagedToSound">The sound played on a damage transition; null when absent.</param>
/// <param name="RepairedToSound">The sound played on a repair transition; null when absent.</param>
/// <param name="TransitionToOCL">Raw TransitionToOCL lines in source order.</param>
/// <param name="TransitionToFX">Raw TransitionToFX lines in source order.</param>
/// <param name="BridgeHoleAreaPercentage">The fraction of the deck removed as a hole when broken; null when absent or unparsable.</param>
public sealed record BridgeInfo(
    string Name,
    float? BridgeScale,
    string? ScaffoldObjectName,
    string? ScaffoldSupportObjectName,
    int? RadarColor,
    float? TransitionEffectsHeight,
    int? NumFXPerType,
    string? BridgeModelName,
    string? Texture,
    string? BridgeModelNameDamaged,
    string? TextureDamaged,
    string? BridgeModelNameReallyDamaged,
    string? TextureReallyDamaged,
    string? BridgeModelNameBroken,
    string? TextureBroken,
    IReadOnlyList<string> TowerObjectNames,
    string? DamagedToSound,
    string? RepairedToSound,
    IReadOnlyList<string> TransitionToOCL,
    IReadOnlyList<string> TransitionToFX,
    float? BridgeHoleAreaPercentage);
