using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Platform;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Tools.GenHotkeys;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Tools.GenHotkeys;
using GenHub.Core.Services.Tools.GenHotkeys;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Tools.GenHotkeys.Services;

/// <summary>
/// Service for loading C&amp;C Generals and Zero Hour tech tree structures, icons, and localized hotkey strings.
/// </summary>
public class TechTreeService(ILogger<TechTreeService> logger) : ITechTreeService
{
    private const string AssetsFolder = "Assets";
    private const string GenHotkeysFolder = "GenHotkeys";
    private const string GenHubFolder = "GenHub";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly string[] FactionSubdirs = [string.Empty, "USA/", "PRC/", "GLA/"];
    private static readonly string[] IconExtensions = [".webp", ".png"];

    private readonly ConcurrentDictionary<GameType, IReadOnlyList<HotkeyFaction>> _cache = new();
    private readonly ConcurrentDictionary<string, byte[]?> _iconCache = new();

    /// <inheritdoc />
    public async Task<IReadOnlyList<HotkeyFaction>> LoadTechTreeAsync(
        GameType gameType,
        CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(gameType, out var cached))
        {
            return cached;
        }

        var relativePath = gameType == GameType.Generals
            ? GenHotkeysConstants.TechTreeGenerals
            : GenHotkeysConstants.TechTreeGeneralsZh;

        var jsonString = await LoadAssetStringAsync(relativePath, cancellationToken);
        if (string.IsNullOrWhiteSpace(jsonString))
        {
            logger.LogWarning("Failed to load tech tree JSON for {GameType} from {Path}", gameType, relativePath);
            return [];
        }

        var root = JsonSerializer.Deserialize<TechTreeRoot>(jsonString, JsonOptions);
        if (root?.TechTree == null)
        {
            return [];
        }

        var refCsf = LoadReferenceCsf(gameType);
        var factions = new List<HotkeyFaction>();

        foreach (var factionJson in root.TechTree)
        {
            var faction = new HotkeyFaction
            {
                ShortName = factionJson.ShortName,
                DisplayName = factionJson.DisplayName,
                DisplayNameDescription = factionJson.DisplayNameDescription,
            };

            AddGameObjects(faction.GameObjects, factionJson.Buildings, HotkeyCategory.Buildings, refCsf);
            AddGameObjects(faction.GameObjects, factionJson.Infantry, HotkeyCategory.Infantry, refCsf);
            AddGameObjects(faction.GameObjects, factionJson.Vehicles, HotkeyCategory.Vehicles, refCsf);
            AddGameObjects(faction.GameObjects, factionJson.Aircrafts, HotkeyCategory.Aircrafts, refCsf);

            factions.Add(faction);
        }

        _cache[gameType] = factions;
        return factions;
    }

    /// <inheritdoc />
    public async Task<byte[]?> GetIconBytesAsync(
        string iconName,
        GameType gameType,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(iconName))
        {
            return null;
        }

        var profileDir = gameType == GameType.Generals ? "Generals" : "GeneralsZH";
        var cacheKey = $"{profileDir}/{iconName}";

        if (_iconCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var searchDirs = new List<string> { profileDir };
        if (gameType == GameType.ZeroHour)
        {
            searchDirs.Add("Generals");
        }

        var candidateNames = BuildCandidateIconNames(iconName);

        foreach (var relativePath in EnumerateCandidateIconPaths(searchDirs, candidateNames))
        {
            using var stream = TryOpenAssetStream(relativePath);
            if (stream == null)
            {
                continue;
            }

            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms, cancellationToken);
            var data = ms.ToArray();
            _iconCache[cacheKey] = data;
            return data;
        }

        _iconCache[cacheKey] = null;
        return null;
    }

    private static List<string> BuildCandidateIconNames(string iconName)
    {
        var candidateNames = new List<string> { iconName };
        if (iconName.Length > 3 &&
            (iconName.StartsWith("USA", StringComparison.OrdinalIgnoreCase) ||
             iconName.StartsWith("PRC", StringComparison.OrdinalIgnoreCase) ||
             iconName.StartsWith("GLA", StringComparison.OrdinalIgnoreCase)))
        {
            candidateNames.Add(iconName[3..]);
        }

        return candidateNames;
    }

    private static IEnumerable<string> EnumerateCandidateIconPaths(
        IEnumerable<string> searchDirs,
        IEnumerable<string> candidateNames)
    {
        foreach (var dir in searchDirs)
        {
            foreach (var sub in FactionSubdirs)
            {
                foreach (var name in candidateNames)
                {
                    foreach (var ext in IconExtensions)
                    {
                        yield return $"Profiles/{dir}/Icons/{sub}{name}{ext}";
                    }
                }
            }
        }
    }

    private static void AddGameObjects(
        List<HotkeyGameObject> targetList,
        List<TechTreeGameObjectJson> sourceList,
        HotkeyCategory category,
        CsfFile? refCsf)
    {
        foreach (var objJson in sourceList)
        {
            targetList.Add(CreateGameObject(objJson, category, refCsf));
        }
    }

    private static HotkeyGameObject CreateGameObject(
        TechTreeGameObjectJson objJson,
        HotkeyCategory category,
        CsfFile? refCsf)
    {
        var cleanDisplayName = objJson.Name;
        if (refCsf != null && !string.IsNullOrEmpty(objJson.IngameName))
        {
            var localized = refCsf.GetString(objJson.IngameName);
            if (!string.IsNullOrWhiteSpace(localized))
            {
                cleanDisplayName = CsfFile.StripHotkey(localized);
            }
        }

        var gameObj = new HotkeyGameObject
        {
            Name = objJson.Name,
            IngameName = objJson.IngameName,
            DisplayName = cleanDisplayName,
            Category = category,
            IconName = objJson.Name,
        };

        var consolidatedLayouts = ConsolidateLayouts(objJson.KeyboardLayouts);

        foreach (var layoutJson in consolidatedLayouts)
        {
            var layout = new List<HotkeyAction>();
            foreach (var actJson in layoutJson)
            {
                layout.Add(CreateAction(actJson, refCsf));
            }

            gameObj.KeyboardLayouts.Add(layout);
        }

        return gameObj;
    }

    private static List<List<TechTreeActionJson>> ConsolidateLayouts(List<List<TechTreeActionJson>> layouts)
    {
        if (layouts == null || layouts.Count <= 1)
        {
            return layouts ?? [];
        }

        // Check if layouts represent sequential upgrade variants of the same command set
        // (e.g. Chinese structures where layout 2 duplicates 80-95% of layout 1 except for upgraded mines/hacks).
        // For distinct multi-page command cards (such as GLAWorker where layout 1 = real buildings and layout 2 = fake buildings),
        // the overlap is near zero and they remain separate layouts.
        var consolidated = new List<List<TechTreeActionJson>>();
        var currentMerged = new List<TechTreeActionJson>(layouts[0]);
        var currentKeys = new HashSet<string>(
            layouts[0].Select(GetActionKey),
            StringComparer.OrdinalIgnoreCase);

        for (var i = 1; i < layouts.Count; i++)
        {
            var nextLayout = layouts[i];
            if (IsUpgradeVariant(currentKeys, nextLayout))
            {
                MergeUpgradeLayout(currentMerged, currentKeys, nextLayout);
            }
            else
            {
                // Distinct layout (e.g. GLA Worker fake structures page)
                consolidated.Add(currentMerged);
                currentMerged = new List<TechTreeActionJson>(nextLayout);
                currentKeys = new HashSet<string>(
                    nextLayout.Select(GetActionKey),
                    StringComparer.OrdinalIgnoreCase);
            }
        }

        consolidated.Add(currentMerged);
        return consolidated;
    }

    private static string GetActionKey(TechTreeActionJson action) =>
        action.HotkeyString ?? action.IconName;

    private static bool IsUpgradeVariant(HashSet<string> currentKeys, List<TechTreeActionJson> nextLayout)
    {
        var nextKeys = new HashSet<string>(
            nextLayout.Select(GetActionKey),
            StringComparer.OrdinalIgnoreCase);

        var commonCount = nextKeys.Count(currentKeys.Contains);
        var minCount = Math.Min(currentKeys.Count, nextKeys.Count);
        if (minCount == 0)
        {
            return false;
        }

        var overlapRatio = (double)commonCount / minCount;
        return overlapRatio > 0.40;
    }

    private static void MergeUpgradeLayout(
        List<TechTreeActionJson> currentMerged,
        HashSet<string> currentKeys,
        List<TechTreeActionJson> nextLayout)
    {
        foreach (var act in nextLayout)
        {
            var key = GetActionKey(act);
            if (currentKeys.Add(key))
            {
                InsertAction(currentMerged, act);
            }
        }
    }

    private static void InsertAction(List<TechTreeActionJson> currentMerged, TechTreeActionJson act)
    {
        var insertIndex = FindInsertIndex(currentMerged, act);
        if (insertIndex >= 0 && insertIndex < currentMerged.Count)
        {
            currentMerged.Insert(insertIndex + 1, act);
            return;
        }

        var sellIndex = currentMerged.FindIndex(IsSellAction);
        if (sellIndex >= 0)
        {
            currentMerged.Insert(sellIndex, act);
        }
        else
        {
            currentMerged.Add(act);
        }
    }

    private static bool IsSellAction(TechTreeActionJson action) =>
        string.Equals(action.HotkeyString, "CONTROLBAR:Sell", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(action.IconName, "Sell", StringComparison.OrdinalIgnoreCase);

    private static int FindInsertIndex(List<TechTreeActionJson> list, TechTreeActionJson action)
    {
        // Neutron Mines -> right after Land Mines
        if (string.Equals(action.HotkeyString, "CONTROLBAR:UpgradeEMPMines", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(action.IconName, "PRCNeutronMines", StringComparison.OrdinalIgnoreCase))
        {
            return list.FindIndex(a =>
                string.Equals(a.HotkeyString, "CONTROLBAR:UpgradeChinaMines", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a.IconName, "PRCLandMine", StringComparison.OrdinalIgnoreCase));
        }

        // Satellite Hack 2 -> right after Satellite Hack 1
        if (string.Equals(action.HotkeyString, "CONTROLBAR:UpgradeChinaSatelliteHackTwo", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(action.IconName, "PRCSatelliteHack2", StringComparison.OrdinalIgnoreCase))
        {
            return list.FindIndex(a =>
                string.Equals(a.HotkeyString, "CONTROLBAR:UpgradeChinaSatelliteHackOne", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a.IconName, "PRCSatelliteHack1", StringComparison.OrdinalIgnoreCase));
        }

        return -1;
    }

    private static HotkeyAction CreateAction(TechTreeActionJson actJson, CsfFile? refCsf)
    {
        var actDisplayName = actJson.IconName;
        char? defaultHk = null;

        if (refCsf != null && !string.IsNullOrEmpty(actJson.HotkeyString))
        {
            var csfVal = refCsf.GetString(actJson.HotkeyString);
            if (!string.IsNullOrWhiteSpace(csfVal))
            {
                actDisplayName = CsfFile.StripHotkey(csfVal);
                defaultHk = CsfFile.ExtractHotkey(csfVal);
            }
        }

        return new HotkeyAction
        {
            IconName = actJson.IconName,
            HotkeyString = actJson.HotkeyString,
            DisplayName = actDisplayName,
            DefaultHotkey = defaultHk,
            Hotkey = defaultHk,
        };
    }

    private static async Task<string?> LoadAssetStringAsync(string relativePath, CancellationToken cancellationToken)
    {
        var stream = TryOpenAssetStream(relativePath);
        if (stream == null)
        {
            return null;
        }

        using (stream)
        {
            using var reader = new StreamReader(stream);
            return await reader.ReadToEndAsync(cancellationToken);
        }
    }

    private static Stream? TryOpenAssetStream(string relativePath)
    {
        // 1. Try Avalonia resource loader
        try
        {
            var uri = new Uri($"avares://{GenHubFolder}/{AssetsFolder}/{GenHotkeysFolder}/{relativePath.Replace('\\', '/')}");
            if (AssetLoader.Exists(uri))
            {
                return AssetLoader.Open(uri);
            }
        }
        catch
        {
            // Ignore and fall back to filesystem
        }

        // 2. Try AppContext.BaseDirectory
        var fileOnDisk = Path.Combine(AppContext.BaseDirectory, AssetsFolder, GenHotkeysFolder, relativePath);
        if (File.Exists(fileOnDisk))
        {
            return File.OpenRead(fileOnDisk);
        }

        // 3. Try relative to project/solution directories during development
        var searchRoots = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", AssetsFolder, GenHotkeysFolder, relativePath),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", GenHubFolder, AssetsFolder, GenHotkeysFolder, relativePath),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", GenHubFolder, AssetsFolder, GenHotkeysFolder, relativePath),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", GenHubFolder, GenHubFolder, AssetsFolder, GenHotkeysFolder, relativePath),
        };

        var match = searchRoots.FirstOrDefault(File.Exists);
        return match != null ? File.OpenRead(match) : null;
    }

    private CsfFile? LoadReferenceCsf(GameType gameType)
    {
        try
        {
            var stream = TryOpenAssetStream(GenHotkeysConstants.PresetsLeikezeEn);
            if (stream != null)
            {
                using (stream)
                {
                    return CsfFile.Load(stream);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load reference CSF for {GameType}", gameType);
        }

        return null;
    }
}
