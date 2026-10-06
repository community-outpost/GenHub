using GenHub.Core.Constants;
using GenHub.Core.Services.Tools.GenHotkeys;
using System;
using System.Collections.Generic;

namespace GenHub.Features.Tools.GenHotkeys.Services;

/// <summary>
/// Resolves SAGE in-game tooltip description labels and text from retail CSF strings and CommandButton definitions.
/// Correlates command buttons and actions with their in-game DescriptLabel keys.
/// </summary>
public static class HotkeyTooltipResolver
{
    private static readonly Dictionary<string, string> RetailActionToTooltipMap = new(StringComparer.OrdinalIgnoreCase)
    {
        // Tactical / Global Command Buttons
        ["CONTROLBAR:Stop"] = "CONTROLBAR:ToolTipStop",
        ["CONTROLBAR:Guard"] = "CONTROLBAR:ToolTipGuard",
        ["CONTROLBAR:AttackMove"] = "CONTROLBAR:ToolTipAttackMove",
        ["CONTROLBAR:DisarmMinesAtPosition"] = "CONTROLBAR:ToolTipDisarmMinesAtPosition",
        ["CONTROLBAR:Evacuate"] = "CONTROLBAR:ToolTipEvacuate",
        ["CONTROLBAR:Sell"] = "CONTROLBAR:ToolTipSell",
        ["CONTROLBAR:AirGuard"] = "CONTROLBAR:ToolTipGuardFlyingUnitsOnly",
        ["CONTROLBAR:RallyPoint"] = "CONTROLBAR:ToolTipRallyPoint",
        ["CONTROLBAR:CaptureBuilding"] = "CONTROLBAR:ToolTipCaptureBuilding",
        ["CONTROLBAR:FireAtWill"] = "CONTROLBAR:ToolTipFireAtWill",
        ["CONTROLBAR:HoldFire"] = "CONTROLBAR:ToolTipHoldFire",

        // USA Common Units & Buildings
        ["CONTROLBAR:ConstructAmericaDozer"] = "CONTROLBAR:ToolTipUSABuildDozer",
        ["CONTROLBAR:ConstructAmericaCrusaderTank"] = "CONTROLBAR:ToolTipUSABuildCrusader",
        ["CONTROLBAR:ConstructAmericaPaladinTank"] = "CONTROLBAR:ToolTipUSABuildPaladin",
        ["CONTROLBAR:ConstructAmericaTankHumvee"] = "CONTROLBAR:ToolTipUSABuildHumvee",
        ["CONTROLBAR:ConstructAmericaTankAvenger"] = "CONTROLBAR:ToolTipUSABuildAvenger",
        ["CONTROLBAR:ConstructAmericaTankAmbulance"] = "CONTROLBAR:ToolTipUSABuildAmbulance",
        ["CONTROLBAR:ConstructAmericaInfantryRanger"] = "CONTROLBAR:ToolTipUSABuildRanger",
        ["CONTROLBAR:ConstructAmericaInfantryMissileDefender"] = "CONTROLBAR:ToolTipUSABuildMissileDefender",
        ["CONTROLBAR:ConstructAmericaInfantryColonelBurton"] = "CONTROLBAR:ToolTipUSABuildColonelBurton",
        ["CONTROLBAR:ConstructAmericaInfantryPathfinder"] = "CONTROLBAR:ToolTipUSABuildPathfinder",
        ["CONTROLBAR:ConstructAmericaInfantryPilot"] = "CONTROLBAR:ToolTipUSABuildPilot",
        ["CONTROLBAR:ConstructAmericaVehicleTomahawk"] = "CONTROLBAR:ToolTipUSABuildTomahawk",
        ["CONTROLBAR:ConstructAmericaVehicleMicrowave"] = "CONTROLBAR:ToolTipUSABuildMicrowaveTank",
        ["CONTROLBAR:ConstructAmericaJetRaptor"] = "CONTROLBAR:ToolTipUSABuildRaptor",
        ["CONTROLBAR:ConstructAmericaJetStealthFighter"] = "CONTROLBAR:ToolTipUSABuildStealthFighter",
        ["CONTROLBAR:ConstructAmericaJetAurora"] = "CONTROLBAR:ToolTipUSABuildAurora",
        ["CONTROLBAR:ConstructAmericaVehicleChinook"] = "CONTROLBAR:ToolTipUSABuildChinook",
        ["CONTROLBAR:ConstructAmericaVehicleComanche"] = "CONTROLBAR:ToolTipUSABuildComanche",
        ["CONTROLBAR:ConstructAmericaCommandCenter"] = "CONTROLBAR:ToolTipUSABuildCommandCenter",
        ["CONTROLBAR:ConstructAmericaPowerPlant"] = "CONTROLBAR:ToolTipUSABuildPowerPlant",
        ["CONTROLBAR:ConstructAmericaSupplyCenter"] = "CONTROLBAR:ToolTipUSABuildSupplyCenter",
        ["CONTROLBAR:ConstructAmericaBarracks"] = "CONTROLBAR:ToolTipUSABuildBarracks",
        ["CONTROLBAR:ConstructAmericaWarFactory"] = "CONTROLBAR:ToolTipUSABuildWarFactory",
        ["CONTROLBAR:ConstructAmericaAirfield"] = "CONTROLBAR:ToolTipUSABuildAirfield",
        ["CONTROLBAR:ConstructAmericaStrategyCenter"] = "CONTROLBAR:ToolTipUSABuildStrategyCenter",
        ["CONTROLBAR:ConstructAmericaSupplyDropZone"] = "CONTROLBAR:ToolTipUSABuildSupplyDropZone",
        ["CONTROLBAR:ConstructAmericaParticleCannonUplink"] = "CONTROLBAR:ToolTipUSABuildParticleCannonUplink",
        ["CONTROLBAR:ConstructAmericaPatriotBattery"] = "CONTROLBAR:ToolTipUSABuildPatriotBattery",
        ["CONTROLBAR:ConstructAmericaFireBase"] = "CONTROLBAR:ToolTipUSABuildFireBase",

        // China Common Units & Buildings
        ["CONTROLBAR:ConstructChinaDozer"] = "CONTROLBAR:ToolTipChinaBuildDozer",
        ["CONTROLBAR:ConstructChinaTankBattleMaster"] = "CONTROLBAR:ToolTipChinaBuildBattleMaster",
        ["CONTROLBAR:ConstructChinaTankOverlord"] = "CONTROLBAR:ToolTipChinaBuildOverlord",
        ["CONTROLBAR:ConstructChinaTankDragon"] = "CONTROLBAR:ToolTipChinaBuildDragonTank",
        ["CONTROLBAR:ConstructChinaTankGattling"] = "CONTROLBAR:ToolTipChinaBuildGattlingTank",
        ["CONTROLBAR:ConstructChinaVehicleTroopCrawler"] = "CONTROLBAR:ToolTipChinaBuildTroopCrawler",
        ["CONTROLBAR:ConstructChinaVehicleECM"] = "CONTROLBAR:ToolTipChinaBuildECMTank",
        ["CONTROLBAR:ConstructChinaVehicleInferno"] = "CONTROLBAR:ToolTipChinaBuildInfernoCannon",
        ["CONTROLBAR:ConstructChinaVehicleNukeLauncher"] = "CONTROLBAR:ToolTipChinaBuildNukeLauncher",
        ["CONTROLBAR:ConstructChinaInfantryRedGuard"] = "CONTROLBAR:ToolTipChinaBuildRedGuard",
        ["CONTROLBAR:ConstructChinaInfantryTankHunter"] = "CONTROLBAR:ToolTipChinaBuildTankHunter",
        ["CONTROLBAR:ConstructChinaInfantryHacker"] = "CONTROLBAR:ToolTipChinaBuildHacker",
        ["CONTROLBAR:ConstructChinaInfantryBlackLotus"] = "CONTROLBAR:ToolTipChinaBuildBlackLotus",
        ["CONTROLBAR:ConstructChinaJetMiG"] = "CONTROLBAR:ToolTipChinaBuildMiG",
        ["CONTROLBAR:ConstructChinaVehicleHelix"] = "CONTROLBAR:ToolTipChinaBuildHelix",
        ["CONTROLBAR:ConstructChinaCommandCenter"] = "CONTROLBAR:ToolTipChinaBuildCommandCenter",
        ["CONTROLBAR:ConstructChinaPowerPlant"] = "CONTROLBAR:ToolTipChinaBuildPowerPlant",
        ["CONTROLBAR:ConstructChinaSupplyCenter"] = "CONTROLBAR:ToolTipChinaBuildSupplyCenter",
        ["CONTROLBAR:ConstructChinaBarracks"] = "CONTROLBAR:ToolTipChinaBuildBarracks",
        ["CONTROLBAR:ConstructChinaWarFactory"] = "CONTROLBAR:ToolTipChinaBuildWarFactory",
        ["CONTROLBAR:ConstructChinaAirfield"] = "CONTROLBAR:ToolTipChinaBuildAirfield",
        ["CONTROLBAR:ConstructChinaPropagandaCenter"] = "CONTROLBAR:ToolTipChinaBuildPropagandaCenter",
        ["CONTROLBAR:ConstructChinaSpeakerTower"] = "CONTROLBAR:ToolTipChinaBuildSpeakerTower",
        ["CONTROLBAR:ConstructChinaGattlingCannon"] = "CONTROLBAR:ToolTipChinaBuildGattlingCannon",
        ["CONTROLBAR:ConstructChinaBunker"] = "CONTROLBAR:ToolTipChinaBuildBunker",
        ["CONTROLBAR:ConstructChinaNuclearSilo"] = "CONTROLBAR:ToolTipChinaBuildNuclearSilo",

        // GLA Common Units & Buildings
        ["CONTROLBAR:ConstructGLAWorker"] = "CONTROLBAR:ToolTipGLABuildWorker",
        ["CONTROLBAR:ConstructGLATankScorpion"] = "CONTROLBAR:ToolTipGLABuildScorpionTank",
        ["CONTROLBAR:ConstructGLATankMarauder"] = "CONTROLBAR:ToolTipGLABuildMarauderTank",
        ["CONTROLBAR:ConstructGLATankTechnical"] = "CONTROLBAR:ToolTipGLABuildTechnical",
        ["CONTROLBAR:ConstructGLAVehicleRadarVan"] = "CONTROLBAR:ToolTipGLABuildRadarVan",
        ["CONTROLBAR:ConstructGLAVehicleRocketBuggy"] = "CONTROLBAR:ToolTipGLABuildRocketBuggy",
        ["CONTROLBAR:ConstructGLAVehicleQuadCannon"] = "CONTROLBAR:ToolTipGLABuildQuadCannon",
        ["CONTROLBAR:ConstructGLAVehicleScudLauncher"] = "CONTROLBAR:ToolTipGLABuildScudLauncher",
        ["CONTROLBAR:ConstructGLAVehicleBattleBus"] = "CONTROLBAR:ToolTipGLABuildBattleBus",
        ["CONTROLBAR:ConstructGLAVehicleBombTruck"] = "CONTROLBAR:ToolTipGLABuildBombTruck",
        ["CONTROLBAR:ConstructGLAVehicleCombatCycle"] = "CONTROLBAR:ToolTipGLABuildCombatCycle",
        ["CONTROLBAR:ConstructGLAInfantryRebel"] = "CONTROLBAR:ToolTipGLABuildRebel",
        ["CONTROLBAR:ConstructGLAInfantryRPGPlayer"] = "CONTROLBAR:ToolTipGLABuildRPGPlayer",
        ["CONTROLBAR:ConstructGLAInfantryTerrorist"] = "CONTROLBAR:ToolTipGLABuildTerrorist",
        ["CONTROLBAR:ConstructGLAInfantryAngryMob"] = "CONTROLBAR:ToolTipGLABuildAngryMob",
        ["CONTROLBAR:ConstructGLAInfantryJarmenKell"] = "CONTROLBAR:ToolTipGLABuildJarmenKell",
        ["CONTROLBAR:ConstructGLAInfantryHijacker"] = "CONTROLBAR:ToolTipGLABuildHijacker",
        ["CONTROLBAR:ConstructGLAInfantrySaboteur"] = "CONTROLBAR:ToolTipGLABuildSaboteur",
        ["CONTROLBAR:ConstructGLACommandCenter"] = "CONTROLBAR:ToolTipGLABuildCommandCenter",
        ["CONTROLBAR:ConstructGLASupplyStash"] = "CONTROLBAR:ToolTipGLABuildSupplyStash",
        ["CONTROLBAR:ConstructGLABarracks"] = "CONTROLBAR:ToolTipGLABuildBarracks",
        ["CONTROLBAR:ConstructGLAArmsDealer"] = "CONTROLBAR:ToolTipGLABuildArmsDealer",
        ["CONTROLBAR:ConstructGLAPalace"] = "CONTROLBAR:ToolTipGLABuildPalace",
        ["CONTROLBAR:ConstructGLABlackMarket"] = "CONTROLBAR:ToolTipGLABuildBlackMarket",
        ["CONTROLBAR:ConstructGLAScudStorm"] = "CONTROLBAR:ToolTipGLABuildScudStorm",
        ["CONTROLBAR:ConstructGLATunnelNetwork"] = "CONTROLBAR:ToolTipGLABuildTunnelNetwork",
        ["CONTROLBAR:ConstructGLAStingerSite"] = "CONTROLBAR:ToolTipGLABuildStingerSite",
    };

    /// <summary>
    /// Resolves the in-game tooltip description label and text for an action.
    /// </summary>
    /// <param name="hotkeyString">The primary CSF string label (e.g. "CONTROLBAR:ConstructAmericaDozer").</param>
    /// <param name="iconName">The icon identifier (e.g. "USADozer").</param>
    /// <param name="explicitTooltipString">An explicit tooltip label if specified in data, or null.</param>
    /// <param name="refCsf">The reference CSF file to lookup text from.</param>
    /// <returns>A tuple of (tooltipLabel, tooltipText).</returns>
    public static (string TooltipLabel, string? TooltipText) ResolveTooltip(
        string hotkeyString,
        string iconName,
        string? explicitTooltipString,
        CsfFile? refCsf)
    {
        // 1. Check explicit tooltip label first
        if (!string.IsNullOrWhiteSpace(explicitTooltipString))
        {
            var text = refCsf?.GetString(explicitTooltipString);
            return (explicitTooltipString, text);
        }

        // 2. Check known direct retail mapping table
        if (!string.IsNullOrWhiteSpace(hotkeyString) && RetailActionToTooltipMap.TryGetValue(hotkeyString, out var mappedLabel))
        {
            var text = refCsf?.GetString(mappedLabel);
            return (mappedLabel, text);
        }

        // 3. Check heuristic variations against CSF
        if (refCsf != null && !string.IsNullOrWhiteSpace(hotkeyString))
        {
            var colonIdx = hotkeyString.IndexOf(':');
            var baseName = colonIdx >= 0 ? hotkeyString[(colonIdx + 1)..] : hotkeyString;

            var candidates = new[]
            {
                $"CONTROLBAR:ToolTip{baseName}",
                $"CONTROLBAR:ToolTip_{baseName}",
                $"CONTROLBAR:ToolTip{iconName}",
                $"CONTROLBAR:ToolTip_{iconName}",
                $"UPGRADE:ToolTip{baseName}",
                $"OBJECT:ToolTip{baseName}",
            };

            foreach (var candidate in candidates)
            {
                var text = refCsf.GetString(candidate);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return (candidate, text);
                }
            }
        }

        // 4. Default synthetic label if not present in retail CSF
        var defaultLabel = !string.IsNullOrWhiteSpace(hotkeyString)
            ? (hotkeyString.StartsWith("CONTROLBAR:", StringComparison.OrdinalIgnoreCase)
                ? $"CONTROLBAR:ToolTip{hotkeyString["CONTROLBAR:".Length..]}"
                : $"CONTROLBAR:ToolTip{hotkeyString}")
            : $"CONTROLBAR:ToolTip{iconName}";

        return (defaultLabel, null);
    }
}
