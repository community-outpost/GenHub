// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
namespace GenHub.Core.Models.Tools.WorldBuilder;

/// <summary>
/// Fixed-pipeline vertex material: colors are bytes, coefficients are floats.
/// </summary>
/// <param name="Name">The material name; empty when unnamed.</param>
/// <param name="Attributes">Attribute flags.</param>
/// <param name="Ambient">Ambient color.</param>
/// <param name="Diffuse">Diffuse color.</param>
/// <param name="Specular">Specular color.</param>
/// <param name="Emissive">Emissive color.</param>
/// <param name="Shininess">Specular shininess.</param>
/// <param name="Opacity">Opacity.</param>
/// <param name="Translucency">Translucency.</param>
public sealed record W3dVertexMaterial(
    string Name,
    uint Attributes,
    W3dRgba Ambient,
    W3dRgba Diffuse,
    W3dRgba Specular,
    W3dRgba Emissive,
    float Shininess,
    float Opacity,
    float Translucency);
