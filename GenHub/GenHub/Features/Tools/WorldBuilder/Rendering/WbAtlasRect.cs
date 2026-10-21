// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
namespace GenHub.Features.Tools.WorldBuilder.Rendering;

/// <summary>
/// Normalized atlas rectangle.
/// </summary>
/// <param name="MinU">The minimum U.</param>
/// <param name="MinV">The minimum V.</param>
/// <param name="MaxU">The maximum U.</param>
/// <param name="MaxV">The maximum V.</param>
public readonly record struct WbAtlasRect(float MinU, float MinV, float MaxU, float MaxV);
