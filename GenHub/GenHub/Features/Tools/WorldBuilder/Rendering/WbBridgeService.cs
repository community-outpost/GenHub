// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using GenHub.Core.Interfaces.Tools.WorldBuilder;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.WorldBuilder;
using GenHub.Core.Services.Tools.WorldBuilder;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.WorldBuilder.Rendering;

/// <summary>
/// Renders bridges from Roads.ini templates: the pristine deck model scaled
/// along the span plus scaffold support models at both ends, resolved through
/// the W3D model pipeline. Spans without resolvable art are skipped.
/// </summary>
/// <param name="roads">The road and bridge template catalog.</param>
/// <param name="templates">The thing template catalog for tower models.</param>
/// <param name="models">The model draw builder.</param>
/// <param name="logger">The logger.</param>
public sealed class WbBridgeService(
    IRoadCatalog roads,
    IThingTemplateCatalog templates,
    WbModelRenderService models,
    ILogger<WbBridgeService> logger)
{
    private readonly IRoadCatalog _roads = roads;
    private readonly IThingTemplateCatalog _templates = templates;
    private readonly WbModelRenderService _models = models;
    private readonly ILogger<WbBridgeService> _logger = logger;

    /// <summary>
    /// Builds deck and tower draws for every bridge on the map.
    /// </summary>
    /// <param name="map">The map document.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The baked draws.</returns>
    public async Task<OperationResult<IReadOnlyList<WbModelDraw>>> BuildBridgesAsync(WorldBuilderMap map, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(map);
        var bridges = MapOverlayTools.GetBridges(map);
        var terrain = map.Terrain;
        var draws = await Task.Run(() => BuildCoreAsync(terrain, bridges, cancellationToken), cancellationToken).ConfigureAwait(false);
        return OperationResult<IReadOnlyList<WbModelDraw>>.CreateSuccess(draws);
    }

    private async Task<List<WbModelDraw>> BuildCoreAsync(MapTerrainData terrain, List<BridgeSegment> bridges, CancellationToken cancellationToken)
    {
        var draws = new List<WbModelDraw>();
        foreach (var bridge in bridges)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var built = await BuildBridgeAsync(terrain, bridge, cancellationToken).ConfigureAwait(false);
            draws.AddRange(built);
        }

        return draws;
    }

    private async Task<IReadOnlyList<WbModelDraw>> BuildBridgeAsync(MapTerrainData terrain, BridgeSegment bridge, CancellationToken cancellationToken)
    {
        var info = _roads.FindBridge(bridge.Template);
        if (info == null)
        {
            _logger.LogDebug("Skipping bridge with unknown template {Template}", bridge.Template);
            return [];
        }

        var scale = info.BridgeScale is > 0 ? info.BridgeScale.Value : 1.0f;
        var midX = (bridge.X1 + bridge.X2) / 2.0f;
        var midY = (bridge.Y1 + bridge.Y2) / 2.0f;
        var startZ = WbPicking.GroundHeightFeet(terrain, bridge.X1, bridge.Y1) + bridge.Z1;
        var endZ = WbPicking.GroundHeightFeet(terrain, bridge.X2, bridge.Y2) + bridge.Z2;
        var midZ = (startZ + endZ) / 2.0f;
        var yaw = MathF.Atan2(bridge.Y2 - bridge.Y1, bridge.X2 - bridge.X1);
        var draws = new List<WbModelDraw>();
        if (!string.IsNullOrWhiteSpace(info.BridgeModelName))
        {
            var deck = await _models.BuildSingleAsync(info.BridgeModelName, midX, midY, midZ, yaw, scale, cancellationToken).ConfigureAwait(false);
            if (deck.Success && deck.Data != null)
            {
                draws.AddRange(deck.Data);
            }
        }

        var towerModel = ResolveTowerModel(info);
        if (towerModel != null)
        {
            var start = await _models.BuildSingleAsync(towerModel, bridge.X1, bridge.Y1, startZ, yaw, scale, cancellationToken).ConfigureAwait(false);
            if (start.Success && start.Data != null)
            {
                draws.AddRange(start.Data);
            }

            var end = await _models.BuildSingleAsync(towerModel, bridge.X2, bridge.Y2, endZ, yaw, scale, cancellationToken).ConfigureAwait(false);
            if (end.Success && end.Data != null)
            {
                draws.AddRange(end.Data);
            }
        }

        return draws;
    }

    private string? ResolveTowerModel(BridgeInfo info)
    {
        var templateName = info.ScaffoldSupportObjectName ?? info.ScaffoldObjectName;
        if (string.IsNullOrWhiteSpace(templateName))
        {
            return null;
        }

        return _templates.FindByName(templateName)?.ModelName;
    }
}
