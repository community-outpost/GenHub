// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
namespace GenHub.Core.Models.Tools.WorldBuilder;

/// <summary>
/// Parsed bone hierarchy: pivots in file order.
/// </summary>
/// <param name="Name">The hierarchy name.</param>
/// <param name="Pivots">Pivots in file order.</param>
public sealed record W3dHierarchy(string Name, IReadOnlyList<W3dPivot> Pivots);
