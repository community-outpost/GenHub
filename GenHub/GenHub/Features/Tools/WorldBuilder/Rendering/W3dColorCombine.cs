// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
namespace GenHub.Features.Tools.WorldBuilder.Rendering;

/// <summary>
/// Color combine for a mapped W3D shader primary gradient.
/// </summary>
public enum W3dColorCombine
{
    /// <summary>Texture only (decal).</summary>
    Decal = 0,

    /// <summary>Texture times vertex color.</summary>
    Modulate = 1,

    /// <summary>Texture plus vertex color.</summary>
    Add = 2,

    /// <summary>Twice texture times vertex color.</summary>
    Modulate2X = 3,
}
