// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
namespace GenHub.Features.Tools.WorldBuilder.Rendering;

/// <summary>
/// Depth comparison for a mapped W3D shader.
/// </summary>
public enum W3dDepthFunction
{
    /// <summary>Never passes.</summary>
    Never = 0,

    /// <summary>Passes when less.</summary>
    Less = 1,

    /// <summary>Passes when equal.</summary>
    Equal = 2,

    /// <summary>Passes when less or equal.</summary>
    Lequal = 3,

    /// <summary>Passes when greater.</summary>
    Greater = 4,

    /// <summary>Passes when not equal.</summary>
    NotEqual = 5,

    /// <summary>Passes when greater or equal.</summary>
    Gequal = 6,

    /// <summary>Always passes.</summary>
    Always = 7,
}
