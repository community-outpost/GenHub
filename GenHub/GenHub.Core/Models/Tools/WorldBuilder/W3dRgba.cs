// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
namespace GenHub.Core.Models.Tools.WorldBuilder;

/// <summary>
/// Eight-bit RGBA color from W3D vertex material and diffuse records.
/// </summary>
/// <param name="R">Red byte.</param>
/// <param name="G">Green byte.</param>
/// <param name="B">Blue byte.</param>
/// <param name="A">Alpha byte.</param>
public sealed record W3dRgba(byte R, byte G, byte B, byte A);
