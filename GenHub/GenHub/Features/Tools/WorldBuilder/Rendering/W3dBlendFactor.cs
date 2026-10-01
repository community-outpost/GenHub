// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
namespace GenHub.Features.Tools.WorldBuilder.Rendering;

/// <summary>
/// Blend factor for a mapped W3D shader.
/// </summary>
public enum W3dBlendFactor
{
    /// <summary>Zero.</summary>
    Zero = 0,

    /// <summary>One.</summary>
    One = 1,

    /// <summary>Source color.</summary>
    SrcColor = 2,

    /// <summary>One minus source color.</summary>
    OneMinusSrcColor = 3,

    /// <summary>Source alpha.</summary>
    SrcAlpha = 4,

    /// <summary>One minus source alpha.</summary>
    OneMinusSrcAlpha = 5,
}
