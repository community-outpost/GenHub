// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using System.Collections.Generic;

namespace GenHub.Features.Tools.WorldBuilder.Rendering;

/// <summary>
/// CPU-side model render data: one draw per placed (mesh, pass) pair.
/// </summary>
/// <param name="Draws">Draws in placement order.</param>
public sealed record WbModelRenderData(IReadOnlyList<WbModelDraw> Draws);
